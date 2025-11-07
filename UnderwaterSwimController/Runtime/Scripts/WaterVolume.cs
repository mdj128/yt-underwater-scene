using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a box-shaped water region used by swimmers to know when they are submerged.
/// Attach to a GameObject with a BoxCollider (set to trigger) to mark your water bounds.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class WaterVolume : MonoBehaviour
{
    private static readonly List<WaterVolume> activeVolumes = new List<WaterVolume>();

    public static IReadOnlyList<WaterVolume> ActiveVolumes => activeVolumes;

    public static bool IsPointInside(Vector3 worldPoint, float margin = 0f)
    {
        for (int i = 0; i < activeVolumes.Count; i++)
        {
            WaterVolume volume = activeVolumes[i];
            if (volume != null && volume.ContainsPoint(worldPoint, margin))
            {
                return true;
            }
        }

        return false;
    }

    [SerializeField] private Color gizmoFillColor = new Color(0f, 0.4f, 0.75f, 0.15f);
    [SerializeField] private Color gizmoOutlineColor = new Color(0f, 0.6f, 1f, 0.6f);

    private BoxCollider boxCollider;

    private void Reset()
    {
        EnsureColliderSetup();
    }

    private void Awake()
    {
        EnsureColliderSetup();
    }

    private void OnEnable()
    {
        if (!activeVolumes.Contains(this))
        {
            activeVolumes.Add(this);
        }
    }

    private void OnDisable()
    {
        activeVolumes.Remove(this);
    }

    /// <summary>
    /// Checks whether the supplied point lies inside this volume.
    /// </summary>
    public bool ContainsPoint(Vector3 worldPoint, float margin = 0f)
    {
        if (boxCollider == null)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint) - boxCollider.center;
        Vector3 halfSize = boxCollider.size * 0.5f;
        halfSize = Vector3.Max(Vector3.zero, halfSize - Vector3.one * margin);

        return Mathf.Abs(localPoint.x) <= halfSize.x &&
               Mathf.Abs(localPoint.y) <= halfSize.y &&
               Mathf.Abs(localPoint.z) <= halfSize.z;
    }

    private void EnsureColliderSetup()
    {
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider>();
        }

        if (boxCollider != null)
        {
            boxCollider.isTrigger = true;
        }
    }

    private void OnDrawGizmos()
    {
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider>();
        }

        if (boxCollider == null)
        {
            return;
        }

        Matrix4x4 matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.matrix = matrix;

        Vector3 size = boxCollider.size;
        Vector3 center = boxCollider.center;

        Gizmos.color = gizmoFillColor;
        Gizmos.DrawCube(center, size);

        Gizmos.color = gizmoOutlineColor;
        Gizmos.DrawWireCube(center, size);
    }
}
