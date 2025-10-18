using UnityEngine;

namespace SwimmingTest
{
    /// <summary>
    /// Applies a slow sway motion so static sea flora feels alive underwater.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeaPlantSway : MonoBehaviour
    {
        [Header("Rotation")]
        [SerializeField, Range(0f, 45f)]
        private float swayAngle = 12f;

        [SerializeField, Min(0f)]
        private float swaySpeed = 0.25f;

        [SerializeField]
        private Vector3 swayAxis = new Vector3(1f, 0f, 0f);

        [SerializeField, Range(0f, 360f)]
        private float phaseOffsetDegrees = 0f;

        [Header("Optional Bobbing")]
        [SerializeField]
        private bool bobPosition = false;

        [SerializeField, Range(0f, 0.3f)]
        private float bobDistance = 0.05f;

        [SerializeField, Min(0f)]
        private float bobSpeedMultiplier = 0.6f;

        private Quaternion initialRotation;
        private Vector3 initialPosition;
        private Vector3 normalizedAxis;

        private void Awake()
        {
            CacheInitialState();
        }

        private void OnEnable()
        {
            CacheInitialState();
        }

        private void OnValidate()
        {
            normalizedAxis = swayAxis.sqrMagnitude < 0.0001f ? Vector3.up : swayAxis.normalized;
            swayAngle = Mathf.Max(0f, swayAngle);
            swaySpeed = Mathf.Max(0f, swaySpeed);
            bobDistance = Mathf.Max(0f, bobDistance);
            bobSpeedMultiplier = Mathf.Max(0f, bobSpeedMultiplier);
        }

        private void CacheInitialState()
        {
            initialRotation = transform.localRotation;
            initialPosition = transform.localPosition;
            normalizedAxis = swayAxis.sqrMagnitude < 0.0001f ? Vector3.up : swayAxis.normalized;
        }

        private void Update()
        {
            var time = Time.time;
            var phaseRadians = phaseOffsetDegrees * Mathf.Deg2Rad;
            var angle = Mathf.Sin(time * swaySpeed + phaseRadians) * swayAngle;

            transform.localRotation = initialRotation * Quaternion.AngleAxis(angle, normalizedAxis);

            if (bobPosition && bobDistance > 0f)
            {
                var bob = Mathf.Sin(time * swaySpeed * bobSpeedMultiplier + phaseRadians) * bobDistance;
                transform.localPosition = initialPosition + Vector3.up * bob;
            }
            else
            {
                transform.localPosition = initialPosition;
            }
        }
    }
}
