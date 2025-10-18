using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple third-person orbit camera that follows a target while reading the Input System "Look" action.
/// Attach to the active camera in the scene.
/// </summary>
public class ThirdPersonCameraController : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 1.5f, -4f);
    [SerializeField] private float followSmoothTime = 0f;
    [SerializeField] private float orbitSensitivity = 90f;
    [SerializeField] private float minPitch = -60f;
    [SerializeField] private float maxPitch = 75f;
    [SerializeField] private float alignToTargetSharpness = 8f;
    [SerializeField] private float alignVelocityThreshold = 0.2f;
    [SerializeField] private float alignDelay = 0.25f;
    [SerializeField] private bool autoAlignWhenIdle = false;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private bool requireHoldForOrbit = true;
    [SerializeField] private InputActionReference orbitHoldAction;
    [SerializeField] private UnderwaterSwimController movementController;

    private Vector3 followVelocity;
    private float yaw;
    private float pitch;
    private float alignCooldown;

    private void Awake()
    {
        if (target == null)
        {
            Debug.LogWarning($"{nameof(ThirdPersonCameraController)} has no target assigned. The camera will not track anything until a target is set.");
        }
        else if (movementController == null)
        {
            movementController = target.GetComponent<UnderwaterSwimController>();
        }
    }

    private void OnEnable()
    {
        EnableAction(lookAction, $"{nameof(ThirdPersonCameraController)} has no look action assigned. Mouse/gamepad orbit will be disabled.");
        if (requireHoldForOrbit && orbitHoldAction != null)
        {
            EnableAction(orbitHoldAction, string.Empty);
        }
        else if (requireHoldForOrbit && orbitHoldAction == null)
        {
            Debug.Log($"[{nameof(ThirdPersonCameraController)}] No hold action assigned. Defaulting to Mouse right button.");
        }

        InitializeAngles();
    }

    private void OnDisable()
    {
        lookAction?.action.Disable();
        orbitHoldAction?.action.Disable();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        UpdateOrbit();
        UpdatePosition();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null && movementController == null)
        {
            movementController = target.GetComponent<UnderwaterSwimController>();
        }
        InitializeAngles();
    }

    private void InitializeAngles()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
    }

    private void UpdateOrbit()
    {
        bool allowOrbit = !requireHoldForOrbit;
        if (requireHoldForOrbit && orbitHoldAction != null)
        {
            allowOrbit = orbitHoldAction.action.IsPressed();
        }
        else if (requireHoldForOrbit && orbitHoldAction == null && Mouse.current != null)
        {
            allowOrbit = Mouse.current.rightButton.isPressed;
        }

        Vector2 lookInput = allowOrbit && lookAction != null ? lookAction.action.ReadValue<Vector2>() : Vector2.zero;
        yaw += lookInput.x * orbitSensitivity * Time.deltaTime;
        pitch -= lookInput.y * orbitSensitivity * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        bool hasOrbitInput = allowOrbit && lookInput.sqrMagnitude > 0.0001f;
        if (hasOrbitInput)
        {
            alignCooldown = alignDelay;
        }
        else
        {
            alignCooldown = Mathf.Max(alignCooldown - Time.deltaTime, 0f);
        }

        bool shouldAlignToTarget = autoAlignWhenIdle &&
                                   alignCooldown <= 0f &&
                                   !hasOrbitInput &&
                                   target != null &&
                                   alignToTargetSharpness > 0f;

        if (shouldAlignToTarget && movementController != null)
        {
            shouldAlignToTarget = movementController.CurrentVelocity.sqrMagnitude > alignVelocityThreshold * alignVelocityThreshold;
        }

        if (shouldAlignToTarget)
        {
            float targetYaw = target.eulerAngles.y;
            float lerpFactor = 1f - Mathf.Exp(-alignToTargetSharpness * Time.deltaTime);
            yaw = Mathf.LerpAngle(yaw, targetYaw, lerpFactor);
        }

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdatePosition()
    {
        Vector3 desiredPosition = target.position + transform.rotation * offset;
        if (followSmoothTime < 0.001f)
        {
            transform.position = desiredPosition;
            followVelocity = Vector3.zero;
        }
        else
        {
            float smoothTime = Mathf.Max(0.0001f, followSmoothTime);
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref followVelocity, smoothTime);
        }
    }

    private void EnableAction(InputActionReference actionReference, string warningMessage)
    {
        if (actionReference != null)
        {
            actionReference.action.Enable();
        }
        else if (!string.IsNullOrEmpty(warningMessage))
        {
            Debug.LogWarning(warningMessage);
        }
    }
}
