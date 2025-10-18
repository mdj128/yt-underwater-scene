// Assets/Editor/AutoLodOnImport.cs
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class AutoLodOnImport : AssetPostprocessor
{
    // Adjust these to taste
    static readonly float[] screenHeights = { 0.6f, 0.3f, 0.1f, 0.02f }; // LOD0..LOD3

    // Matches base + _LOD# (case-insensitive)
    static readonly Regex lodRegex = new Regex(@"^(?<base>.+)_LOD(?<lvl>\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    void OnPostprocessModel(GameObject root)
    {
        // Find groups of siblings that share the same base name and have _LOD# children.
        // Typical hierarchies:
        //  - <Name>_LOD_GROUP
        //      - <Name>_LOD0
        //      - <Name>_LOD1
        //  OR
        //  - <Name>_LOD0, <Name>_LOD1 under the same parent

        // Search every parent under the root
        var parents = root.GetComponentsInChildren<Transform>(true);
        foreach (var parent in parents)
        {
            // Map level -> renderers
            var baseName = "";
            var lodBuckets = new SortedDictionary<int, List<Renderer>>();

            foreach (Transform child in parent)
            {
                var m = lodRegex.Match(child.name);
                if (!m.Success) continue;

                baseName = m.Groups["base"].Value;
                if (!int.TryParse(m.Groups["lvl"].Value, out int lvl)) continue;

                var renderers = new List<Renderer>();
                child.GetComponentsInChildren(true, renderers);
                if (renderers.Count == 0) continue;

                if (!lodBuckets.ContainsKey(lvl))
                    lodBuckets[lvl] = new List<Renderer>();
                lodBuckets[lvl].AddRange(renderers);
            }

            if (lodBuckets.Count == 0) continue;

            // Create or reuse LODGroup on the parent
            var lodGroupGO = parent.gameObject;
            var lodGroup = lodGroupGO.GetComponent<LODGroup>();
            if (!lodGroup) lodGroup = lodGroupGO.AddComponent<LODGroup>();
            lodGroup.fadeMode = LODFadeMode.CrossFade;

            // Build LODs in order LOD0..LODn
            var maxLvl = -1;
            foreach (var lvl in lodBuckets.Keys) maxLvl = Mathf.Max(maxLvl, lvl);
            var lods = new List<LOD>();
            for (int lvl = 0; lvl <= maxLvl; lvl++)
            {
                lodBuckets.TryGetValue(lvl, out var renderers);
                if (renderers == null || renderers.Count == 0) continue;

                float h = (lvl < screenHeights.Length) ? screenHeights[lvl] : Mathf.Max(0.01f, 0.6f / (lvl + 1));
                lods.Add(new LOD(h, renderers.ToArray()));
            }

            if (lods.Count > 0)
            {
                lodGroup.SetLODs(lods.ToArray());
                lodGroup.RecalculateBounds();
                // Optional: enable animate cross-fading on all renderers
                foreach (var lod in lods)
                    foreach (var r in lod.renderers)
                        r.allowOcclusionWhenDynamic = true;

                Debug.Log($"[AutoLOD] Created LODGroup on '{parent.name}' with {lods.Count} level(s).");
            }
        }
    }
}
