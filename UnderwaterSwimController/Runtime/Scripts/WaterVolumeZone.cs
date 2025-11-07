using UnityEngine;

/// <summary>
/// Drives the WaterWorks volumetric water material so it behaves like a boxed volume
/// instead of an infinite plane. Attach to an axis-aligned collider that encloses the
/// space you want to feel underwater (for example, the top cube that acts as the surface).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Collider))]
public class WaterVolumeZone : MonoBehaviour
{
    [Tooltip("Leave at zero to read the bounding size from the attached collider each frame.")]
    public Vector3 manualSize = Vector3.zero;

    [Range(0f, 1f)]
    [Tooltip("How dense the underwater fog feels inside the volume.")]
    public float density = 0.6f;

    [ColorUsage(false, true)]
    [Tooltip("Color tint used for the volumetric fog.")]
    public Color albedo = new Color(0.056f, 0.151f, 0.151f, 1f);

    [Tooltip("Draw a wireframe cube to show the extents while selected.")]
    public bool drawGizmo = true;

    Material _waterVolumeMat;
    Collider _collider;

    void OnEnable()
    {
        CacheReferences();
        UpdateVolumeSettings();
    }

    void OnDisable()
    {
        _waterVolumeMat = null;
        _collider = null;
    }

    void Update()
    {
        UpdateVolumeSettings();
    }

    void OnValidate()
    {
        UpdateVolumeSettings();
    }

    void CacheReferences()
    {
        if (_waterVolumeMat == null)
        {
            _waterVolumeMat = Resources.Load<Material>("Water_Volume");
        }

        if (_collider == null)
        {
            TryGetComponent(out _collider);
        }
    }

    void UpdateVolumeSettings()
    {
        CacheReferences();
        if (_waterVolumeMat == null)
        {
            return;
        }

        Vector3 size = manualSize;
        if (size == Vector3.zero && _collider != null)
        {
            size = _collider.bounds.size;
        }

        // Avoid zero-sized bounds which would break the shader's ray-box checks.
        size.x = Mathf.Max(size.x, 0.01f);
        size.y = Mathf.Max(size.y, 0.01f);
        size.z = Mathf.Max(size.z, 0.01f);

        Vector4 volumeBounds = new Vector4(size.x, size.y, size.z, 0f);
        Vector3 centerPos = transform.position;
        if (_collider != null)
        {
            centerPos = _collider.bounds.center;
        }

        Vector4 center = new Vector4(centerPos.x, centerPos.y, centerPos.z, 0f);

        _waterVolumeMat.SetVector("bounds", volumeBounds);
        _waterVolumeMat.SetVector("pos", center);
        _waterVolumeMat.SetFloat("density", density);
        _waterVolumeMat.SetColor("Albedo", albedo);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawGizmo)
        {
            return;
        }

        CacheReferences();

        Vector3 size = manualSize;
        if (size == Vector3.zero && _collider != null)
        {
            size = _collider.bounds.size;
        }

        Gizmos.color = new Color(0.1f, 0.6f, 0.8f, 0.4f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.identity, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
#endif
}
