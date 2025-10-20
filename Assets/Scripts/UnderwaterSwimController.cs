using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles simple 3D swim-style locomotion using the new Input System.
/// Attach to the player root (e.g. Gary) that also owns a PlayerInput component.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public class UnderwaterSwimController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float swimSpeed = 4f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private float acceleration = 8f;
    [SerializeField] private float rotationDamping = 10f;
    [SerializeField, Range(0f, 0.95f)] private float turnAlignmentThreshold = 0.25f;

    [Header("Vertical Movement")]
    [SerializeField] private float verticalSwimSpeed = 3f;
    [SerializeField] private Vector3 rotationPivotOffset = new Vector3(0f, 1f, 0f);
    [SerializeField, Range(0f, 1f)] private float cameraPitchVerticalInfluence = 0.75f;

    [Header("Movement Limits")]
    [SerializeField] private Terrain terrainLimit;
    [SerializeField] private float terrainClearance = 0.1f;
    [SerializeField] private bool clampToWorldHeight = false;
    [SerializeField] private float maxWorldHeight = 0f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator animator;

    [Header("Animation Parameters")]
    [SerializeField] private string speedParameter = "SwimSpeed";
    [SerializeField] private string swimmingBoolParameter = "IsSwimming";

    [Header("Water Volumes")]
    [SerializeField] private bool requireWaterVolume;
    [SerializeField] private float volumeCheckRadius = 0.1f;

    private PlayerInput playerInput;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction crouchAction;
    private InputAction sprintAction;

    private Vector3 currentVelocity;

    private int speedParamHash;
    private int swimmingBoolHash;
    private float defaultAnimatorSpeed = 1f;

    public Vector3 CurrentVelocity => currentVelocity;

    public bool IsInWater => !requireWaterVolume || WaterVolume.IsPointInside(transform.position, volumeCheckRadius);

    public Terrain TerrainLimit => terrainLimit;
    public float TerrainClearance => terrainClearance;
    public bool ClampToWorldHeight => clampToWorldHeight;
    public float MaxWorldHeight => maxWorldHeight;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        if (playerInput == null)
        {
            Debug.LogError("UnderwaterSwimController requires a PlayerInput component on the same GameObject.");
            enabled = false;
            return;
        }

        InputActionAsset actions = playerInput.actions;
        if (actions == null)
        {
            Debug.LogError("PlayerInput requires an InputActionAsset assigned to provide controls.");
            enabled = false;
            return;
        }

        moveAction = actions.FindAction("Move");
        jumpAction = actions.FindAction("Jump");
        crouchAction = actions.FindAction("Crouch");
        sprintAction = actions.FindAction("Sprint");

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (animator != null)
        {
            // Ensure locomotion is entirely code-driven; prevents swim clips with root motion from double-moving the character.
            animator.applyRootMotion = false;
            defaultAnimatorSpeed = animator.speed;
            speedParamHash = string.IsNullOrEmpty(speedParameter) ? 0 : Animator.StringToHash(speedParameter);
            swimmingBoolHash = string.IsNullOrEmpty(swimmingBoolParameter) ? 0 : Animator.StringToHash(swimmingBoolParameter);
        }
    }

    private void OnEnable()
    {
        moveAction?.Enable();
        jumpAction?.Enable();
        crouchAction?.Enable();
        sprintAction?.Enable();
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        jumpAction?.Disable();
        crouchAction?.Disable();
        sprintAction?.Disable();
    }

    private void Update()
    {
        if (moveAction == null)
        {
            return;
        }

        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        float ascend = jumpAction != null && jumpAction.IsPressed() ? 1f : 0f;
        float descend = crouchAction != null && crouchAction.IsPressed() ? 1f : 0f;
        float cameraVertical = 0f;
        if (cameraTransform != null && Mathf.Abs(moveInput.y) > 0.01f)
        {
            cameraVertical = cameraTransform.forward.y * moveInput.y * cameraPitchVerticalInfluence;
        }

        float verticalInput = ascend - descend + cameraVertical;
        verticalInput = Mathf.Clamp(verticalInput, -1f, 1f);
        bool sprinting = sprintAction != null && sprintAction.IsPressed();

        Transform referenceTransform = cameraTransform != null ? cameraTransform : transform;
        Vector3 forward = referenceTransform.forward;
        Vector3 right = referenceTransform.right;

        // Remove any unintended vertical influence from the camera when calculating planar movement.
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 desiredHorizontal = forward * moveInput.y + right * moveInput.x;
        Vector3 desiredVertical = Vector3.up * verticalInput;

        Vector3 desiredVelocity = desiredHorizontal + desiredVertical * (verticalSwimSpeed / Mathf.Max(swimSpeed, 0.01f));
        if (desiredVelocity.sqrMagnitude > 1f)
        {
            desiredVelocity.Normalize();
        }

        if (!IsInWater)
        {
            desiredVelocity = Vector3.zero;
        }

        float currentSpeed = swimSpeed * (sprinting ? sprintMultiplier : 1f);
        desiredVelocity *= currentSpeed;

        Vector3 desiredHorizontalVelocity = Vector3.ProjectOnPlane(desiredVelocity, Vector3.up);
        Vector3 desiredVerticalVelocity = desiredVelocity - desiredHorizontalVelocity;

        Vector3 targetPlanarDirection = Vector3.ProjectOnPlane(desiredHorizontal, Vector3.up);
        if (desiredHorizontalVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 planarDir = desiredHorizontalVelocity.normalized;
            float alignment = Vector3.Dot(transform.forward, planarDir);
            float denominator = 1f - turnAlignmentThreshold;
            float alignmentFactor = denominator > 0.0001f
                ? Mathf.Clamp01((alignment - turnAlignmentThreshold) / denominator)
                : 1f;

            desiredHorizontalVelocity *= alignmentFactor;
        }

        Vector3 adjustedDesiredVelocity = desiredHorizontalVelocity + desiredVerticalVelocity;

        float maxSpeedChange = acceleration * Time.deltaTime;
        currentVelocity = Vector3.MoveTowards(currentVelocity, adjustedDesiredVelocity, maxSpeedChange);

        Vector3 newPosition = transform.position + currentVelocity * Time.deltaTime;
        ApplyMovementLimits(ref newPosition, adjustVelocity: true);
        transform.position = newPosition;

        Vector3 flatVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
        Vector3 desiredFacing = targetPlanarDirection.sqrMagnitude > 0.0001f
            ? targetPlanarDirection.normalized
            : (flatVelocity.sqrMagnitude > 0.0001f ? flatVelocity.normalized : Vector3.zero);

        if (desiredFacing.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(desiredFacing, Vector3.up);
            Quaternion smoothedRotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationDamping * Time.deltaTime);

            Vector3 worldPivotBefore = transform.position + transform.TransformVector(rotationPivotOffset);
            transform.rotation = smoothedRotation;
            Vector3 worldPivotAfter = transform.position + transform.TransformVector(rotationPivotOffset);
            Vector3 rotatedPosition = transform.position + worldPivotBefore - worldPivotAfter;
            ApplyMovementLimits(ref rotatedPosition, adjustVelocity: false);
            transform.position = rotatedPosition;
        }

        UpdateAnimator(currentVelocity, currentSpeed, sprinting);
    }

    private void UpdateAnimator(Vector3 velocity, float maxSpeed, bool sprinting)
    {
        if (animator == null)
        {
            return;
        }

        float normalizedSpeed = maxSpeed > 0.01f ? velocity.magnitude / maxSpeed : 0f;

        if (speedParamHash != 0)
        {
            animator.SetFloat(speedParamHash, normalizedSpeed, 0.1f, Time.deltaTime);
        }

        if (swimmingBoolHash != 0)
        {
            bool isSwimming = normalizedSpeed > 0.05f;
            animator.SetBool(swimmingBoolHash, isSwimming);
        }

        animator.speed = sprinting ? defaultAnimatorSpeed * sprintMultiplier : defaultAnimatorSpeed;
    }

    private void ApplyMovementLimits(ref Vector3 position, bool adjustVelocity)
    {
        bool clampedBottom = false;
        bool clampedTop = false;

        if (terrainLimit != null)
        {
            Vector3 terrainOrigin = terrainLimit.transform.position;
            float terrainHeight = terrainLimit.SampleHeight(position) + terrainOrigin.y + terrainClearance;
            if (position.y < terrainHeight)
            {
                position.y = terrainHeight;
                clampedBottom = true;
            }
        }

        if (clampToWorldHeight && position.y > maxWorldHeight)
        {
            position.y = maxWorldHeight;
            clampedTop = true;
        }

        if (!adjustVelocity)
        {
            return;
        }

        if (clampedBottom && currentVelocity.y < 0f)
        {
            currentVelocity.y = 0f;
        }

        if (clampedTop && currentVelocity.y > 0f)
        {
            currentVelocity.y = 0f;
        }
    }
}
