using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("References")]
    public Transform cam;
    [SerializeField] private InputActionReference moveAction;

    [Header("Ground Movement")]
    public float moveSpeed = 6f;
    [Tooltip("Units/sec². moveSpeed / this = seconds to reach full speed.")]
    public float groundAcceleration = 40f;
    [Tooltip("Units/sec². moveSpeed / this = seconds to come to a stop.")]
    public float groundDeceleration = 30f;

    [Header("Air Movement")]
    [Tooltip("How much you can steer while airborne. Low = heavy momentum.")]
    public float airAcceleration = 4f;
    [Tooltip("Slowdown when no input is held in the air. 0 = keep full momentum.")]
    public float airDeceleration = 0f;

    [Header("Jump")]
    public float jumpForce = 7f;
    public float groundCheckDistance = 1.1f;

    [Header("Camera")]
    public float mouseSensitivity = 0.3f;
    public float cameraDistance = 5f;
    public float cameraHeight = 1.3f;

    [Header("Camera COllision")]
    public LayerMask cameraCollisionMask = ~0;
    public float cameraRadius = 0.2f;
    public float cameraReturnSpeed = 10f;
    private float currentCamDistance;
    public float castStartOffset = 0.5f;

    private Rigidbody rb;
    private float yaw;
    private float pitch;

    private Vector2 moveInput;
    private bool jumpQueued;

    private void OnEnable() => moveAction.action.Enable();
    private void OnDisable() => moveAction.action.Disable();

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        Cursor.lockState = CursorLockMode.Locked;
        currentCamDistance = cameraDistance;
    }

    // Input + camera look run every frame
    void Update()
    {
        Look();

        moveInput = Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f);

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            jumpQueued = true;
    }

    // Physics runs on the fixed timestep
    void FixedUpdate()
    {
        bool grounded = IsGrounded();

        Move(grounded);
        Jump(grounded);
    }

    void LateUpdate()
    {
        PositionCamera();
    }

    void Look()
    {
        if (Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        yaw += mouseDelta.x * mouseSensitivity;
        pitch -= mouseDelta.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -40f, 70f);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void Move(bool grounded)
    {
        // Camera-relative (yaw-relative) desired direction
        Vector3 moveDir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 targetVelocity = moveDir * moveSpeed;

        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

        bool hasInput = moveInput.sqrMagnitude > 0.0001f;

        float accel;
        if (grounded)
            accel = hasInput ? groundAcceleration : groundDeceleration;
        else
            accel = hasInput ? airAcceleration : airDeceleration;

        // Ease current horizontal velocity toward the target (zero when no input).
        // In the air the rate is low, so your launch velocity carries you.
        horizontal = Vector3.MoveTowards(horizontal, targetVelocity, accel * Time.fixedDeltaTime);

        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
    }

    void Jump(bool grounded)
    {
        if (jumpQueued && grounded)
        {
            // Set Y directly so jump height is consistent no matter what
            // vertical velocity you had. Horizontal velocity is untouched,
            // so you keep your momentum into the jump.
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x, jumpForce, v.z);
        }

        jumpQueued = false;
    }

    bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);
    }

    void PositionCamera()
    {
        Quaternion camRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = transform.position + Vector3.up * cameraHeight;
        Vector3 backDir = camRotation * Vector3.back;

        Vector3 idealPos = focusPoint + backDir * cameraDistance;
        Vector3 castOrigin = focusPoint + Vector3.down * castStartOffset;

        Vector3 toIdeal = idealPos - castOrigin;
        float castLength = toIdeal.magnitude;
        Vector3 castDir = toIdeal / castLength;


        float targetDistance = cameraDistance;
        if (Physics.SphereCast(castOrigin, cameraRadius, castDir, out RaycastHit hit,
                               castLength, cameraCollisionMask, QueryTriggerInteraction.Ignore))
        {
            targetDistance = hit.distance;
        }

        // Snap in instantly when blocked, ease back out when clear
        currentCamDistance = targetDistance < currentCamDistance
            ? targetDistance
            : Mathf.MoveTowards(currentCamDistance, targetDistance, cameraReturnSpeed * Time.deltaTime);

        cam.position = focusPoint - camRotation * Vector3.forward * cameraDistance;
        cam.rotation = camRotation;
    }
}