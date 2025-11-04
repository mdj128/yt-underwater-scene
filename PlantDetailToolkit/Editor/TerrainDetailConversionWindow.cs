using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Plant.TerrainDetails.Editor
{
    internal enum DetailAnimationPreset
    {
        UnderwaterSeaPlant,
        Grass
    }

    /// <summary>
    /// Editor window that converts static prefab assets into terrain detail meshes, materials, and prefabs.
    /// </summary>
    public sealed class TerrainDetailConversionWindow : EditorWindow
    {
        private const string DefaultShaderName = "Plant/Terrain/PlantDetail";
        private const string EditorPrefsKey = "Plant.TerrainDetails.ConversionSettings";

        [Serializable]
        private sealed class WindowState
        {
            public string outputRoot;
            public string shaderPath;
            public bool generateMesh;
            public bool generateMaterial;
            public bool generatePrefab;
            public bool mergeLods;
            public bool inheritMaterialProperties;
            public bool logVerbose;
            public string animationPreset;
        }

        [SerializeField] private Shader _detailShader;
        [SerializeField] private string _outputRoot = "Assets/TerrainDetails";
        [SerializeField] private bool _generateMesh = true;
        [SerializeField] private bool _generateMaterial = true;
        [SerializeField] private bool _generatePrefab = true;
        [SerializeField] private bool _mergeLods = true;
        [SerializeField] private bool _inheritMaterialProperties = true;
        [SerializeField] private bool _logVerbose = false;
        [SerializeField] private DetailAnimationPreset _animationPreset = DetailAnimationPreset.UnderwaterSeaPlant;

        private readonly List<UnityEngine.Object> _targets = new();
        private Vector2 _scroll;
        private string _status;

        [MenuItem("Tools/Terrain Details/Convert Mesh To Terrain Detail", priority = 300)]
        public static void Open()
        {
            GetWindow<TerrainDetailConversionWindow>("Terrain Detail Converter").Show();
        }

        private void OnEnable()
        {
            LoadState();
            if (_detailShader == null)
            {
                _detailShader = Shader.Find(DefaultShaderName);
            }

            RefreshSelection();
        }

        private void OnDisable()
        {
            SaveState();
        }

        private void OnSelectionChange()
        {
            RefreshSelection();
            Repaint();
        }

        private void RefreshSelection()
        {
            _targets.Clear();
            var selected = Selection.objects;
            if (selected == null)
            {
                return;
            }

            var seen = new HashSet<UnityEngine.Object>();
            foreach (var obj in selected)
            {
                if (obj == null)
                {
                    continue;
                }

                if (obj is DefaultAsset asset && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(asset)))
                {
                    continue;
                }

                if (IsSupportedAsset(obj))
                {
                    if (seen.Add(obj))
                    {
                        _targets.Add(obj);
                    }
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab)
                {
                    if (seen.Add(prefab))
                    {
                        _targets.Add(prefab);
                    }
                    continue;
                }

                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh != null)
                {
                    if (seen.Add(mesh))
                    {
                        _targets.Add(mesh);
                    }
                }
            }
        }

        private static bool IsSupportedAsset(UnityEngine.Object obj)
        {
            if (obj is GameObject go)
            {
                return PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab;
            }

            if (obj is Mesh)
            {
                return true;
            }

            return false;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Terrain Detail Conversion", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select one or more assets that contain static meshes (FBX, GLB, prefabs, or individual mesh assets), then convert them into terrain detail meshes, materials, and prefabs.", MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _outputRoot = EditorGUILayout.TextField("Output Root", _outputRoot);
                    if (GUILayout.Button("Pick…", GUILayout.Width(60)))
                    {
                        var picked = EditorUtility.OpenFolderPanel("Choose Output Folder", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(picked) && TryMakeProjectRelative(picked, out var relative))
                        {
                            _outputRoot = relative;
                        }
                    }
                }

                EditorGUILayout.HelpBox("Assets will be generated under subfolders named 'Meshes', 'Materials', and 'Prefabs' inside the output root.", MessageType.None);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Conversion Options", EditorStyles.boldLabel);
                _detailShader = (Shader)EditorGUILayout.ObjectField("Detail Shader", _detailShader, typeof(Shader), false);
                _animationPreset = (DetailAnimationPreset)EditorGUILayout.EnumPopup("Animation Preset", _animationPreset);
                _mergeLods = EditorGUILayout.ToggleLeft("Collapse LODGroup to first LOD", _mergeLods);
                _inheritMaterialProperties = EditorGUILayout.ToggleLeft("Copy base texture/color from source material", _inheritMaterialProperties);

                EditorGUILayout.Space(4);
                _generateMesh = EditorGUILayout.ToggleLeft("Create combined mesh asset", _generateMesh);
                _generateMaterial = EditorGUILayout.ToggleLeft("Create material asset", _generateMaterial);
                _generatePrefab = EditorGUILayout.ToggleLeft("Create preview prefab", _generatePrefab);

                EditorGUILayout.Space(4);
                _logVerbose = EditorGUILayout.ToggleLeft("Log verbose output", _logVerbose);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Prefabs To Convert", EditorStyles.boldLabel);
                if (_targets.Count == 0)
                {
                    EditorGUILayout.HelpBox("No prefab assets selected. Select prefabs in the Project window.", MessageType.Warning);
                }
                else
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(80));
                    foreach (var target in _targets)
                    {
                        EditorGUILayout.ObjectField(target, typeof(UnityEngine.Object), false);
                    }
                    EditorGUILayout.EndScrollView();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUI.enabled = _targets.Count > 0 && _detailShader != null && !string.IsNullOrEmpty(_outputRoot);
                if (GUILayout.Button("Convert Selected", GUILayout.Height(28), GUILayout.Width(180)))
                {
                    ConvertSelection();
                }
                GUI.enabled = true;
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.None);
            }
        }

        private void ConvertSelection()
        {
            if (!TerrainDetailGenerator.ValidateOutputRoot(_outputRoot, out var validationMessage))
            {
                _status = validationMessage;
                Debug.LogError(validationMessage);
                return;
            }

            var options = new TerrainDetailGenerator.Options
            {
                OutputRoot = _outputRoot,
                DetailShader = _detailShader,
                CollapseLods = _mergeLods,
                CopyMaterialProperties = _inheritMaterialProperties,
                GenerateMesh = _generateMesh,
                GenerateMaterial = _generateMaterial,
                GeneratePrefab = _generatePrefab,
                AnimationPreset = _animationPreset,
                Verbose = _logVerbose
            };

            var results = new List<TerrainDetailGenerator.Result>();
            foreach (var target in _targets)
            {
                if (target == null)
                {
                    continue;
                }

                if (TerrainDetailGenerator.TryGenerate(target, options, out var result))
                {
                    results.Add(result);
                }
            }

            if (results.Count == 0)
            {
                _status = "No terrain detail assets were generated. Check the Console for warnings.";
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _status = "Generated detail assets:\n" + string.Join("\n", results.Select(r => r.ToString()));
            if (_logVerbose)
            {
                foreach (var r in results)
                {
                    Debug.Log(r.ToString());
                }
            }
        }

        private void LoadState()
        {
            var json = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                var state = JsonUtility.FromJson<WindowState>(json);
                if (state == null)
                {
                    return;
                }

                _outputRoot = string.IsNullOrEmpty(state.outputRoot) ? _outputRoot : state.outputRoot;
                _generateMesh = state.generateMesh;
                _generateMaterial = state.generateMaterial;
                _generatePrefab = state.generatePrefab;
                _mergeLods = state.mergeLods;
                _inheritMaterialProperties = state.inheritMaterialProperties;
                _logVerbose = state.logVerbose;
                if (!string.IsNullOrEmpty(state.animationPreset) && Enum.TryParse(state.animationPreset, out DetailAnimationPreset preset))
                {
                    _animationPreset = preset;
                }

                if (!string.IsNullOrEmpty(state.shaderPath))
                {
                    _detailShader = AssetDatabase.LoadAssetAtPath<Shader>(state.shaderPath);
                }

                if (_detailShader == null)
                {
                    _detailShader = Shader.Find(DefaultShaderName);
                }
            }
            catch
            {
                // Ignore corrupted state
            }
        }

        private void SaveState()
        {
            try
            {
                var state = new WindowState
                {
                    outputRoot = _outputRoot,
                    shaderPath = _detailShader != null ? AssetDatabase.GetAssetPath(_detailShader) : null,
                    generateMesh = _generateMesh,
                    generateMaterial = _generateMaterial,
                    generatePrefab = _generatePrefab,
                    mergeLods = _mergeLods,
                    inheritMaterialProperties = _inheritMaterialProperties,
                    logVerbose = _logVerbose,
                    animationPreset = _animationPreset.ToString()
                };

                var json = JsonUtility.ToJson(state, false);
                EditorPrefs.SetString(EditorPrefsKey, json);
            }
            catch
            {
                // Ignore serialization issues
            }
        }

        private static bool TryMakeProjectRelative(string fullPath, out string relativePath)
        {
            fullPath = fullPath.Replace('\\', '/');
            string projectPath = Path.GetFullPath(Application.dataPath + "/../").Replace('\\', '/');

            if (!fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = null;
                return false;
            }

            relativePath = fullPath.Substring(projectPath.Length);
            if (!relativePath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = null;
                return false;
            }

            relativePath = relativePath.TrimEnd('/');
            return true;
        }
    }

    internal static class TerrainDetailGenerator
    {
        internal sealed class Options
        {
            public string OutputRoot;
            public Shader DetailShader;
            public bool CollapseLods = true;
            public bool CopyMaterialProperties = true;
            public bool GenerateMesh = true;
            public bool GenerateMaterial = true;
            public bool GeneratePrefab = true;
            public DetailAnimationPreset AnimationPreset = DetailAnimationPreset.UnderwaterSeaPlant;
            public bool Verbose;
        }

        internal sealed class Result
        {
            public string SourceName;
            public string MeshPath;
            public string MaterialPath;
            public string PrefabPath;

            public override string ToString()
            {
                var parts = new List<string> { SourceName };
                if (!string.IsNullOrEmpty(MeshPath)) parts.Add($"Mesh: {MeshPath}");
                if (!string.IsNullOrEmpty(MaterialPath)) parts.Add($"Mat: {MaterialPath}");
                if (!string.IsNullOrEmpty(PrefabPath)) parts.Add($"Prefab: {PrefabPath}");
                return string.Join(" | ", parts);
            }
        }

        private const string MeshFolderName = "Meshes";
        private const string MaterialFolderName = "Materials";
        private const string PrefabFolderName = "Prefabs";

        internal static bool ValidateOutputRoot(string outputRoot, out string message)
        {
            message = null;
            if (string.IsNullOrEmpty(outputRoot))
            {
                message = "Output root cannot be empty.";
                return false;
            }

            if (!outputRoot.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                message = "Output root must be inside the project's Assets folder.";
                return false;
            }

            return true;
        }

        internal static bool TryGenerate(UnityEngine.Object target, Options options, out Result result)
        {
            result = null;

            if (options.DetailShader == null)
            {
                Debug.LogWarning("[TerrainDetailGenerator] Detail shader is not assigned.");
                return false;
            }

            using var context = SourceContext.Create(target);
            if (context == null || context.Root == null)
            {
                Debug.LogWarning($"[TerrainDetailGenerator] Unsupported asset type '{target}'. Select prefabs, FBX/GLB model assets, or mesh assets.");
                return false;
            }

            var renderers = CollectRenderers(context.Root, options.CollapseLods);
            if (renderers.Count == 0)
            {
                Debug.LogWarning($"[TerrainDetailGenerator] No mesh renderers found in '{context.SourceName}'.");
                return false;
            }

            var meshFilters = new List<MeshFilter>();
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    meshFilters.Add(filter);
                }
            }

            if (meshFilters.Count == 0)
            {
                Debug.LogWarning($"[TerrainDetailGenerator] No MeshFilter components with valid meshes were found in '{context.SourceName}'.");
                return false;
            }

            Mesh detailMesh = null;
            string meshAssetPath = null;
            if (options.GenerateMesh)
            {
                detailMesh = BuildCombinedMesh(context.SourceName, meshFilters);
                meshAssetPath = WriteMeshAsset(detailMesh, options.OutputRoot);
            }
            else
            {
                detailMesh = meshFilters[0].sharedMesh;
            }

            Material detailMaterial = null;
            string materialAssetPath = null;
            if (options.GenerateMaterial)
            {
                detailMaterial = CreateMaterial(context.SourceName, options.DetailShader, renderers, options.CopyMaterialProperties, options.AnimationPreset);
                materialAssetPath = WriteMaterialAsset(detailMaterial, options.OutputRoot);
            }
            else
            {
                detailMaterial = renderers.SelectMany(r => r.sharedMaterials ?? Array.Empty<Material>()).FirstOrDefault(m => m != null);
            }

            string prefabAssetPath = null;
            if (options.GeneratePrefab)
            {
                prefabAssetPath = CreateDetailPrefab(context.SourceName, detailMesh, detailMaterial, options.OutputRoot);
            }

            result = new Result
            {
                SourceName = context.SourceName,
                MeshPath = meshAssetPath,
                MaterialPath = materialAssetPath,
                PrefabPath = prefabAssetPath
            };

            return true;
        }

        private sealed class SourceContext : IDisposable
        {
            public GameObject Root { get; }
            public string SourceName { get; }
            public string AssetPath { get; }

            private readonly Action _cleanup;

            private SourceContext(GameObject root, string sourceName, string assetPath, Action cleanup)
            {
                Root = root;
                SourceName = sourceName;
                AssetPath = assetPath;
                _cleanup = cleanup;
            }

            public static SourceContext Create(UnityEngine.Object target)
            {
                if (target == null)
                {
                    return null;
                }

                var assetPath = AssetDatabase.GetAssetPath(target);

                if (target is Mesh mesh)
                {
                    var temp = new GameObject(mesh.name + "_Source")
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    var filter = temp.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    temp.AddComponent<MeshRenderer>();
                    return new SourceContext(temp, mesh.name, assetPath, () => UnityEngine.Object.DestroyImmediate(temp));
                }

                var prefab = ResolvePrefabAsset(target);
                if (prefab != null)
                {
                    var path = AssetDatabase.GetAssetPath(prefab);
                    if (!string.IsNullOrEmpty(path))
                    {
                        var prefabRoot = PrefabUtility.LoadPrefabContents(path);
                        if (prefabRoot != null)
                        {
                            return new SourceContext(prefabRoot, prefab.name, path, () => PrefabUtility.UnloadPrefabContents(prefabRoot));
                        }
                    }
                }

                if (target is GameObject go)
                {
                    var clone = UnityEngine.Object.Instantiate(go);
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    return new SourceContext(clone, go.name, assetPath, () => UnityEngine.Object.DestroyImmediate(clone));
                }

                return null;
            }

            public void Dispose()
            {
                _cleanup?.Invoke();
            }
        }

        private static GameObject ResolvePrefabAsset(UnityEngine.Object target)
        {
            switch (target)
            {
                case GameObject go when PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab:
                    return go;
                default:
                    var path = AssetDatabase.GetAssetPath(target);
                    if (!string.IsNullOrEmpty(path))
                    {
                        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    }
                    break;
            }

            return null;
        }

        private static List<Renderer> CollectRenderers(GameObject root, bool collapseLods)
        {
            if (!collapseLods)
            {
                return root.GetComponentsInChildren<Renderer>(true).ToList();
            }

            var lodGroup = root.GetComponentInChildren<LODGroup>();
            if (lodGroup != null && lodGroup.lodCount > 0)
            {
                var lods = lodGroup.GetLODs();
                if (lods.Length > 0 && lods[0].renderers != null)
                {
                    return lods[0].renderers.Where(r => r != null).ToList();
                }
            }

            return root.GetComponentsInChildren<Renderer>(true).ToList();
        }

        private static Mesh BuildCombinedMesh(string prefabName, List<MeshFilter> meshFilters)
        {
            var combineInstances = new CombineInstance[meshFilters.Count];
            for (int i = 0; i < meshFilters.Count; i++)
            {
                combineInstances[i] = new CombineInstance
                {
                    mesh = meshFilters[i].sharedMesh,
                    transform = meshFilters[i].transform.localToWorldMatrix
                };
            }

            var detailMesh = new Mesh
            {
                name = prefabName + "_DetailMesh"
            };

            detailMesh.CombineMeshes(combineInstances, mergeSubMeshes: true, useMatrices: true, hasLightmapData: false);
            detailMesh.RecalculateBounds();
            MeshUtility.Optimize(detailMesh);
            return detailMesh;
        }

        private static string WriteMeshAsset(Mesh mesh, string outputRoot)
        {
            EnsureFolderStructure(outputRoot, MeshFolderName, out var folderPath);
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folderPath, mesh.name + ".asset").Replace('\\', '/'));
            AssetDatabase.CreateAsset(mesh, assetPath);
            return assetPath;
        }

        private static Material CreateMaterial(string prefabName, Shader shader, IEnumerable<Renderer> renderers, bool copyProperties, DetailAnimationPreset preset)
        {
            var material = new Material(shader)
            {
                name = prefabName + "_Detail"
            };

            if (!copyProperties)
            {
                return material;
            }

            var sourceMaterial = renderers
                .SelectMany(r => r.sharedMaterials ?? Array.Empty<Material>())
                .FirstOrDefault(m => m != null);

            if (sourceMaterial == null)
            {
                return material;
            }

            CopyTextureProperty(sourceMaterial, material, "_BaseMap", "_MainTex");
            CopyColorProperty(sourceMaterial, material, "_BaseColor", "_Color");

            if (sourceMaterial.HasProperty("_Cutoff") && material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", sourceMaterial.GetFloat("_Cutoff"));
            }

            ApplyAnimationPreset(material, preset);
            return material;
        }

        private static string WriteMaterialAsset(Material material, string outputRoot)
        {
            EnsureFolderStructure(outputRoot, MaterialFolderName, out var folderPath);
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folderPath, material.name + ".mat").Replace('\\', '/'));
            AssetDatabase.CreateAsset(material, assetPath);
            return assetPath;
        }

        private static string CreateDetailPrefab(string prefabName, Mesh mesh, Material material, string outputRoot)
        {
            EnsureFolderStructure(outputRoot, PrefabFolderName, out var folderPath);
            var temp = new GameObject(prefabName + "_DetailPreview");
            try
            {
                var filter = temp.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = temp.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                var assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folderPath, prefabName + "_Detail.prefab").Replace('\\', '/'));
                PrefabUtility.SaveAsPrefabAsset(temp, assetPath);
                return assetPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        private static void EnsureFolderStructure(string outputRoot, string leafFolder, out string fullPath)
        {
            EnsureFolderPath(outputRoot);
            var combined = Path.Combine(outputRoot, leafFolder).Replace('\\', '/');
            EnsureFolderPath(combined);
            fullPath = combined;
        }

        private static void EnsureFolderPath(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var segments = path.Split('/');
            if (segments.Length <= 1)
            {
                return;
            }

            var current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                var next = segments[i];
                var combined = current + "/" + next;
                if (!AssetDatabase.IsValidFolder(combined))
                {
                    AssetDatabase.CreateFolder(current, next);
                }
                current = combined;
            }
        }

        private static void CopyTextureProperty(Material source, Material target, params string[] propertyNames)
        {
            foreach (var property in propertyNames)
            {
                if (!source.HasProperty(property))
                {
                    continue;
                }

                var texture = source.GetTexture(property);
                if (texture == null)
                {
                    continue;
                }

                if (target.HasProperty("_MainTex"))
                {
                    target.SetTexture("_MainTex", texture);
                    target.SetTextureScale("_MainTex", source.GetTextureScale(property));
                    target.SetTextureOffset("_MainTex", source.GetTextureOffset(property));
                }

                if (target.HasProperty("_BaseMap"))
                {
                    target.SetTexture("_BaseMap", texture);
                    target.SetTextureScale("_BaseMap", source.GetTextureScale(property));
                    target.SetTextureOffset("_BaseMap", source.GetTextureOffset(property));
                }

                return;
            }
        }

        private static void CopyColorProperty(Material source, Material target, params string[] propertyNames)
        {
            foreach (var property in propertyNames)
            {
                if (!source.HasProperty(property))
                {
                    continue;
                }

                var color = source.GetColor(property);
                if (target.HasProperty("_Color"))
                {
                    target.SetColor("_Color", color);
                }

                if (target.HasProperty("_BaseColor"))
                {
                    target.SetColor("_BaseColor", color);
                }

                return;
            }
        }

        private static void ApplyAnimationPreset(Material material, DetailAnimationPreset preset)
        {
            if (material == null)
            {
                return;
            }

            switch (preset)
            {
                case DetailAnimationPreset.Grass:
                    SetFloatIfExists(material, "_SwayAmplitude", 0.045f);
                    SetFloatIfExists(material, "_SwayVertical", 0.008f);
                    SetFloatIfExists(material, "_SwaySpeed", 1.6f);
                    SetFloatIfExists(material, "_SwayHeightScale", 0.9f);
                    SetFloatIfExists(material, "_SwayPhaseJitter", 0.6f);
                    SetFloatIfExists(material, "_SwayNoiseStrength", 0.2f);
                    SetFloatIfExists(material, "_SwayNoiseScale", 1.2f);
                    SetFloatIfExists(material, "_GroundSink", 0.01f);
                    SetFloatIfExists(material, "_GroundSlopeSink", 0.04f);
                    SetFloatIfExists(material, "_GroundSinkHeightScale", 1.8f);
                    break;

                default:
                    SetFloatIfExists(material, "_SwayAmplitude", 0.12f);
                    SetFloatIfExists(material, "_SwayVertical", 0.02f);
                    SetFloatIfExists(material, "_SwaySpeed", 0.8f);
                    SetFloatIfExists(material, "_SwayHeightScale", 1.4f);
                    SetFloatIfExists(material, "_SwayPhaseJitter", 1.2f);
                    SetFloatIfExists(material, "_SwayNoiseStrength", 0.35f);
                    SetFloatIfExists(material, "_SwayNoiseScale", 0.6f);
                    SetFloatIfExists(material, "_GroundSink", 0.04f);
                    SetFloatIfExists(material, "_GroundSlopeSink", 0.12f);
                    SetFloatIfExists(material, "_GroundSinkHeightScale", 3.5f);
                    break;
            }
        }

        private static void SetFloatIfExists(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
    }
}
