using UnityEngine;

/// <summary>Walking, air control, the landing slide and gravity. PlayerController runs it every physics step.</summary>
public class PlayerMovement : MonoBehaviour
{
    [Header("Ground Movement")]
    public float moveSpeed = 8f;
    [Tooltip("Units/sec². moveSpeed / this = seconds to reach full speed.")]
    public float groundAcceleration = 20f;
    [Tooltip("Units/sec². moveSpeed / this = seconds to come to a stop.")]
    public float groundDeceleration = 10f;
    [Tooltip("Extra acceleration on the ground when input opposes current velocity. 1 = none.")]
    public float groundTurnBoost = 2.5f;

    [Header("Air Movement")]
    [Tooltip("How much you can steer while airborne. Low = heavy momentum.")]
    public float airAcceleration = 10f;
    [Tooltip("Slowdown when no input is held in the air. 0 = keep full momentum.")]
    public float airDeceleration = 5f;

    [Header("Gravity")]
    [Tooltip("Multiplier on Unity's gravity while rising. 1 = default.")]
    public float riseGravityMultiplier = 1.8f;
    [Tooltip("Multiplier while falling. Higher than rise = snappier landings.")]
    public float fallGravityMultiplier = 1f;
    public float maxFallSpeed = 30f;

    [Header("Landing")]
    [Tooltip("Seconds after touching down that ground friction is reduced, so air speed carries over.")]
    public float landingSlideTime = 0.25f;
    [Tooltip("Ground accel/decel multiplier at the moment of landing, easing back to 1 over the slide time. 0 = pure slide, 1 = no effect.")]
    [Range(0f, 1f)] public float landingFrictionMultiplier = 0.2f;

    private PlayerController player;
    private Rigidbody rb;
    private bool wasGrounded;
    private float landingSlideTimer;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>Start of every physics step: notices landings.</summary>
    public void BeginStep(bool grounded)
    {
        if (grounded && !wasGrounded)
            landingSlideTimer = landingSlideTime;   // just touched down
        else if (landingSlideTimer > 0f)
            landingSlideTimer -= Time.fixedDeltaTime;

        wasGrounded = grounded;
    }

    public void Move(bool grounded)
    {
        Vector3 moveDir = player.CameraRelativeMove;
        float lightMultiplier = player.LightSlowdown != null ? player.LightSlowdown.SpeedMultiplier : 1f;
        Vector3 targetVelocity = moveDir * (moveSpeed * lightMultiplier);

        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

        bool hasInput = player.MoveInput.sqrMagnitude > 0.0001f;

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

    public void ApplyGravity(bool grounded)
    {
        if (grounded) return;

        float vy = rb.linearVelocity.y;
        float mult = vy > 0f ? riseGravityMultiplier : fallGravityMultiplier;

        // Unity already applies 1x gravity, so add only the remainder
        rb.AddForce(Physics.gravity * (mult - 1f), ForceMode.Acceleration);

        if (vy < -maxFallSpeed)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -maxFallSpeed, rb.linearVelocity.z);
    }
}
