using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SwimmingTest.Editor
{
    /// <summary>
    /// Scene view brush that scatters the sea plant detail prefabs in random batches.
    /// Hold Shift while painting to erase.
    /// </summary>
    public sealed class SeaPlantDetailBatchPainterWindow : EditorWindow
    {
        private const string DefaultDetailFolder = "Assets/Props/SeaPlants/DetailPrefabs";
        private const float MinBrushRadius = 1f;
        private const float MaxBrushRadius = 20f;
        private const float MinDensity = 0.1f;
        private const float MaxDensity = 150f;
        private const float MinStrokeSpacing = 0.1f;
        private const float MaxStrokeSpacing = 5f;
        private const int MinInstancesPerSample = 1;
        private const int MaxInstancesPerSample = 6;

        [Serializable]
        private sealed class DetailLayerInfo
        {
            public int index;
            public string name;
            public string assetPath;
            public bool enabled;

            public DetailLayerInfo(int index, string name, string assetPath)
            {
                this.index = index;
                this.name = name;
                this.assetPath = assetPath;
                enabled = true;
            }
        }

        private Terrain _activeTerrain;
        private bool _followSelection = true;
        private bool _filterToDefaultFolder = true;
        private bool _paintingEnabled;
        private readonly List<DetailLayerInfo> _detailLayers = new();

        [SerializeField] private float _brushRadius = 6f;
        [SerializeField] private float _densityPerSquareMeter = 18f;
        [SerializeField] private int _maxInstancesPerCell = 4;
        [SerializeField] private float _strokeSpacing = 0.75f;
        [SerializeField] private int _instancesPerSample = 2;

        private Vector3? _lastStrokeWorldPos;
        private readonly List<DetailLayerInfo> _layerSequence = new();
        private int _layerSequenceIndex;
        private int _layerSequenceSignature;

        [MenuItem("Tools/Sea Plants/Sea Plant Batch Detail Painter", priority = 305)]
        public static void OpenWindow()
        {
            GetWindow<SeaPlantDetailBatchPainterWindow>("Sea Plant Painter").Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            if (_activeTerrain == null)
            {
                TryAutoAssignTerrain();
            }
            RefreshDetailLayers();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            _paintingEnabled = false;
        }

        private void OnSelectionChange()
        {
            if (!_followSelection)
            {
                return;
            }

            var previous = _activeTerrain;
            TryAutoAssignTerrain();

            if (previous != _activeTerrain)
            {
                RefreshDetailLayers();
            }

            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Target Terrain", EditorStyles.boldLabel);
                var picked = (Terrain)EditorGUILayout.ObjectField(_activeTerrain, typeof(Terrain), true);
                if (picked != _activeTerrain)
                {
                    _activeTerrain = picked;
                    RefreshDetailLayers();
                    SceneView.RepaintAll();
                }

                _followSelection = EditorGUILayout.ToggleLeft("Follow Scene Selection", _followSelection);

                if (_followSelection)
                {
                    EditorGUILayout.HelpBox("Selecting any terrain in the Scene view will auto-assign it here.", MessageType.Info);
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Detail Layer Selection", EditorStyles.boldLabel);
                _filterToDefaultFolder = EditorGUILayout.ToggleLeft($"Only include prefabs under '{DefaultDetailFolder}'", _filterToDefaultFolder);

                if (GUILayout.Button("Refresh Detail Layers"))
                {
                    RefreshDetailLayers();
                }

                if (_detailLayers.Count == 0)
                {
                    EditorGUILayout.HelpBox("No eligible detail layers detected on the selected terrain. Assign the sea plant detail prefabs to the terrain first.", MessageType.Warning);
                }
                else
                {
                    foreach (var layer in _detailLayers)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            layer.enabled = EditorGUILayout.Toggle(layer.enabled, GUILayout.Width(20));
                            EditorGUILayout.LabelField($"[{layer.index}] {layer.name}", GUILayout.MinWidth(150));
                            EditorGUILayout.LabelField(Path.GetFileName(layer.assetPath), EditorStyles.miniLabel);
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel);
                _brushRadius = EditorGUILayout.Slider("Brush Radius (m)", _brushRadius, MinBrushRadius, MaxBrushRadius);
                _densityPerSquareMeter = EditorGUILayout.Slider("Batches / m²", _densityPerSquareMeter, MinDensity, MaxDensity);
                _maxInstancesPerCell = EditorGUILayout.IntSlider("Max Instances / Cell", _maxInstancesPerCell, 1, 8);
                _strokeSpacing = EditorGUILayout.Slider("Stroke Spacing (m)", _strokeSpacing, MinStrokeSpacing, MaxStrokeSpacing);
                _instancesPerSample = EditorGUILayout.IntSlider("Instances per Hit", _instancesPerSample, MinInstancesPerSample, MaxInstancesPerSample);

                EditorGUILayout.HelpBox("Click-drag in the Scene view to scatter sea plants. Hold Shift to erase existing details in the brush radius.", MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _activeTerrain != null && _detailLayers.Exists(l => l.enabled);
                var toggleLabel = _paintingEnabled ? "Disable Painting" : "Enable Painting";
                var pressed = GUILayout.Toggle(_paintingEnabled, toggleLabel, "Button", GUILayout.Height(32));
                if (pressed != _paintingEnabled)
                {
                    _paintingEnabled = pressed;
                    if (!_paintingEnabled)
                    {
                        _lastStrokeWorldPos = null;
                    }
                    ResetLayerSequence();
                    SceneView.RepaintAll();
                }
                GUI.enabled = true;
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_paintingEnabled || _activeTerrain == null || _detailLayers.Count == 0)
            {
                return;
            }

            var evt = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            if (evt.alt)
            {
                return;
            }

            if (evt.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
            }

            if (!TryRaycastTerrain(evt.mousePosition, out var hit))
            {
                return;
            }

            DrawBrushGizmo(hit.point, hit.normal);

            bool isErase = evt.shift;
            bool wantsPaint = evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag;

            if (wantsPaint && evt.button == 0)
            {
                if (!_lastStrokeWorldPos.HasValue || Vector3.Distance(_lastStrokeWorldPos.Value, hit.point) >= _strokeSpacing || evt.type == EventType.MouseDown)
                {
                    PaintDetails(hit.point, isErase);
                    _lastStrokeWorldPos = hit.point;
                }
                evt.Use();
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                _lastStrokeWorldPos = null;
            }
        }

        private void PaintDetails(Vector3 worldPosition, bool erase)
        {
            if (_activeTerrain == null || _activeTerrain.terrainData == null)
            {
                return;
            }

            var terrainData = _activeTerrain.terrainData;
            var enabledLayers = _detailLayers.FindAll(l => l.enabled);
            if (enabledLayers.Count == 0)
            {
                return;
            }

            Vector3 terrainLocal = worldPosition - _activeTerrain.transform.position;
            float normalizedX = Mathf.Clamp01(terrainLocal.x / terrainData.size.x);
            float normalizedZ = Mathf.Clamp01(terrainLocal.z / terrainData.size.z);

            int detailWidth = terrainData.detailWidth;
            int detailHeight = terrainData.detailHeight;

            int centerX = Mathf.RoundToInt(normalizedX * (detailWidth - 1));
            int centerY = Mathf.RoundToInt(normalizedZ * (detailHeight - 1));

            float detailRadiusX = Mathf.Max(1f, _brushRadius / terrainData.size.x * detailWidth);
            float detailRadiusY = Mathf.Max(1f, _brushRadius / terrainData.size.z * detailHeight);

            int xBase = Mathf.Clamp(centerX - Mathf.CeilToInt(detailRadiusX), 0, detailWidth - 1);
            int yBase = Mathf.Clamp(centerY - Mathf.CeilToInt(detailRadiusY), 0, detailHeight - 1);
            int xEnd = Mathf.Clamp(centerX + Mathf.CeilToInt(detailRadiusX), 0, detailWidth - 1);
            int yEnd = Mathf.Clamp(centerY + Mathf.CeilToInt(detailRadiusY), 0, detailHeight - 1);

            int width = xEnd - xBase + 1;
            int height = yEnd - yBase + 1;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(terrainData, erase ? "Erase Sea Plant Details" : "Paint Sea Plant Details");

            var buffers = new Dictionary<int, int[,]>();
            foreach (var layer in enabledLayers)
            {
                buffers[layer.index] = terrainData.GetDetailLayer(xBase, yBase, width, height, layer.index);
            }

            float brushArea = Mathf.PI * _brushRadius * _brushRadius;
            int operations = Mathf.Max(1, Mathf.RoundToInt(_densityPerSquareMeter * brushArea));

            EnsureLayerSequence(enabledLayers);
            if (_layerSequence.Count == 0)
            {
                return;
            }

            for (int i = 0; i < operations; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle;
                int sampleX = Mathf.Clamp(Mathf.RoundToInt(centerX + offset.x * detailRadiusX), xBase, xEnd);
                int sampleY = Mathf.Clamp(Mathf.RoundToInt(centerY + offset.y * detailRadiusY), yBase, yEnd);

                int localX = sampleX - xBase;
                int localY = sampleY - yBase;

                var layer = NextLayer();
                if (layer == null)
                {
                    break;
                }
                var buffer = buffers[layer.index];

                buffer[localY, localX] = erase
                    ? Mathf.Max(0, buffer[localY, localX] - _instancesPerSample)
                    : Mathf.Min(_maxInstancesPerCell, buffer[localY, localX] + _instancesPerSample);
            }

            foreach (var kvp in buffers)
            {
                terrainData.SetDetailLayer(xBase, yBase, kvp.Key, kvp.Value);
            }

            EditorUtility.SetDirty(terrainData);
            EditorSceneManager.MarkSceneDirty(_activeTerrain.gameObject.scene);
        }

        private bool TryRaycastTerrain(Vector2 mousePosition, out RaycastHit hit)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            var terrainCollider = _activeTerrain.GetComponent<TerrainCollider>();
            if (terrainCollider != null && terrainCollider.Raycast(ray, out hit, Mathf.Infinity))
            {
                return true;
            }

            // Fallback: approximate by sampling height at the ray projection.
            if (IntersectTerrainBounds(ray, out var point))
            {
                hit = new RaycastHit
                {
                    point = point,
                    normal = _activeTerrain.terrainData.GetInterpolatedNormal(
                        Mathf.InverseLerp(_activeTerrain.transform.position.x,
                            _activeTerrain.transform.position.x + _activeTerrain.terrainData.size.x, point.x),
                        Mathf.InverseLerp(_activeTerrain.transform.position.z,
                            _activeTerrain.transform.position.z + _activeTerrain.terrainData.size.z, point.z))
                };
                return true;
            }

            hit = default;
            return false;
        }

        private bool IntersectTerrainBounds(Ray ray, out Vector3 point)
        {
            var bounds = new Bounds(
                _activeTerrain.transform.position + 0.5f * _activeTerrain.terrainData.size,
                _activeTerrain.terrainData.size);

            if (!bounds.IntersectRay(ray, out float distance))
            {
                point = Vector3.zero;
                return false;
            }

            point = ray.GetPoint(distance);
            point.y = _activeTerrain.SampleHeight(point) + _activeTerrain.transform.position.y;
            return true;
        }

        private void DrawBrushGizmo(Vector3 center, Vector3 normal)
        {
            Handles.color = new Color(0f, 0.6f, 1f, 0.18f);
            Handles.DrawSolidDisc(center, normal, _brushRadius);
            Handles.color = new Color(0f, 0.8f, 1f, 0.9f);
            Handles.DrawWireDisc(center, normal, _brushRadius);
        }

        private void TryAutoAssignTerrain()
        {
            _activeTerrain = null;

            if (Selection.activeGameObject != null)
            {
                _activeTerrain = Selection.activeGameObject.GetComponent<Terrain>();
                if (_activeTerrain == null && Selection.activeGameObject.TryGetComponent(out TerrainCollider collider))
                {
                    _activeTerrain = collider.GetComponent<Terrain>();
                }
            }

            if (_activeTerrain == null)
            {
                _activeTerrain = Terrain.activeTerrain;
            }
        }

        private void RefreshDetailLayers()
        {
            _detailLayers.Clear();

            if (_activeTerrain == null || _activeTerrain.terrainData == null)
            {
                return;
            }

            var prototypes = _activeTerrain.terrainData.detailPrototypes;
            if (prototypes == null || prototypes.Length == 0)
            {
                return;
            }

            for (int i = 0; i < prototypes.Length; i++)
            {
                var prototype = prototypes[i];

                if (!prototype.usePrototypeMesh || prototype.prototype == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(prototype.prototype);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (_filterToDefaultFolder && !IsSeaPlantDetailPrefab(path))
                {
                    continue;
                }

                var layer = new DetailLayerInfo(i, prototype.prototype.name, path);
                _detailLayers.Add(layer);
            }

            _detailLayers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            ResetLayerSequence();
            Repaint();
        }

        private static bool IsSeaPlantDetailPrefab(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string normalized = assetPath.Replace('\\', '/');
            string normalizedFolder = DefaultDetailFolder.Replace('\\', '/');
            return normalized.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureLayerSequence(List<DetailLayerInfo> enabledLayers)
        {
            int signature = ComputeLayerSignature(enabledLayers);
            if (signature != _layerSequenceSignature)
            {
                _layerSequenceSignature = signature;
                _layerSequence.Clear();
                _layerSequence.AddRange(enabledLayers);
                Shuffle(_layerSequence);
                _layerSequenceIndex = 0;
            }
        }

        private DetailLayerInfo NextLayer()
        {
            if (_layerSequence.Count == 0)
            {
                return null;
            }

            if (_layerSequenceIndex >= _layerSequence.Count)
            {
                Shuffle(_layerSequence);
                _layerSequenceIndex = 0;
            }

            return _layerSequence[_layerSequenceIndex++];
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
            }
        }

        private void ResetLayerSequence()
        {
            _layerSequence.Clear();
            _layerSequenceIndex = 0;
            _layerSequenceSignature = 0;
        }

        private static int ComputeLayerSignature(List<DetailLayerInfo> layers)
        {
            unchecked
            {
                int hash = 17;
                foreach (var layer in layers)
                {
                    hash = hash * 31 + layer.index;
                }
                return hash;
            }
        }
    }
}
