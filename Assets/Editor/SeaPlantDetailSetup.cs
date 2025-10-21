using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace SwimmingTest.Editor
{
    internal static class SeaPlantDetailSetup
    {
        private const string DetailShaderName = "Sea/Terrain/SeaPlantDetail";
        private const string RootFolder = "Assets/Props/SeaPlants";
        private const string MeshFolder = RootFolder + "/DetailMeshes";
        private const string MaterialFolder = RootFolder + "/DetailMaterials";
        private const string PrefabFolder = RootFolder + "/DetailPrefabs";

        [MenuItem("Tools/Sea Plants/Generate Terrain Detail Assets", priority = 300)]
        private static void GenerateTerrainDetailAssets()
        {
            var shader = Shader.Find(DetailShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Sea Plant Terrain Details",
                    $"Shader '{DetailShaderName}' could not be found. Make sure the shader asset exists in the project.",
                    "Close");
                return;
            }

            var selection = Selection.GetFiltered<GameObject>(SelectionMode.Assets);
            if (selection == null || selection.Length == 0)
            {
                EditorUtility.DisplayDialog("Sea Plant Terrain Details",
                    "Select one or more sea plant prefabs in the Project window before running this command.",
                    "Close");
                return;
            }

            EnsureFolderPath(RootFolder);
            EnsureFolderPath(MeshFolder);
            EnsureFolderPath(MaterialFolder);
            EnsureFolderPath(PrefabFolder);

            var createdAssets = new List<string>();

            foreach (var prefabAsset in selection)
            {
                var prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    continue;
                }

                var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (prefabRoot == null)
                {
                    continue;
                }

                try
                {
                    var lodGroup = prefabRoot.GetComponentInChildren<LODGroup>();
                    Renderer[] sourceRenderers;
                    if (lodGroup != null && lodGroup.lodCount > 0)
                    {
                        var lods = lodGroup.GetLODs();
                        sourceRenderers = lods.Length > 0
                            ? lods[0].renderers.Where(r => r != null).ToArray()
                            : lodGroup.GetComponentsInChildren<Renderer>();
                    }
                    else
                    {
                        sourceRenderers = prefabRoot.GetComponentsInChildren<Renderer>();
                    }

                    var meshFilters = sourceRenderers
                        .Select(r => r.GetComponent<MeshFilter>())
                        .Where(mf => mf != null && mf.sharedMesh != null)
                        .ToArray();

                    if (meshFilters.Length == 0)
                    {
                        Debug.LogWarning($"[SeaPlantDetailSetup] No MeshFilter found in LOD0 for '{prefabAsset.name}'. Skipping.");
                        continue;
                    }

                    var combineInstances = new CombineInstance[meshFilters.Length];
                    for (int i = 0; i < meshFilters.Length; i++)
                    {
                        combineInstances[i] = new CombineInstance
                        {
                            mesh = meshFilters[i].sharedMesh,
                            transform = meshFilters[i].transform.localToWorldMatrix
                        };
                    }

                    var detailMesh = new Mesh
                    {
                        name = $"{prefabAsset.name}_DetailMesh"
                    };
                    detailMesh.CombineMeshes(combineInstances, mergeSubMeshes: true, useMatrices: true, hasLightmapData: false);
                    detailMesh.RecalculateBounds();
                    MeshUtility.Optimize(detailMesh);

                    var meshAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{MeshFolder}/{detailMesh.name}.asset");
                    AssetDatabase.CreateAsset(detailMesh, meshAssetPath);

                    var sourceMaterial = sourceRenderers.SelectMany(r => r.sharedMaterials ?? new Material[0])
                        .FirstOrDefault(mat => mat != null);
                    var detailMaterial = new Material(shader)
                    {
                        name = $"{prefabAsset.name}_Detail"
                    };

                    if (sourceMaterial != null)
                    {
                        if (sourceMaterial.HasProperty("_BaseMap"))
                        {
                            detailMaterial.SetTexture("_MainTex", sourceMaterial.GetTexture("_BaseMap"));
                        }
                        else if (sourceMaterial.HasProperty("_MainTex"))
                        {
                            detailMaterial.SetTexture("_MainTex", sourceMaterial.GetTexture("_MainTex"));
                        }

                        if (sourceMaterial.HasProperty("_BaseColor"))
                        {
                            detailMaterial.SetColor("_Color", sourceMaterial.GetColor("_BaseColor"));
                        }
                        else if (sourceMaterial.HasProperty("_Color"))
                        {
                            detailMaterial.SetColor("_Color", sourceMaterial.GetColor("_Color"));
                        }

                        if (sourceMaterial.HasProperty("_Cutoff"))
                        {
                            detailMaterial.SetFloat("_Cutoff", sourceMaterial.GetFloat("_Cutoff"));
                        }
                    }

                    var materialAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/{detailMaterial.name}.mat");
                    AssetDatabase.CreateAsset(detailMaterial, materialAssetPath);

                    var previewObject = new GameObject(prefabAsset.name + "_DetailPreview");
                    try
                    {
                        var filter = previewObject.AddComponent<MeshFilter>();
                        filter.sharedMesh = detailMesh;
                        var renderer = previewObject.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = detailMaterial;

                        var prefabAssetPathDetail = AssetDatabase.GenerateUniqueAssetPath($"{PrefabFolder}/{prefabAsset.name}_Detail.prefab");
                        PrefabUtility.SaveAsPrefabAsset(previewObject, prefabAssetPathDetail);
                        createdAssets.Add($"{prefabAsset.name} -> Mesh: {meshAssetPath}, Material: {materialAssetPath}, Prefab: {prefabAssetPathDetail}");
                    }
                    finally
                    {
                        Object.DestroyImmediate(previewObject);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (createdAssets.Count > 0)
            {
                Debug.Log("[SeaPlantDetailSetup] Generated terrain detail assets:\n" + string.Join("\n", createdAssets));
            }
            else
            {
                Debug.LogWarning("[SeaPlantDetailSetup] No terrain detail assets were generated.");
            }
        }

        private static void EnsureFolderPath(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var segments = path.Split('/');
            var current = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                var next = segments[i];
                var combined = $"{current}/{next}";
                if (!AssetDatabase.IsValidFolder(combined))
                {
                    AssetDatabase.CreateFolder(current, next);
                }
                current = combined;
            }
        }
    }
}
