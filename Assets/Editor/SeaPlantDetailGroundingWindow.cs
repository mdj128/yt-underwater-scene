using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SwimmingTest.Editor
{
    /// <summary>
    /// Bakes painted terrain detail meshes into scene GameObjects snapped to the nearest surface.
    /// Useful for hero areas where you need grounded placement instead of Unity's detail instancing.
    /// </summary>
    public sealed class SeaPlantDetailGroundingWindow : EditorWindow
    {
        private const string DefaultDetailFolder = "Assets/Props/SeaPlants/DetailPrefabs";

        [Serializable]
        private sealed class DetailLayerEntry
        {
            public int index;
            public string name;
            public string assetPath;
            public bool enabled;

            public DetailLayerEntry(int index, string name, string assetPath)
            {
                this.index = index;
                this.name = name;
                this.assetPath = assetPath;
                enabled = true;
            }
        }

        private Terrain _terrain;
        private readonly List<DetailLayerEntry> _detailLayers = new();
        private bool _filterToSeaPlantFolder = true;
        private float _raycastStartHeight = 4f;
        private float _raycastDistance = 12f;
        private float _pushIntoSurface = 0.02f;
        private bool _alignToSurfaceNormal = true;
        private LayerMask _raycastMask = ~0;
        private Transform _parentOverride;
        private bool _clearDetailCells = true;
        private bool _randomizeYaw = true;

        [MenuItem("Tools/Sea Plants/Ground Painted Details", priority = 310)]
        public static void OpenWindow()
        {
            GetWindow<SeaPlantDetailGroundingWindow>("Ground Details").Show();
        }

        private void OnEnable()
        {
            if (_terrain == null)
            {
                TryAssignTerrainFromSelection();
            }
            RefreshLayerList();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Terrain Source", EditorStyles.boldLabel);
                var picked = (Terrain)EditorGUILayout.ObjectField("Terrain", _terrain, typeof(Terrain), true);
                if (picked != _terrain)
                {
                    _terrain = picked;
                    RefreshLayerList();
                }

                if (GUILayout.Button("Use Selected Terrain"))
                {
                    TryAssignTerrainFromSelection();
                    RefreshLayerList();
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Detail Layers", EditorStyles.boldLabel);
                _filterToSeaPlantFolder = EditorGUILayout.ToggleLeft($"Only show '{DefaultDetailFolder}'", _filterToSeaPlantFolder);

                if (GUILayout.Button("Refresh Layers"))
                {
                    RefreshLayerList();
                }

                if (_detailLayers.Count == 0)
                {
                    EditorGUILayout.HelpBox("No mesh-based detail layers found. Make sure the terrain uses the sea plant detail prefabs.", MessageType.Info);
                }
                else
                {
                    foreach (var entry in _detailLayers)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(18));
                            EditorGUILayout.LabelField($"[{entry.index}] {entry.name}", GUILayout.MinWidth(150));
                            EditorGUILayout.LabelField(entry.assetPath, EditorStyles.miniLabel);
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Bake Settings", EditorStyles.boldLabel);
                _parentOverride = (Transform)EditorGUILayout.ObjectField("Parent Override", _parentOverride, typeof(Transform), true);
                _raycastMask = LayerMaskField("Raycast Mask", _raycastMask);
                _raycastStartHeight = EditorGUILayout.Slider("Raycast Start Height", _raycastStartHeight, 0.2f, 15f);
                _raycastDistance = EditorGUILayout.Slider("Raycast Distance", _raycastDistance, 1f, 30f);
                _pushIntoSurface = EditorGUILayout.Slider("Embed Depth", _pushIntoSurface, 0f, 0.2f);
                _alignToSurfaceNormal = EditorGUILayout.ToggleLeft("Align To Surface Normal", _alignToSurfaceNormal);
                _randomizeYaw = EditorGUILayout.ToggleLeft("Randomize Yaw", _randomizeYaw);
                _clearDetailCells = EditorGUILayout.ToggleLeft("Clear Terrain Detail Cells After Bake", _clearDetailCells);
                EditorGUILayout.HelpBox("This converts selected detail instances into regular GameObjects snapped to colliders/terrain. Use for foreground areas where floating is noticeable.", MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _terrain != null && _detailLayers.Exists(l => l.enabled);
                if (GUILayout.Button("Bake Selected Details", GUILayout.Height(30)))
                {
                    BakeSelectedDetails();
                }
                GUI.enabled = true;
            }
        }

        private void BakeSelectedDetails()
        {
            if (_terrain == null || _terrain.terrainData == null)
            {
                EditorUtility.DisplayDialog("Ground Details", "Select a valid terrain before baking.", "Close");
                return;
            }

            var data = _terrain.terrainData;
            var prototypes = data.detailPrototypes;
            if (prototypes == null || prototypes.Length == 0)
            {
                EditorUtility.DisplayDialog("Ground Details", "The terrain has no detail prototypes to bake.", "Close");
                return;
            }

            var enabledLayers = new List<DetailLayerEntry>();
            foreach (var entry in _detailLayers)
            {
                if (entry.enabled)
                {
                    enabledLayers.Add(entry);
                }
            }

            if (enabledLayers.Count == 0)
            {
                EditorUtility.DisplayDialog("Ground Details", "Enable at least one detail layer to bake.", "Close");
                return;
            }

            Transform parent = _parentOverride;
            if (parent == null)
            {
                var parentGO = new GameObject($"{_terrain.name}_BakedSeaPlants");
                Undo.RegisterCreatedObjectUndo(parentGO, "Create Baked Sea Plant Parent");
                parentGO.transform.SetParent(_terrain.transform, false);
                parent = parentGO.transform;
            }

            Undo.RegisterCompleteObjectUndo(data, "Bake Sea Plant Details");

            try
            {
                int detailWidth = data.detailWidth;
                int detailHeight = data.detailHeight;
                Vector3 terrainPosition = _terrain.transform.position;
                Vector3 terrainSize = data.size;
                int totalCells = detailWidth * detailHeight * enabledLayers.Count;
                int processedCells = 0;

                foreach (var layer in enabledLayers)
                {
                    if (layer.index < 0 || layer.index >= prototypes.Length)
                    {
                        continue;
                    }

                    var prototype = prototypes[layer.index];
                    if (!prototype.usePrototypeMesh || prototype.prototype == null)
                    {
                        continue;
                    }

                    var detailLayer = data.GetDetailLayer(0, 0, detailWidth, detailHeight, layer.index);
                    bool layerCleared = false;

                    for (int y = 0; y < detailHeight; y++)
                    {
                        for (int x = 0; x < detailWidth; x++)
                        {
                            processedCells++;
                            if (EditorUtility.DisplayCancelableProgressBar("Ground Sea Plant Details",
                                    $"Layer {layer.name} ({processedCells}/{totalCells})",
                                    processedCells / (float)totalCells))
                            {
                                EditorUtility.ClearProgressBar();
                                return;
                            }

                            int count = detailLayer[y, x];
                            if (count == 0)
                            {
                                continue;
                            }

                            for (int i = 0; i < count; i++)
                            {
                                Vector3 randomOffset = new Vector3(UnityEngine.Random.value, 0f, UnityEngine.Random.value);
                                Vector3 localPos = new Vector3(
                                    (x + randomOffset.x) / detailWidth * terrainSize.x,
                                    0f,
                                    (y + randomOffset.z) / detailHeight * terrainSize.z);

                                Vector3 worldPos = terrainPosition + localPos;
                                Vector3 rayStart = worldPos + Vector3.up * _raycastStartHeight;

                                Vector3 hitPoint;
                                Vector3 hitNormal;

                                if (Physics.Raycast(rayStart, Vector3.down, out var hit, _raycastStartHeight + _raycastDistance, _raycastMask, QueryTriggerInteraction.Ignore))
                                {
                                    hitPoint = hit.point - hit.normal * _pushIntoSurface;
                                    hitNormal = hit.normal;
                                }
                                else
                                {
                                    float sampledHeight = _terrain.SampleHeight(worldPos) + terrainPosition.y;
                                    hitPoint = new Vector3(worldPos.x, sampledHeight - _pushIntoSurface, worldPos.z);
                                    hitNormal = _terrain.terrainData.GetInterpolatedNormal(
                                        (x + 0.5f) / detailWidth,
                                        (y + 0.5f) / detailHeight);
                                }

                                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prototype.prototype);
                                Undo.RegisterCreatedObjectUndo(instance, "Instantiate Sea Plant Detail");

                                instance.transform.SetParent(parent, true);
                                instance.transform.position = hitPoint;

                                if (_alignToSurfaceNormal)
                                {
                                    instance.transform.rotation = Quaternion.FromToRotation(Vector3.up, hitNormal);
                                }

                                if (_randomizeYaw)
                                {
                                    instance.transform.Rotate(Vector3.up, UnityEngine.Random.Range(0f, 360f), Space.World);
                                }
                            }

                            if (_clearDetailCells)
                            {
                                detailLayer[y, x] = 0;
                                layerCleared = true;
                            }
                        }
                    }

                    if (layerCleared)
                    {
                        data.SetDetailLayer(0, 0, layer.index, detailLayer);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorSceneManager.MarkSceneDirty(_terrain.gameObject.scene);
        }

        private void RefreshLayerList()
        {
            _detailLayers.Clear();
            if (_terrain == null || _terrain.terrainData == null)
            {
                return;
            }

            var prototypes = _terrain.terrainData.detailPrototypes;
            if (prototypes == null || prototypes.Length == 0)
            {
                return;
            }

            for (int i = 0; i < prototypes.Length; i++)
            {
                var proto = prototypes[i];
                if (!proto.usePrototypeMesh || proto.prototype == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(proto.prototype);
                if (_filterToSeaPlantFolder && !IsSeaPlantDetailPrefab(path))
                {
                    continue;
                }

                _detailLayers.Add(new DetailLayerEntry(i, proto.prototype.name, path));
            }

            _detailLayers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            Repaint();
        }

        private void TryAssignTerrainFromSelection()
        {
            _terrain = null;
            if (Selection.activeGameObject != null)
            {
                _terrain = Selection.activeGameObject.GetComponent<Terrain>();
                if (_terrain == null && Selection.activeGameObject.TryGetComponent(out TerrainCollider collider))
                {
                    _terrain = collider.GetComponent<Terrain>();
                }
            }

            if (_terrain == null)
            {
                _terrain = Terrain.activeTerrain;
            }
        }

        private static bool IsSeaPlantDetailPrefab(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string normalized = assetPath.Replace('\\', '/');
            string folder = DefaultDetailFolder.Replace('\\', '/');
            return normalized.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }

        private static LayerMask LayerMaskField(string label, LayerMask selected)
        {
            var layers = UnityEditorInternal.InternalEditorUtility.layers;
            var layerNumbers = new List<int>();
            for (int i = 0; i < layers.Length; i++)
            {
                layerNumbers.Add(LayerMask.NameToLayer(layers[i]));
            }

            int maskWithoutEmpty = 0;
            for (int i = 0; i < layerNumbers.Count; i++)
            {
                if (((1 << layerNumbers[i]) & selected.value) > 0)
                {
                    maskWithoutEmpty |= 1 << i;
                }
            }

            maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers);

            int mask = 0;
            for (int i = 0; i < layerNumbers.Count; i++)
            {
                if ((maskWithoutEmpty & (1 << i)) > 0)
                {
                    mask |= 1 << layerNumbers[i];
                }
            }

            selected.value = mask;
            return selected;
        }
    }
}

