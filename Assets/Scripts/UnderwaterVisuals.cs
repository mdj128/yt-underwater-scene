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

    private bool originalFogEnabled;
    private Color originalFogColor;
    private float originalFogDensity;
    private FogMode originalFogMode;

    private float currentBlend;
    private float originalVolumeWeight;

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
    }

    private void OnDisable()
    {
        RestoreFogSettings();
        if (underwaterVolume != null)
        {
            underwaterVolume.weight = originalVolumeWeight;
        }
        currentBlend = 0f;
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
}
