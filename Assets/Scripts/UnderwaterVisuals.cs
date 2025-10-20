using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Applies simple underwater visuals (fog tint and optional post-processing volume) while the camera or player is inside a water volume.
/// Attach to the main camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public class UnderwaterVisuals : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] private UnderwaterSwimController playerController;
    [SerializeField] private bool includeCameraPosition = true;
    [SerializeField] private float cameraCheckRadius = 0.1f;

    [Header("Fog Settings")]
    [SerializeField] private bool adjustFog = true;
    [SerializeField] private Color underwaterFogColor = new Color(0.1f, 0.4f, 0.6f, 1f);
    [SerializeField] private float underwaterFogDensity = 0.08f;
    [SerializeField] private FogMode underwaterFogMode = FogMode.ExponentialSquared;
    [SerializeField] private float transitionSpeed = 2f;

    [Header("Post Processing")]
    [SerializeField] private Volume underwaterVolume;

    [Header("Bubble Effects")]
    [SerializeField] private bool spawnBubbleBursts = true;
    [SerializeField] private ParticleSystem bubbleParticleSystem;
    [SerializeField] private Material bubbleMaterialOverride;
    [SerializeField] private Mesh bubbleMeshOverride;
    [SerializeField] private Transform bubbleSurfaceReference;
    [SerializeField] private bool bubbleUseFixedSurfaceHeight;
    [SerializeField] private float bubbleFixedSurfaceHeight;
    [SerializeField] private Vector2 bubbleBurstInterval = new Vector2(0.75f, 1.6f);
    [SerializeField] private Vector2Int bubbleBurstCountRange = new Vector2Int(3, 7);
    [SerializeField] private float bubbleSpawnRadius = 0.35f;
    [SerializeField] private Vector2 bubbleRiseSpeedRange = new Vector2(0.6f, 1.8f);
    [SerializeField] private Vector2 bubbleRiseTimeRange = new Vector2(3f, 6f);
    [SerializeField] private Vector3 bubbleLocalOffset = new Vector3(0f, -0.2f, 0.25f);
    [SerializeField, Range(0f, 1f)] private float bubbleActivationBlend = 0.2f;
    [SerializeField] private bool bubbleRiseToSurface = true;
    [SerializeField] private float bubbleSurfaceOvershoot = 0.25f;
    [SerializeField] private float bubbleMinimumLifetime = 0.5f;
    [SerializeField] private float bubbleMaximumLifetime = 8f;
    [SerializeField] private Vector2 bubbleSizeRange = new Vector2(0.05f, 0.16f);
    [SerializeField, Range(0f, 1f)] private float bubbleSmallSizeBias = 0.7f;
    [SerializeField] private bool bubbleUseClusters = true;
    [SerializeField] private Vector2Int bubbleClusterCountRange = new Vector2Int(1, 3);
    [SerializeField] private Vector2Int bubbleClusterBubbleCountRange = new Vector2Int(6, 14);
    [SerializeField] private float bubbleClusterRadius = 0.18f;

    private bool originalFogEnabled;
    private Color originalFogColor;
    private float originalFogDensity;
    private FogMode originalFogMode;

    private float currentBlend;
    private float originalVolumeWeight;
    private float bubbleTimer;
    private bool bubblePlaying;
    private static Mesh cachedSphereMesh;
    private ParticleSystem.Particle[] bubbleParticleBuffer;

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = FindObjectOfType<UnderwaterSwimController>();
        }

        if (underwaterVolume != null)
        {
            originalVolumeWeight = underwaterVolume.weight;
        }

        CacheFogSettings();
        EnsureBubbleSystem();
        ApplyBubbleVisualSettings();
        ResetBubbleTimer();
    }

    private void OnDisable()
    {
        RestoreFogSettings();
        if (underwaterVolume != null)
        {
            underwaterVolume.weight = originalVolumeWeight;
        }
        currentBlend = 0f;
        StopBubbleSystem();
    }

    private void Update()
    {
        bool underwater = DetermineUnderwaterState();
        float targetBlend = underwater ? 1f : 0f;
        currentBlend = Mathf.MoveTowards(currentBlend, targetBlend, transitionSpeed * Time.deltaTime);

        if (adjustFog)
        {
            ApplyFogBlend();
        }

        if (underwaterVolume != null)
        {
            underwaterVolume.weight = Mathf.Lerp(originalVolumeWeight, 1f, currentBlend);
        }

        HandleBubbleEffects(underwater);
    }

    private void LateUpdate()
    {
        if (spawnBubbleBursts && bubbleRiseToSurface && bubbleParticleSystem != null && bubbleParticleSystem.particleCount > 0)
        {
            ClampBubbleHeights();
        }
    }

    private bool DetermineUnderwaterState()
    {
        bool underwater = false;

        if (playerController != null)
        {
            underwater |= playerController.IsInWater;
        }

        if (includeCameraPosition)
        {
            underwater |= WaterVolume.IsPointInside(transform.position, cameraCheckRadius);
        }

        return underwater;
    }

    private void CacheFogSettings()
    {
        originalFogEnabled = RenderSettings.fog;
        originalFogColor = RenderSettings.fogColor;
        originalFogDensity = RenderSettings.fogDensity;
        originalFogMode = RenderSettings.fogMode;
    }

    private void ApplyFogBlend()
    {
        if (currentBlend <= 0f)
        {
            RestoreFogSettings();
            return;
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = underwaterFogMode;
        RenderSettings.fogColor = Color.Lerp(originalFogColor, underwaterFogColor, currentBlend);
        RenderSettings.fogDensity = Mathf.Lerp(originalFogDensity, underwaterFogDensity, currentBlend);
    }

    private void RestoreFogSettings()
    {
        RenderSettings.fog = originalFogEnabled;
        RenderSettings.fogColor = originalFogColor;
        RenderSettings.fogDensity = originalFogDensity;
        RenderSettings.fogMode = originalFogMode;
    }

    private void HandleBubbleEffects(bool underwater)
    {
        if (!spawnBubbleBursts || bubbleParticleSystem == null)
        {
            return;
        }

        if (!underwater || currentBlend < bubbleActivationBlend)
        {
            if (bubblePlaying)
            {
                StopBubbleSystem();
            }
            ResetBubbleTimer();
            return;
        }

        if (!bubblePlaying)
        {
            bubbleParticleSystem.Play(true);
            bubblePlaying = true;
        }

        bubbleTimer -= Time.deltaTime;
        if (bubbleTimer <= 0f)
        {
            EmitBubbleBurst();
            ResetBubbleTimer();
        }
    }

    private void EmitBubbleBurst()
    {
        var emitOrigin = transform.position + transform.TransformVector(bubbleLocalOffset);

        if (bubbleUseClusters)
        {
            EmitBubbleClusters(emitOrigin);
            return;
        }

        int bubbleCount = Mathf.Max(1, Random.Range(bubbleBurstCountRange.x, bubbleBurstCountRange.y + 1));
        for (int i = 0; i < bubbleCount; i++)
        {
            Vector3 spawnPos = emitOrigin + SampleBubbleOffset(bubbleSpawnRadius);
            EmitSingleBubble(spawnPos, preferSmall: false, desiredRiseTimeOverride: null);
        }
    }

    private void EmitBubbleClusters(Vector3 emitOrigin)
    {
        int clusterCount = Mathf.Max(1, Random.Range(bubbleClusterCountRange.x, bubbleClusterCountRange.y + 1));
        for (int c = 0; c < clusterCount; c++)
        {
            Vector3 clusterCenter = emitOrigin + SampleBubbleOffset(bubbleSpawnRadius);
            float clusterRiseTime = Mathf.Clamp(Random.Range(bubbleRiseTimeRange.x, bubbleRiseTimeRange.y), bubbleMinimumLifetime, bubbleMaximumLifetime);
            int bubblesInCluster = Mathf.Max(1, Random.Range(bubbleClusterBubbleCountRange.x, bubbleClusterBubbleCountRange.y + 1));

            for (int i = 0; i < bubblesInCluster; i++)
            {
                Vector3 localOffset = SampleBubbleOffset(bubbleClusterRadius, forceUpwards: false);
                Vector3 spawnPos = clusterCenter + localOffset;
                if (spawnPos.y < clusterCenter.y)
                {
                    spawnPos.y = clusterCenter.y;
                }
                EmitSingleBubble(spawnPos, preferSmall: true, desiredRiseTimeOverride: clusterRiseTime);
            }
        }
    }

    private void EmitSingleBubble(Vector3 spawnPosition, bool preferSmall, float? desiredRiseTimeOverride)
    {
        float baseLifetime = Mathf.Clamp(
            desiredRiseTimeOverride ?? Random.Range(bubbleRiseTimeRange.x, bubbleRiseTimeRange.y),
            bubbleMinimumLifetime,
            bubbleMaximumLifetime);
        float baseSpeed = Mathf.Clamp(Random.Range(bubbleRiseSpeedRange.x, bubbleRiseSpeedRange.y), bubbleRiseSpeedRange.x, bubbleRiseSpeedRange.y);

        var emitParams = new ParticleSystem.EmitParams
        {
            position = spawnPosition,
            applyShapeToPosition = false,
            startSize = SampleBubbleSize(preferSmall),
            startLifetime = baseLifetime,
            velocity = Vector3.up * baseSpeed
        };

        if (bubbleRiseToSurface)
        {
            ApplyBubbleLifetime(spawnPosition, ref emitParams, baseLifetime, null);
        }

        bubbleParticleSystem.Emit(emitParams, 1);
    }

    private static Vector3 SampleBubbleOffset(float radius, bool forceUpwards = true)
    {
        Vector3 offset = Random.insideUnitSphere * radius;
        if (forceUpwards)
        {
            offset.y = Mathf.Abs(offset.y);
        }
        return offset;
    }

    private float SampleBubbleSize(bool preferSmall)
    {
        float min = Mathf.Max(0.001f, Mathf.Min(bubbleSizeRange.x, bubbleSizeRange.y));
        float max = Mathf.Max(min, Mathf.Max(bubbleSizeRange.x, bubbleSizeRange.y));

        float t = Random.value;
        if (preferSmall)
        {
            float biasPower = Mathf.Lerp(1f, 4f, bubbleSmallSizeBias);
            t = Mathf.Pow(t, biasPower);
        }
        return Mathf.Lerp(min, max, t);
    }

    private void ResetBubbleTimer()
    {
        if (bubbleBurstInterval.y <= 0f)
        {
            bubbleBurstInterval.y = 1f;
        }

        float min = Mathf.Max(0.05f, Mathf.Min(bubbleBurstInterval.x, bubbleBurstInterval.y));
        float max = Mathf.Max(min, Mathf.Max(bubbleBurstInterval.x, bubbleBurstInterval.y));
        bubbleTimer = Random.Range(min, max);
    }

    private void StopBubbleSystem()
    {
        if (bubbleParticleSystem != null)
        {
            bubbleParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        bubblePlaying = false;
    }

    private void EnsureBubbleSystem()
    {
        if (!spawnBubbleBursts || bubbleParticleSystem != null)
        {
            return;
        }

        var emitter = new GameObject("AutoBubbleEmitter");
        emitter.transform.SetParent(transform, false);
        emitter.transform.localPosition = bubbleLocalOffset;

        bubbleParticleSystem = emitter.AddComponent<ParticleSystem>();
        var main = bubbleParticleSystem.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = bubbleMaximumLifetime;
        main.startSize = 0.08f;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;

        var emission = bubbleParticleSystem.emission;
        emission.enabled = false;

        var shape = bubbleParticleSystem.shape;
        shape.enabled = false;

        bubbleParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ApplyBubbleVisualSettings();
    }

    private void OnValidate()
    {
        if (bubbleBurstInterval.y < bubbleBurstInterval.x)
        {
            bubbleBurstInterval.y = bubbleBurstInterval.x;
        }

        bubbleBurstCountRange.x = Mathf.Max(1, bubbleBurstCountRange.x);
        bubbleBurstCountRange.y = Mathf.Max(bubbleBurstCountRange.x, bubbleBurstCountRange.y);
        bubbleSpawnRadius = Mathf.Max(0f, bubbleSpawnRadius);
        bubbleRiseSpeedRange.x = Mathf.Max(0f, bubbleRiseSpeedRange.x);
        bubbleRiseSpeedRange.y = Mathf.Max(bubbleRiseSpeedRange.x + 0.001f, bubbleRiseSpeedRange.y);
        bubbleRiseTimeRange.x = Mathf.Max(0.1f, bubbleRiseTimeRange.x);
        bubbleRiseTimeRange.y = Mathf.Max(bubbleRiseTimeRange.x, bubbleRiseTimeRange.y);
        bubbleSurfaceOvershoot = Mathf.Max(0f, bubbleSurfaceOvershoot);
        bubbleMinimumLifetime = Mathf.Max(0.05f, bubbleMinimumLifetime);
        bubbleMaximumLifetime = Mathf.Max(bubbleMinimumLifetime, bubbleMaximumLifetime);
        bubbleSizeRange.x = Mathf.Max(0.001f, bubbleSizeRange.x);
        bubbleSizeRange.y = Mathf.Max(bubbleSizeRange.x, bubbleSizeRange.y);
        bubbleClusterCountRange.x = Mathf.Max(1, bubbleClusterCountRange.x);
        bubbleClusterCountRange.y = Mathf.Max(bubbleClusterCountRange.x, bubbleClusterCountRange.y);
        bubbleClusterBubbleCountRange.x = Mathf.Max(1, bubbleClusterBubbleCountRange.x);
        bubbleClusterBubbleCountRange.y = Mathf.Max(bubbleClusterBubbleCountRange.x, bubbleClusterBubbleCountRange.y);
        bubbleClusterRadius = Mathf.Max(0f, bubbleClusterRadius);

        EnsureBubbleSystem();
        ApplyBubbleVisualSettings();
    }

    private void ApplyBubbleVisualSettings()
    {
        if (bubbleParticleSystem == null)
        {
            return;
        }

        var renderer = bubbleParticleSystem.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
        {
            return;
        }

        renderer.alignment = ParticleSystemRenderSpace.World;

        var resolvedMat = ResolveBubbleMaterial();
        if (resolvedMat != null)
        {
            renderer.sharedMaterial = resolvedMat;
            renderer.material = resolvedMat;
            if (renderer.trailMaterial != null)
            {
                renderer.trailMaterial = resolvedMat;
            }
        }
        else
        {
            Debug.LogWarning("[UnderwaterVisuals] Unable to locate a particle material. Assign one to Bubble Material Override to avoid pink particles.", this);
        }

        var mesh = ResolveBubbleMesh();
        if (mesh != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
        }
        else
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        var main = bubbleParticleSystem.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = bubbleMaximumLifetime;
        main.gravityModifier = 0f;
        main.startSize = bubbleSizeRange.y;
    }

    private Material ResolveBubbleMaterial()
    {
        if (bubbleMaterialOverride != null)
        {
            return bubbleMaterialOverride;
        }

        var pipelineAsset = GraphicsSettings.currentRenderPipeline ?? GraphicsSettings.defaultRenderPipeline;
        if (pipelineAsset != null)
        {
            var particleMat = pipelineAsset.defaultParticleMaterial;
            if (particleMat != null)
            {
                return particleMat;
            }
        }

        var builtinMat = Resources.GetBuiltinResource<Material>("Default-Particle.mat");
        if (builtinMat != null)
        {
            return builtinMat;
        }

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = "AutoBubbleParticleMaterial"
        };

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        }
        else if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", new Color(0.75f, 0.9f, 1f, 0.7f));
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", new Color(0.75f, 0.9f, 1f, 0.7f));
        }

        return material;
    }

    private Mesh ResolveBubbleMesh()
    {
        if (bubbleMeshOverride != null)
        {
            return bubbleMeshOverride;
        }

        if (cachedSphereMesh == null)
        {
            cachedSphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            if (cachedSphereMesh == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var filter = temp.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    cachedSphereMesh = filter.sharedMesh;
                }
                Destroy(temp);
            }
        }

        return cachedSphereMesh;
    }

    private void ApplyBubbleLifetime(Vector3 spawnPosition, ref ParticleSystem.EmitParams emitParams, float? desiredTimeOverride, float? speedOverride)
    {
        if (!bubbleRiseToSurface || bubbleParticleSystem == null)
        {
            return;
        }

        if (!TryGetBubbleSurfaceHeight(spawnPosition, out float surfaceHeight))
        {
            return;
        }

        float targetHeight = surfaceHeight + bubbleSurfaceOvershoot;
        float verticalDistance = targetHeight - spawnPosition.y;
        if (verticalDistance <= 0f)
        {
            return;
        }

        float desiredTime = desiredTimeOverride.HasValue
            ? desiredTimeOverride.Value
            : Random.Range(bubbleRiseTimeRange.x, bubbleRiseTimeRange.y);
        desiredTime = Mathf.Clamp(desiredTime, bubbleMinimumLifetime, bubbleMaximumLifetime);

        float speed = speedOverride.HasValue
            ? speedOverride.Value
            : verticalDistance / Mathf.Max(0.01f, desiredTime);
        speed = Mathf.Clamp(speed, bubbleRiseSpeedRange.x, bubbleRiseSpeedRange.y);

        float adjustedLifetime = verticalDistance / Mathf.Max(0.01f, speed);
        adjustedLifetime = Mathf.Clamp(adjustedLifetime, bubbleMinimumLifetime, bubbleMaximumLifetime);

        emitParams.startLifetime = adjustedLifetime;
        emitParams.velocity = Vector3.up * speed;
    }

    private bool TryGetBubbleSurfaceHeight(Vector3 spawnPosition, out float surfaceHeight)
    {
        if (bubbleUseFixedSurfaceHeight)
        {
            surfaceHeight = bubbleFixedSurfaceHeight;
            return true;
        }

        if (bubbleSurfaceReference != null)
        {
            surfaceHeight = bubbleSurfaceReference.position.y;
            return true;
        }

        var volumes = WaterVolume.ActiveVolumes;
        if (volumes != null)
        {
            for (int i = 0; i < volumes.Count; i++)
            {
                var volume = volumes[i];
                if (volume == null || !volume.ContainsPoint(spawnPosition))
                {
                    continue;
                }

                var collider = volume.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    continue;
                }

                Vector3 localTop = new Vector3(0f, collider.center.y + collider.size.y * 0.5f, 0f);
                Vector3 worldTop = volume.transform.TransformPoint(localTop);
                surfaceHeight = worldTop.y;
                return true;
            }
        }

        surfaceHeight = 0f;
        return false;
    }

    private void ClampBubbleHeights()
    {
        int particleCount = bubbleParticleSystem.particleCount;
        if (particleCount == 0)
        {
            return;
        }

        if (bubbleParticleBuffer == null || bubbleParticleBuffer.Length < particleCount)
        {
            int newSize = Mathf.Max(particleCount, bubbleParticleBuffer != null ? bubbleParticleBuffer.Length * 2 : 64);
            bubbleParticleBuffer = new ParticleSystem.Particle[newSize];
        }

        int retrieved = bubbleParticleSystem.GetParticles(bubbleParticleBuffer);
        bool modified = false;

        for (int i = 0; i < retrieved; i++)
        {
            var particle = bubbleParticleBuffer[i];
            if (!TryGetBubbleSurfaceHeight(particle.position, out float surfaceHeight))
            {
                continue;
            }

            float targetHeight = surfaceHeight + bubbleSurfaceOvershoot;
            var particlePosition = particle.position;
            if (particlePosition.y >= targetHeight)
            {
                particlePosition.y = targetHeight;
                particle.position = particlePosition;
                var velocity = particle.velocity;
                if (velocity.y > 0f)
                {
                    velocity.y = 0f;
                    particle.velocity = velocity;
                }
                particle.remainingLifetime = Mathf.Min(particle.remainingLifetime, Time.deltaTime);
                bubbleParticleBuffer[i] = particle;
                modified = true;
            }
        }

        if (modified)
        {
            bubbleParticleSystem.SetParticles(bubbleParticleBuffer, retrieved);
        }
    }
}
