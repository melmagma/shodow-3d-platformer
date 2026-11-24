using UnityEngine;

/// <summary>Dash (shift): a short burst of horizontal speed, with a cooldown and limited air dashes.
/// PlayerController feeds it the key press and runs it every physics step.</summary>
public class PlayerDash : MonoBehaviour
{
    [Header("Dash")]
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

    public bool IsDashing { get; private set; }

    private PlayerController player;
    private Rigidbody rb;
    private bool dashQueued;
    private Vector3 dashDirection;
    private float dashTimeRemaining;
    private float cooldownTimer;
    private int airDashesUsed;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();
    }

    public void OnDashPressed() => dashQueued = true;

    /// <summary>One physics step. Returns true if the player is dashing this step
    /// (PlayerController then skips walking and jumping).</summary>
    public bool Step(bool grounded)
    {
        if (grounded && !IsDashing) airDashesUsed = 0;
        cooldownTimer -= Time.fixedDeltaTime;

        TryStartDash(grounded);
        bool dashing = IsDashing;
        if (dashing) DashStep(grounded);

        dashQueued = false;   // a press only counts for the step right after it
        return dashing;
    }

    public void Cancel()
    {
        dashQueued = false;
        IsDashing = false;
    }

    private void TryStartDash(bool grounded)
    {
        if (!dashQueued || IsDashing || cooldownTimer > 0f) return;
        if (!grounded && airDashesUsed >= maxAirDashes) return;

        // The direction being held (camera-relative). Nothing held: dash where the camera faces.
        Vector3 dir = player.CameraRelativeMove;
        if (dir.sqrMagnitude < 0.0001f) dir = player.CameraForward;

        dashDirection = dir.normalized;    // locked in for the whole dash
        dashTimeRemaining = dashDuration;
        IsDashing = true;

        if (!grounded) airDashesUsed++;
    }

    private void DashStep(bool grounded)
    {
        Vector3 velocity = dashDirection * dashSpeed;

        // Dash is horizontal only. In the air, optionally cancel vertical movement so it stays level.
        bool holdHeight = holdHeightInAir && !grounded;
        velocity.y = holdHeight ? 0f : rb.linearVelocity.y;
        rb.linearVelocity = velocity;

        dashTimeRemaining -= Time.fixedDeltaTime;
        if (dashTimeRemaining <= 0f) EndDash();
    }

    private void EndDash()
    {
        IsDashing = false;
        cooldownTimer = dashCooldown;

        // Cap leftover horizontal speed so normal movement takes over smoothly
        Vector3 v = rb.linearVelocity;
        Vector3 horizontal = Vector3.ClampMagnitude(new Vector3(v.x, 0f, v.z), dashExitSpeed);
        rb.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
    }
}
