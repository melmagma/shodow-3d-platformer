using UnityEngine;

/// <summary>Jumping, with coyote time, jump buffering, and a short hop when you let go early.
/// PlayerController feeds it the key presses and runs it every physics step.</summary>
public class PlayerJump : MonoBehaviour
{
    [Header("Jump")]
    public float jumpForce = 10f;
    [Tooltip("Upward speed is multiplied by this when jump is released while rising. Lower = shorter hops.")]
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.6f;

    [Header("Jump Forgiveness")]
    [Tooltip("Seconds after leaving the ground that you can still jump.")]
    public float coyoteTime = 0.1f;
    [Tooltip("Seconds a jump press is remembered before landing.")]
    public float jumpBufferTime = 0.12f;

    public bool IsJumping { get; private set; }

    private PlayerController player;
    private Rigidbody rb;
    private bool jumpQueued;
    private bool jumpReleased;
    private float jumpBufferTimer;
    private float coyoteTimer;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();
    }

    public void OnJumpPressed()
    {
        jumpQueued = true;
        jumpBufferTimer = jumpBufferTime;
    }

    public void OnJumpReleased() => jumpReleased = true;

    /// <summary>One physics step. canJump is false while dashing.</summary>
    public void Step(bool grounded, bool canJump)
    {
        float dt = Time.fixedDeltaTime;
        coyoteTimer = (grounded && !IsJumping) ? coyoteTime : coyoteTimer - dt;

        if (canJump) UpdateJump();

        // Presses only count for a short while
        jumpBufferTimer -= dt;
        if (jumpBufferTimer <= 0f) jumpQueued = false;
        jumpReleased = false;
    }

    /// <summary>Forgets any queued press and stops tracking the current jump.</summary>
    public void CancelAll()
    {
        jumpQueued = false;
        jumpReleased = false;
        jumpBufferTimer = 0f;
        IsJumping = false;
    }

    private void UpdateJump()
    {
        if (jumpQueued && coyoteTimer > 0f)
        {
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x, jumpForce, v.z);
            IsJumping = true;
            jumpQueued = false;
            coyoteTimer = 0f;

            // A buffered tap you already let go of should still be a short hop
            if (!player.JumpHeld) jumpReleased = true;
        }

        if (jumpReleased && IsJumping)
        {
            Vector3 v = rb.linearVelocity;
            if (v.y > 0f) rb.linearVelocity = new Vector3(v.x, v.y * jumpCutMultiplier, v.z);
            IsJumping = false;
        }

        if (IsJumping && rb.linearVelocity.y <= 0f) IsJumping = false;
    }
}
