using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class SeaPlantPrefabUtilities
{
    private const string PrefabFolder = "Assets/Props/SeaPlants";
    private const string MaterialFolder = PrefabFolder + "/Materials";

    [MenuItem("Tools/Sea Plants/Recenter Sea Plant Prefabs")]
    public static void RecenterSeaPlantPrefabs()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab model.*_LOD_GROUP", new[] { PrefabFolder });
        if (prefabGuids.Length == 0)
        {
            Debug.LogWarning($"No sea plant prefabs found in {PrefabFolder}.");
            return;
        }

        int adjustedCount = 0;

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                if (RecenterPrefab(root, out float offsetApplied))
                {
                    adjustedCount++;
                    Debug.Log($"[SeaPlant] Recentered {path} by {offsetApplied:0.###} units.");
                }
            }
            finally
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SeaPlant] Recentered {adjustedCount}/{prefabGuids.Length} prefabs.");
    }

    private static bool RecenterPrefab(GameObject root, out float appliedOffset)
    {
        appliedOffset = 0f;

        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return false;
        }

        float minY = float.PositiveInfinity;
        foreach (var renderer in renderers)
        {
            var bounds = renderer.bounds;
            if (bounds.size == Vector3.zero)
            {
                continue;
            }

            minY = Mathf.Min(minY, bounds.min.y);
        }

        if (float.IsPositiveInfinity(minY) || Mathf.Abs(minY) < 0.001f)
        {
            return false;
        }

        foreach (Transform child in root.transform)
        {
            Vector3 localPos = child.localPosition;
            localPos.y -= minY;
            child.localPosition = localPos;
        }

        var lodGroup = root.GetComponent<LODGroup>();
        if (lodGroup != null)
        {
            Vector3 refPoint = lodGroup.localReferencePoint;
            refPoint.y -= minY;
            lodGroup.localReferencePoint = refPoint;
            EditorUtility.SetDirty(lodGroup);
        }

        appliedOffset = minY;
        EditorUtility.SetDirty(root);
        return true;
    }

    [MenuItem("Tools/Sea Plants/Convert Materials To Wobble Shader")]
    public static void ConvertMaterialsToWobbleShader()
    {
        Shader wobbleShader = Shader.Find("Custom/SeaPlantWobble");
        if (wobbleShader == null)
        {
            Debug.LogError("Custom/SeaPlantWobble shader not found. Make sure it exists and is included in the project.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder(PrefabFolder, "Materials");
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab model.*_LOD_GROUP", new[] { PrefabFolder });
        if (prefabGuids.Length == 0)
        {
            Debug.LogWarning($"No sea plant prefabs found in {PrefabFolder}.");
            return;
        }

        var materialCache = new Dictionary<string, Material>();
        int updatedPrefabs = 0;
        int materialAssignments = 0;

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                if (ApplyMaterials(root, wobbleShader, materialCache, out int rendererCount))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    updatedPrefabs++;
                    materialAssignments += rendererCount;
                    Debug.Log($"[SeaPlant] Updated materials on {rendererCount} renderers in {path}.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SeaPlant] Converted materials on {updatedPrefabs} prefabs ({materialAssignments} renderer assignments).");
    }

    private static bool ApplyMaterials(GameObject root, Shader wobbleShader, Dictionary<string, Material> cache, out int rendererCount)
    {
        rendererCount = 0;
        bool modified = false;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            var sourceRenderer = PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer);
            if (sourceRenderer == null)
            {
                continue;
            }

            var sourceMaterials = sourceRenderer.sharedMaterials;
            if (sourceMaterials == null || sourceMaterials.Length == 0)
            {
                continue;
            }

            bool rendererModified = false;
            var newMaterials = renderer.sharedMaterials;

            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                var sourceMaterial = sourceMaterials[i];
                if (sourceMaterial == null)
                {
                    continue;
                }

                string cacheKey = $"{AssetDatabase.GetAssetPath(sourceMaterial)}::{sourceMaterial.name}";
                if (!cache.TryGetValue(cacheKey, out Material wobbleMaterial) || wobbleMaterial == null)
                {
                    wobbleMaterial = CreateConvertedMaterial(sourceMaterial, wobbleShader);
                    cache[cacheKey] = wobbleMaterial;
                }

                if (i >= newMaterials.Length || newMaterials[i] != wobbleMaterial)
                {
                    if (i >= newMaterials.Length)
                    {
                        Array.Resize(ref newMaterials, sourceMaterials.Length);
                    }
                    newMaterials[i] = wobbleMaterial;
                    rendererModified = true;
                }
            }

            if (rendererModified)
            {
                renderer.sharedMaterials = newMaterials;
                EditorUtility.SetDirty(renderer);
                modified = true;
                rendererCount++;
            }
        }

        return modified;
    }

    private static Material CreateConvertedMaterial(Material sourceMaterial, Shader wobbleShader)
    {
        var newMaterial = new Material(wobbleShader)
        {
            name = $"{sourceMaterial.name}_Wobble"
        };

        bool copiedTexture = CopyTexture(sourceMaterial, newMaterial, "_BaseMap");
        if (!copiedTexture)
        {
            CopyTexture(sourceMaterial, newMaterial, "_MainTex");
        }

        if (!CopyColor(sourceMaterial, newMaterial, "_BaseColor"))
        {
            CopyColor(sourceMaterial, newMaterial, "_Color");
        }

        if (sourceMaterial.HasProperty("_Cutoff") && newMaterial.HasProperty("_Cutoff"))
        {
            newMaterial.SetFloat("_Cutoff", sourceMaterial.GetFloat("_Cutoff"));
        }

        if (newMaterial.HasProperty("_AlphaClip"))
        {
            float clipToggle = sourceMaterial.IsKeywordEnabled("_ALPHATEST_ON") ? 1f :
                (sourceMaterial.HasProperty("_AlphaClip") ? sourceMaterial.GetFloat("_AlphaClip") : newMaterial.GetFloat("_AlphaClip"));
            newMaterial.SetFloat("_AlphaClip", clipToggle);
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/{newMaterial.name}.mat");
        AssetDatabase.CreateAsset(newMaterial, assetPath);

        return newMaterial;
    }

    private static bool CopyTexture(Material source, Material target, string property)
    {
        if (source.HasProperty(property) && target.HasProperty("_BaseMap"))
        {
            var tex = source.GetTexture(property);
            if (tex != null)
            {
                var scale = source.GetTextureScale(property);
                var offset = source.GetTextureOffset(property);
                target.SetTexture("_BaseMap", tex);
                target.SetTextureScale("_BaseMap", scale);
                target.SetTextureOffset("_BaseMap", offset);
                return true;
            }
        }
        return false;
    }

    private static bool CopyColor(Material source, Material target, string property)
    {
        if (source.HasProperty(property) && target.HasProperty("_BaseColor"))
        {
            target.SetColor("_BaseColor", source.GetColor(property));
            return true;
        }
        return false;
    }
}
