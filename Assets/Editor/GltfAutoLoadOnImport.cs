using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class GltfAutoLodToPrefab : AssetPostprocessor
{
    // LOD thresholds (LOD0..)
    static readonly float[] screenHeights = { 0.6f, 0.3f, 0.12f, 0.04f };
    static readonly Regex lodRegex = new Regex(@"^(?<base>.+)_LOD(?<lvl>\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static bool IsGltf(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".glb" || ext == ".gltf";
    }

    static void OnPostprocessAllAssets(
        string[] importedAssets, string[] deletedAssets,
        string[] movedAssets, string[] movedFromAssetPaths)
    {
        foreach (var path in importedAssets)
        {
            if (!IsGltf(path)) continue;

            // Load the main GameObject that glTFast creates
            var main = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!main) continue;

            // Instantiate in memory (not in scene)
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(main);
            if (!instance) continue;

            bool modified = BuildLodsInHierarchy(instance.transform);

            // Save as a prefab *next to* the .glb
            if (modified)
            {
                var dir = Path.GetDirectoryName(path).Replace("\\", "/");
                var baseName = Path.GetFileNameWithoutExtension(path);
                var prefabPath = $"{dir}/{baseName}.prefab"; // overwrite if already exists

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool success);
                if (success)
                    Debug.Log($"[GltfAutoLOD] Saved prefab with LODGroups: {prefabPath}");
                else
                    Debug.LogWarning($"[GltfAutoLOD] Failed to save prefab for: {path}");
            }

            // Clean up
            Object.DestroyImmediate(instance);
        }
    }

    // Scans for siblings named *_LOD0, *_LOD1, ... under the same parent and
    // creates/updates a LODGroup on that parent.
    static bool BuildLodsInHierarchy(Transform root)
    {
        bool any = false;
        var parents = root.GetComponentsInChildren<Transform>(true);
        foreach (var parent in parents)
        {
            var buckets = new SortedDictionary<int, List<Renderer>>();
            string baseName = null;

            foreach (Transform child in parent)
            {
                var m = lodRegex.Match(child.name);
                if (!m.Success) continue;

                baseName ??= m.Groups["base"].Value;
                if (!int.TryParse(m.Groups["lvl"].Value, out int lvl)) continue;

                if (!buckets.ContainsKey(lvl))
                    buckets[lvl] = new List<Renderer>();

                var rs = child.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0) buckets[lvl].AddRange(rs);
            }

            if (buckets.Count == 0) continue;

            var lodGroup = parent.GetComponent<LODGroup>();
            if (!lodGroup) lodGroup = parent.gameObject.AddComponent<LODGroup>();
            lodGroup.fadeMode = LODFadeMode.CrossFade;

            var lods = new List<LOD>();
            int maxLvl = -1; foreach (var k in buckets.Keys) maxLvl = Mathf.Max(maxLvl, k);

            for (int lvl = 0; lvl <= maxLvl; lvl++)
            {
                if (!buckets.TryGetValue(lvl, out var rs) || rs.Count == 0) continue;
                float h = (lvl < screenHeights.Length) ? screenHeights[lvl] : Mathf.Max(0.01f, 0.6f / (lvl + 1));
                lods.Add(new LOD(h, rs.ToArray()));
            }

            if (lods.Count > 0)
            {
                lodGroup.SetLODs(lods.ToArray());
                lodGroup.RecalculateBounds();
                any = true;
            }
        }
        return any;
    }
}
