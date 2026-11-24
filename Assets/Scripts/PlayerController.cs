using FMOD.Studio;
using FMODUnity;
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
    [Tooltip("Extra acceleration on the ground when input opposes current velocity. 1 = none.")]
    public float groundTurnBoost = 2.5f;

    public float speedMultiplier = 1f; // to be reduced when in the range of light, so that speed gets reduced by the multiplier and then placed back to the original speed values.

    [Header("Air Movement")]
    [Tooltip("How much you can steer while airborne. Low = heavy momentum.")]
    public float airAcceleration = 4f;
    [Tooltip("Slowdown when no input is held in the air. 0 = keep full momentum.")]
    public float airDeceleration = 0f;

    [Header("Gravity")]
    [Tooltip("Multiplier on Unity's gravity while rising. 1 = default.")]
    public float riseGravityMultiplier = 1.8f;
    [Tooltip("Multiplier while falling. Higher than rise = snappier landings.")]
    public float fallGravityMultiplier = 2.6f;
    public float maxFallSpeed = 30f;

    [Header("Landing")]
    [Tooltip("Seconds after touching down that ground friction is reduced, so air speed carries over.")]
    public float landingSlideTime = 0.25f;
    [Tooltip("Ground accel/decel multiplier at the moment of landing, easing back to 1 over the slide time. 0 = pure slide, 1 = no effect.")]
    [Range(0f, 1f)] public float landingFrictionMultiplier = 0.2f;

    [Header("Jump Forgiveness")]
    [Tooltip("Seconds after leaving the ground that you can still jump.")]
    public float coyoteTime = 0.1f;
    [Tooltip("Seconds a jump press is remembered before landing.")]
    public float jumpBufferTime = 0.12f;

    [Header("Dash (Shift)")]
    [Tooltip("Horizontal speed during the dash. Distance = speed * duration.")]
    public float dashSpeed = 22f;
    public float dashDuration = 0.18f;
    [Tooltip("Time after a dash ends before you can dash again.")]
    public float dashCooldown = 0.5f;
    [Tooltip("Horizontal speed is capped to this when the dash ends, so you don't carry full dash speed into the air.")]
    public float dashExitSpeed = 8f;
    [Tooltip("How many dashes you get per airtime. Resets on landing.")]
    public int maxAirDashes = 1;
    [Tooltip("Airborne dashes stay level instead of falling (or rising) during the dash.")]
    public bool holdHeightInAir = true;


    [Header("Jump")]
    public float jumpForce = 7f;
    public float groundCheckDistance = 1.1f;
    [Tooltip("Upward speed is multiplied by this when jump is released while rising. Lower = shorter hops.")]
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.4f;


    [Header("Camera")]
    public float mouseSensitivity = 0.3f;
    public float cameraDistance = 5f;
    public float cameraHeight = 1.3f;

    [Header("Facing")]
    [Tooltip("Degrees per second the player turns toward its movement direction.")]
    public float turnSpeed = 720f;

    [Header("Camera Collision")]
    [Tooltip("Uncheck the Player's own layer.")]
    public LayerMask cameraCollisionMask = ~0;
    [Tooltip("Minimum cast radius. It is raised automatically to cover the camera's near plane.")]
    public float cameraRadius = 0.2f;
    public float cameraReturnSpeed = 10f;
    [Tooltip("Moves the cast origin down so it starts inside the player's capsule.")]
    public float castStartOffset = 0.5f;
    [Tooltip("Draws the cast in the Scene view while playing: green = ideal, red = actual.")]
    public bool debugCamera = false;

    [Header("Light Slowdown")]
    [SerializeField] private LightDetector lightDetector;
    [Tooltip("Light intensity at or below this means no slowdown.")]
    public float minLightIntensity = 0.5f;
    [Tooltip("Light intensity at or above this means maximum slowdown.")]
    public float maxLightIntensity = 5f;
    [Tooltip("Speed multiplier when standing in the brightest light.")]
    [Range(0f, 1f)] public float minSpeedMultiplier = 0.4f;
    [Tooltip("How quickly the multiplier changes, per second.")]
    public float multiplierChangeRate = 3f;

    private Rigidbody rb;
    private Camera camComponent;
    private float yaw;
    private float pitch;
    private float currentCamDistance;

    private Vector2 moveInput;
    private bool jumpQueued;
    private bool jumpReleased;
    private bool isJumping;

    private bool dashQueued;

    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool wasGrounded;
    private float landingSlideTimer;

    private bool isDashing;
    private Vector3 dashDirection;
    private float dashTimeRemaining;
    private float dashCooldownTimer;
    private int airDashesUsed;

    private EventInstance playerFootsteps;

    [HideInInspector] public bool controlLocked;
    public Vector2 MoveInput => moveInput;


    private void OnEnable() => moveAction.action.Enable();
    private void OnDisable() => moveAction.action.Disable();

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        Cursor.lockState = CursorLockMode.Locked;

        camComponent = cam.GetComponent<Camera>();
        currentCamDistance = cameraDistance;

        playerFootsteps = AudioManager.instance.CreateEventInstance(FMODEvents.instance.playerFootsteps);
    }

    void Update()
    {
        Look();
        UpdateFacing();

        moveInput = Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f);

 
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasReleasedThisFrame)
            {
                jumpReleased = true;
            }
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                jumpQueued = true;
                jumpBufferTimer = jumpBufferTime;
            }
            if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
            {
                dashQueued = true;
            }
        }
    }


    void FixedUpdate() //update handles physics
    {
        if (controlLocked)
        {
            jumpQueued = dashQueued = jumpReleased = false;
            isDashing = false;
            isJumping = false;
            playerFootsteps.stop(STOP_MODE.ALLOWFADEOUT);
            return;
        }

        bool grounded = IsGrounded();
        if (grounded && !wasGrounded)
            landingSlideTimer = landingSlideTime;   // just touched down
        else if (landingSlideTimer > 0f)
            landingSlideTimer -= Time.fixedDeltaTime;

        wasGrounded = grounded;
        coyoteTimer = (grounded && !isJumping) ? coyoteTime : coyoteTimer - Time.fixedDeltaTime;

        UpdateLightSlowdown();

        if (grounded && !isDashing)
            airDashesUsed = 0;
 
        dashCooldownTimer -= Time.fixedDeltaTime;
        TryStartDash(grounded);

        if (isDashing)
        {
            Dash(grounded);
        }
        else
        {
            Move(grounded);
            Jump(grounded);
        }

        ApplyGravity(grounded);
        UpdateSound();

        // Inputs only count for the physics step right after they're pressed
        jumpBufferTimer -= Time.fixedDeltaTime;
        if (jumpBufferTimer <= 0f) jumpQueued = false;
        dashQueued = false;
        jumpReleased = false;
        dashQueued = false;
        jumpReleased=false;
    }


    void LateUpdate() //runs right after what Update()
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
    }

    void Move(bool grounded)
    {
        Vector3 moveDir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 targetVelocity = moveDir * (moveSpeed * speedMultiplier);

        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

        bool hasInput = moveInput.sqrMagnitude > 0.0001f;

        float accel;
        if (grounded)
            accel = hasInput ? groundAcceleration : groundDeceleration;
        else
            accel = hasInput ? airAcceleration : airDeceleration;

        if (grounded && hasInput && horizontal.sqrMagnitude > 0.01f)
        {
            // 1 = already moving the way you're pushing, -1 = fully reversing
            float alignment = Vector3.Dot(horizontal.normalized, moveDir.normalized);
            accel *= Mathf.Lerp(groundTurnBoost, 1f, (alignment + 1f) * 0.5f);
        }

        if (grounded && landingSlideTimer > 0f)
        {
            // 0 at the moment of landing, 1 when the slide window ends
            float t = 1f - landingSlideTimer / landingSlideTime;
            accel *= Mathf.Lerp(landingFrictionMultiplier, 1f, t);
        }
        horizontal = Vector3.MoveTowards(horizontal, targetVelocity, accel * Time.fixedDeltaTime);

        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
    }

    public void SwimExitJump(Vector3 launchVelocity)
    {
        controlLocked = false;
        jumpQueued = dashQueued = jumpReleased = false;
        isDashing = false;
        isJumping = true;               // keeps release-to-shorten-hop working
        rb.linearVelocity = launchVelocity;
    }

    public void SwimExitDash(Vector3 direction)
    {
        controlLocked = false;
        jumpQueued = dashQueued = jumpReleased = false;
        dashDirection = direction.normalized;
        dashTimeRemaining = dashDuration;
        dashCooldownTimer = 0f;
        isDashing = true;
        isJumping = false;
    }

    void Jump(bool grounded)
    {
        if (jumpQueued && coyoteTimer > 0f)
        {
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x, jumpForce, v.z);
            isJumping = true;
            jumpQueued = false;
            coyoteTimer = 0f;

            // A buffered tap you already let go of should still be a short hop
            if (Keyboard.current != null && !Keyboard.current.spaceKey.isPressed)
                jumpReleased = true;
        }
        if(jumpReleased && isJumping)
        {
            Vector3 v = rb.linearVelocity;
            if (v.y > 0f)
            {
                rb.linearVelocity = new Vector3(v.x, v.y * jumpCutMultiplier, v.z);
            }
            isJumping = false;
        }
        if (isJumping && rb.linearVelocity.y <= 0f)
        {
            isJumping=false;
        }
    }

    void ApplyGravity(bool grounded)
    {
        if (grounded) return;

        float vy = rb.linearVelocity.y;
        float mult = vy > 0f ? riseGravityMultiplier : fallGravityMultiplier;

        // Unity already applies 1x gravity, so add only the remainder
        rb.AddForce(Physics.gravity * (mult - 1f), ForceMode.Acceleration);

        if (vy < -maxFallSpeed)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -maxFallSpeed, rb.linearVelocity.z);
    }
    void TryStartDash(bool grounded)
    {
        if (!dashQueued || isDashing || dashCooldownTimer > 0f) return;
        if (!grounded && airDashesUsed >= maxAirDashes) return;
 
        // Direction being held (camera-relative), flattened to the XZ plane
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 dir = yawRotation * new Vector3(moveInput.x, 0f, moveInput.y);
 
        // No input held: dash straight ahead
        if (dir.sqrMagnitude < 0.0001f)
            dir = transform.forward;
 
        dashDirection = dir.normalized; // locked in for the whole dash
        dashTimeRemaining = dashDuration;
        isDashing = true;
 
        if (!grounded)
            airDashesUsed++;
    }
 
    void Dash(bool grounded)
    {
        Vector3 velocity = dashDirection * dashSpeed;
 
        // Dash is horizontal only. In the air, optionally cancel vertical movement so it stays level.
        bool holdHeight = holdHeightInAir && !grounded;
        velocity.y = holdHeight ? 0f : rb.linearVelocity.y;
 
        rb.linearVelocity = velocity;
 
        dashTimeRemaining -= Time.fixedDeltaTime;
        if (dashTimeRemaining <= 0f)
            EndDash();
    }
 
    void EndDash()
    {
        isDashing = false;
        dashCooldownTimer = dashCooldown;
 
        // Cap leftover horizontal speed so normal movement takes over smoothly
        Vector3 v = rb.linearVelocity;
        Vector3 horizontal = Vector3.ClampMagnitude(new Vector3(v.x, 0f, v.z), dashExitSpeed);
        rb.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
    }


    bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);
    }

    // Distance from the cameras center to the corners of its near clip plane.
    // The cast sphere has to be at least this big or the view clips into walls.
    float GetCameraClearance()
    {
        if (camComponent == null) return cameraRadius;

        float near = camComponent.nearClipPlane;
        float halfH = near * Mathf.Tan(camComponent.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * camComponent.aspect;
        float corner = Mathf.Sqrt(near * near + halfH * halfH + halfW * halfW);

        return Mathf.Max(cameraRadius, corner);
    }

    void PositionCamera()
    {
        Quaternion camRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = transform.position + Vector3.up * cameraHeight;
        Vector3 backDir = camRotation * Vector3.back;

        // Where the camera wants to be, and where the cast starts (inside the player)
        Vector3 idealPos = focusPoint + backDir * cameraDistance;
        Vector3 castOrigin = focusPoint + Vector3.down * castStartOffset;

        Vector3 toIdeal = idealPos - castOrigin;
        float castLength = toIdeal.magnitude;
        Vector3 castDir = toIdeal / castLength;

        float targetDistance = castLength;
        if (Physics.SphereCast(castOrigin, GetCameraClearance(), castDir, out RaycastHit hit,
                               castLength, cameraCollisionMask, QueryTriggerInteraction.Ignore))
        {
            targetDistance = hit.distance;
        }

        // Snap in instantly when blocked, ease back out when clear
        currentCamDistance = targetDistance < currentCamDistance
            ? targetDistance
            : Mathf.MoveTowards(currentCamDistance, targetDistance, cameraReturnSpeed * Time.deltaTime);

        cam.position = castOrigin + castDir * currentCamDistance;
        cam.rotation = camRotation;

        if (debugCamera)
        {
            Debug.DrawLine(castOrigin, idealPos, Color.green);
            Debug.DrawLine(castOrigin, cam.position, Color.red);
        }
    }

    void UpdateFacing()
    {
        if (controlLocked) return; // your swim code owns facing while locked

        Vector3 faceDir;
        float speed = turnSpeed;

        if (isDashing)
        {
            faceDir = dashDirection;
            speed *= 4f; // dash snaps around almost instantly
        }
        else if (moveInput.sqrMagnitude > 0.0001f)
        {
            faceDir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(moveInput.x, 0f, moveInput.y);
        }
        else return; // no input: keep facing the same way

        Quaternion targetRot = Quaternion.LookRotation(faceDir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, speed * Time.deltaTime);
    }
    private void UpdateSound()
    {
        Vector3 v = rb.linearVelocity;
        bool isMoving = new Vector2(v.x, v.z).sqrMagnitude > 0.01f;

        if (isMoving && IsGrounded())
        {
            playerFootsteps.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject, rb));
            PLAYBACK_STATE playbackState;
            playerFootsteps.getPlaybackState(out playbackState);
            if (playbackState.Equals(PLAYBACK_STATE.STOPPED))
            {
                playerFootsteps.start();
            }

        }
        else
        {
            playerFootsteps.stop(STOP_MODE.ALLOWFADEOUT);
        }
    }
    void UpdateLightSlowdown()
    {
        float intensity = lightDetector != null ? lightDetector.CurrentIntensity : 0f;

        // 0 at minLightIntensity, 1 at maxLightIntensity
        float t = Mathf.InverseLerp(minLightIntensity, maxLightIntensity, intensity);

        // Bright light -> lower multiplier
        float targetMultiplier = Mathf.Lerp(1f, minSpeedMultiplier, t);

        speedMultiplier = Mathf.MoveTowards(speedMultiplier, targetMultiplier,
                                            multiplierChangeRate * Time.fixedDeltaTime);
    }
}