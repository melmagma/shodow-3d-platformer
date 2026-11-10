using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Splatoon-style ink swimming, using shadow instead of ink.
///
/// Press the toggle key once to enter the swim state. While swimming you move normally, but if you push
/// into a Wall surface that has no light on it, you stick to it and climb it automatically (W = up,
/// S = down, A/D = sideways). The collider is always respected: you stay on the outside of the wall.
/// Lit walls can't be climbed, and you can't climb from shadow into light (the lit area acts like a wall).
/// Reaching the top of a wall hops you over the lip.
///
/// Jump / dash push you off the wall. Toggle key drops you off.
///
/// Put this on the Player next to PlayerController (same PlayerController additions as before).
/// Needs LightSampler.cs.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(PlayerController))]
public class ShadowSwim : MonoBehaviour
{
    private enum ExitKind { Jump, Dash }

    [Header("Surfaces & Light")]
    [Tooltip("Surfaces you can climb (the 'Wall' layer).")]
    [SerializeField] private LayerMask wallLayers;
    [Tooltip("Everything that blocks light. Must include the Wall layer. The player's own layer is ignored automatically, so give the player its own layer.")]
    [SerializeField] private LayerMask occluders = ~0;
    [Tooltip("Light on a surface below this counts as shadow. Tune it using the readout from your LightDetector.")]
    [SerializeField] private float darkThreshold = 0.1f;
    [Tooltip("If a light lands on the wall you're climbing, you get pushed off after this many seconds.")]
    [SerializeField] private float litEjectDelay = 0.15f;

    [Header("Input")]
    [Tooltip("Press once to start swimming, press again to stop.")]
    [SerializeField] private Key toggleKey = Key.LeftCtrl;
    [Tooltip("Pushes you off the wall with a jump. Match PlayerController's jump key.")]
    [SerializeField] private Key exitJumpKey = Key.Space;
    [Tooltip("Pushes you off the wall with a dash. Match PlayerController's dash key.")]
    [SerializeField] private Key exitDashKey = Key.LeftShift;
    [Tooltip("On: jumping or dashing also ends the swim state. Off: you stay in the swim state and can stick to the next dark wall (wall-jump chaining).")]
    [SerializeField] private bool jumpDashEndsSwim = true;

    [Header("Climbing")]
    [SerializeField] private float climbSpeed = 7f;
    [Tooltip("How close you need to be to a wall (while pushing into it) to stick to it.")]
    [SerializeField] private float attachReach = 0.4f;
    [Tooltip("Surface steepness you can climb, measured as the angle between the surface normal and straight up. 90 = vertical wall.")]
    [SerializeField] private float minClimbAngle = 60f;
    [SerializeField] private float maxClimbAngle = 110f;
    [Tooltip("Seconds before you can stick to a wall again after leaving one.")]
    [SerializeField] private float reattachDelay = 0.25f;
    [Tooltip("Hop over the top of a wall: upward speed...")]
    [SerializeField] private float ledgeHopUp = 6f;
    [Tooltip("...and speed over the lip.")]
    [SerializeField] private float ledgeHopForward = 3.5f;

    [Header("Jumping / dashing off a wall")]
    [SerializeField] private float exitJumpOutSpeed = 5f;
    [SerializeField] private float exitJumpUpSpeed = 8f;

    [Header("Climb Mode")]
    [Tooltip("Renderers hidden while climbing. Leave empty to hide every MeshRenderer and SkinnedMeshRenderer on the player.")]
    [SerializeField] private Renderer[] hideWhileClimbing;

    [Header("Feedback")]
    public UnityEvent onEnterSwim;     // swim state turned on
    public UnityEvent onExitSwim;      // swim state turned off
    public UnityEvent onAttachWall;    // stuck to a wall (swap to a squid model, play a sound...)
    public UnityEvent onDetachWall;

    public bool IsSwimming { get; private set; }
    public bool IsClimbing { get; private set; }

    /// <summary>True while stuck to a wall. Light damage should be skipped while this is true.</summary>
    public bool IsLightImmune => IsClimbing;

    // Internal tuning
    private const float ProbeRadius = 0.2f;        // sphere used to find a wall to stick to
    private const float MinPushDot = 0.5f;         // how directly you must push into the wall to stick
    private const float ReacquireReach = 0.5f;     // how far past the capsule radius we look for the wall each step
    private const float WrapDepth = 0.15f;         // outer corners / tops: how far behind the wall plane we look back from
    private const float WrapReach = 0.6f;
    private const float CornerProbe = 0.3f;        // inner corners: how early we notice a wall ahead
    private const float LitLookAhead = 0.35f;      // how far ahead on the wall we check for light
    private const float PeekHeight = 0.3f;
    private const float PeekDepth = 0.4f;
    private const float StickSqueeze = 0.02f;      // pressed this far into the wall so contact never breaks
    private const float MaxStickSpeed = 10f;
    private const float LostSurfaceGrace = 0.15f;
    private const float LedgeReattachDelay = 0.5f;

    private PlayerController controller;
    private Rigidbody rb;
    private Collider col;
    private int occluderMask;

    private bool togglePressed;
    private bool jumpPressed;
    private bool dashPressed;
    private bool savedUseGravity;
    private float reattachTimer;
    private float litTimer;
    private float lostTimer;

    private Vector3 surfacePoint;
    private Vector3 surfaceNormal;
    private Collider surfaceCollider;
    private bool[] rendererWasEnabled;
    private bool visualsHidden;
    private Vector3 rightHint;      // keeps "right" consistent around corners
    private Vector3 lastWishDir;    // last direction we tried to move along the wall

    private void Reset()
    {
        wallLayers = LayerMask.GetMask("Wall");
    }

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();

        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            if (!c.isTrigger) { col = c; break; }
        }
        if (col == null)
        {
            Debug.LogError("[ShadowSwim] No non-trigger collider found on the player.", this);
            enabled = false;
            return;
        }

        if (hideWhileClimbing == null || hideWhileClimbing.Length == 0)
        {
            var found = new List<Renderer>();
            found.AddRange(GetComponentsInChildren<MeshRenderer>());
            found.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>());
            hideWhileClimbing = found.ToArray();
        }
        rendererWasEnabled = new bool[hideWhileClimbing.Length];

        // The player's own body must never count as a shadow caster for the wall it's climbing
        occluderMask = occluders & ~(1 << gameObject.layer);
        if (gameObject.layer == 0)
            Debug.LogWarning("[ShadowSwim] The player is on the Default layer, so Default geometry would be ignored as a shadow caster. Give the player its own layer.", this);
    }

    private void OnDisable()
    {
        if (IsClimbing)
        {
            IsClimbing = false;
            if (rb != null) rb.useGravity = savedUseGravity;
            if (controller != null) controller.controlLocked = false;
            SetClimbVisuals(false);
        }
        IsSwimming = false;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb[toggleKey].wasPressedThisFrame) togglePressed = true;
        if (kb[exitJumpKey].wasPressedThisFrame) jumpPressed = true;
        if (kb[exitDashKey].wasPressedThisFrame) dashPressed = true;
    }

    private void FixedUpdate()
    {
        reattachTimer -= Time.fixedDeltaTime;

        if (togglePressed)
        {
            if (IsSwimming) ExitSwimState();
            else EnterSwimState();
        }
        else if (IsSwimming)
        {
            if (IsClimbing)
            {
                if (dashPressed) LeaveWall(ExitKind.Dash);
                else if (jumpPressed) LeaveWall(ExitKind.Jump);
                else ClimbStep();
            }
            else if ((jumpPressed || dashPressed) && jumpDashEndsSwim)
            {
                // PlayerController is running and handles the jump / dash itself
                ExitSwimState();
            }
            else
            {
                TryAttach();
            }
        }

        togglePressed = false;
        jumpPressed = false;
        dashPressed = false;
    }

    // ------------------------------------------------------------------ swim state

    private void EnterSwimState()
    {
        IsSwimming = true;
        onEnterSwim?.Invoke();
    }

    private void ExitSwimState()
    {
        if (IsClimbing) DropOffWall();
        IsSwimming = false;
        onExitSwim?.Invoke();
    }

    // ------------------------------------------------------------------ attaching

    // In swim state, pushing into a climbable wall that's in shadow sticks you to it
    private void TryAttach()
    {
        if (reattachTimer > 0f) return;

        Vector3 dir = Flatten(CameraRelativeInput());
        if (dir.sqrMagnitude < 0.01f) return;           // only stick when you're pushing into something

        GetBody(out Vector3 center, out float radius, out float halfHeight);

        if (!Physics.SphereCast(center, ProbeRadius, dir, out RaycastHit hit, radius + attachReach,
                                wallLayers, QueryTriggerInteraction.Ignore))
            return;

        if (!IsClimbable(hit.normal)) return;
        if (Vector3.Dot(dir, -hit.normal) < MinPushDot) return;
        if (IsLit(hit.point, hit.normal, hit.collider)) return;   // lit wall = just a wall

        Attach(hit);
    }

    private void Attach(RaycastHit hit)
    {
        IsClimbing = true;
        surfacePoint = hit.point;
        surfaceNormal = hit.normal;
        surfaceCollider = hit.collider;
        rightHint = Vector3.zero;
        lastWishDir = Vector3.zero;
        litTimer = 0f;
        lostTimer = 0f;

        savedUseGravity = rb.useGravity;
        rb.useGravity = false;
        controller.controlLocked = true;       // this script drives the body while climbing
        SetClimbVisuals(true);                 // hide the character

        onAttachWall?.Invoke();
    }

    // ------------------------------------------------------------------ climbing

    private void ClimbStep()
    {
        float dt = Time.fixedDeltaTime;
        GetBody(out Vector3 center, out float radius, out float halfHeight);

        // 1) Find the wall under us again. It may curve, turn a corner, or end (the top).
        bool found = Physics.Raycast(center, -surfaceNormal, out RaycastHit hit, radius + ReacquireReach,
                                     wallLayers, QueryTriggerInteraction.Ignore);

        if (!found && lastWishDir.sqrMagnitude > 0.01f)
        {
            // We're past an edge. Look back at the side / top face from just beyond it.
            Vector3 wrapOrigin = center - surfaceNormal * (radius + WrapDepth);
            found = Physics.Raycast(wrapOrigin, -lastWishDir, out hit, radius + WrapReach,
                                    wallLayers, QueryTriggerInteraction.Ignore);
        }

        bool wishingUp = Vector3.Dot(lastWishDir, Vector3.up) > 0.3f;

        if (found)
        {
            lostTimer = 0f;

            if (IsClimbable(hit.normal))
            {
                // Round a corner onto another face, but only if it's in shadow
                if (!IsLit(hit.point, hit.normal, hit.collider)) AdoptSurface(hit);
            }
            else if (hit.normal.y > 0.5f && wishingUp)
            {
                LedgeHop();        // reached the top: hop onto it
                return;
            }
        }
        else
        {
            lostTimer += dt;
            if (lostTimer >= LostSurfaceGrace)
            {
                if (wishingUp) LedgeHop();
                else DropOffWall();
                return;
            }
        }

        // 2) A light landed on the wall we're on -> pushed off after a short grace period
        if (IsLit(surfacePoint, surfaceNormal, surfaceCollider))
        {
            litTimer += dt;
            if (litTimer >= litEjectDelay) { DropOffWall(); return; }
        }
        else
        {
            litTimer = 0f;
        }

        // 3) Input along the wall (W = up, S = down, A/D = sideways)
        GetSurfaceAxes(out Vector3 right, out Vector3 up);
        Vector2 input = controller.MoveInput;
        Vector3 wish = right * input.x + up * input.y;

        // Climbed back down to the floor: step off the wall
        if (input.y < -0.1f && IsGrounded(center, halfHeight))
        {
            DropOffWall();
            return;
        }

        if (wish.sqrMagnitude > 0.0001f)
        {
            Vector3 wdir = wish.normalized;
            lastWishDir = wdir;

            // Another wall right ahead (inner corner): move onto it if it can be climbed and is in shadow
            if (Physics.Raycast(center, wdir, out RaycastHit ahead, radius + CornerProbe, wallLayers, QueryTriggerInteraction.Ignore)
                && Vector3.Dot(ahead.normal, surfaceNormal) < 0.9f
                && IsClimbable(ahead.normal)
                && !IsLit(ahead.point, ahead.normal, ahead.collider))
            {
                AdoptSurface(ahead);
            }

            // Never climb into light: a lit area acts like a solid wall. Slide along its edge if you can.
            if (IsBlockedByLight(wish))
            {
                Vector3 alongRight = Vector3.Project(wish, right);
                Vector3 alongUp = wish - alongRight;

                if (!IsBlockedByLight(alongRight)) wish = alongRight;
                else if (!IsBlockedByLight(alongUp)) wish = alongUp;
                else wish = Vector3.zero;
            }
        }

        // 4) Move along the wall, pressed lightly against it so the collider contact never breaks
        float gap = Vector3.Dot(center - surfacePoint, surfaceNormal);
        float wantedGap = Mathf.Max(0.05f, radius - StickSqueeze);
        float pull = Mathf.Clamp((wantedGap - gap) / dt, -MaxStickSpeed, MaxStickSpeed);

        rb.linearVelocity = wish * climbSpeed + surfaceNormal * pull;
    }

    private void AdoptSurface(RaycastHit hit)
    {
        // Carry "right" and the last move direction around the corner so controls stay continuous
        Quaternion turn = Quaternion.FromToRotation(surfaceNormal, hit.normal);
        if (rightHint != Vector3.zero) rightHint = turn * rightHint;
        lastWishDir = turn * lastWishDir;

        surfacePoint = hit.point;
        surfaceNormal = hit.normal;
        surfaceCollider = hit.collider;
    }

    // Peeks at the wall surface a little way along 'dir'. True if that spot is lit.
    private bool IsBlockedByLight(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return false;

        Vector3 peek = surfacePoint + dir.normalized * LitLookAhead + surfaceNormal * PeekHeight;
        return Physics.Raycast(peek, -surfaceNormal, out RaycastHit h, PeekHeight + PeekDepth,
                               wallLayers, QueryTriggerInteraction.Ignore)
               && IsLit(h.point, h.normal, h.collider);
    }

    // "Up" on the wall is world-up. "Right" matches the screen's right, and stays consistent around corners.
    private void GetSurfaceAxes(out Vector3 right, out Vector3 up)
    {
        Vector3 n = surfaceNormal;

        up = Vector3.ProjectOnPlane(Vector3.up, n);
        if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(controller.cam.forward, n);
        up.Normalize();

        right = Vector3.Cross(n, up);

        // Use the camera to pick the sign. When the camera looks along the wall (corners), fall back to last frame's right.
        float camDot = Vector3.Dot(right, controller.cam.right);
        float sign = (Mathf.Abs(camDot) > 0.3f || rightHint == Vector3.zero) ? camDot : Vector3.Dot(right, rightHint);
        if (sign < 0f) right = -right;

        rightHint = right;
    }

    // ------------------------------------------------------------------ leaving a wall

    private void DetachClimb(float cooldown)
    {
        IsClimbing = false;
        reattachTimer = cooldown;
        litTimer = 0f;
        lostTimer = 0f;

        rb.useGravity = savedUseGravity;
        controller.controlLocked = false;       // PlayerController takes over again
        SetClimbVisuals(false);                 // show the character again

        onDetachWall?.Invoke();
    }

    // Just fall off (toggle key, light landed on you, climbed down to the floor, lost the wall)
    private void DropOffWall()
    {
        DetachClimb(reattachDelay);
        rb.linearVelocity = Vector3.zero;
    }

    // Reached the top: pop over the lip
    private void LedgeHop()
    {
        Vector3 over = Flatten(-surfaceNormal);     // into the wall = forward over its top
        DetachClimb(LedgeReattachDelay);
        rb.linearVelocity = Vector3.up * ledgeHopUp + over * ledgeHopForward;
    }

    // Jump / dash off the wall
    private void LeaveWall(ExitKind kind)
    {
        Vector3 n = surfaceNormal;
        DetachClimb(reattachDelay);

        if (kind == ExitKind.Jump)
        {
            Vector3 launch = Flatten(n) * exitJumpOutSpeed + Vector3.up * exitJumpUpSpeed;
            controller.SwimExitJump(launch);
        }
        else
        {
            controller.SwimExitDash(GetExitDashDirection(n));
        }

        if (jumpDashEndsSwim)
        {
            IsSwimming = false;
            onExitSwim?.Invoke();
        }
    }

    // Held direction if there is one, otherwise straight off the wall.
    // Never dashes back into the wall you just left.
    private Vector3 GetExitDashDirection(Vector3 n)
    {
        Vector3 away = Flatten(n);
        if (away.sqrMagnitude < 0.01f) away = transform.forward;

        Vector3 dir = Flatten(CameraRelativeInput());
        if (dir.sqrMagnitude < 0.01f)
        {
            dir = away;
        }
        else if (Vector3.Dot(dir, n) < 0f)
        {
            dir = Flatten(Vector3.ProjectOnPlane(dir, n));
            if (dir.sqrMagnitude < 0.01f) dir = away;
        }
        return dir;
    }

    // ------------------------------------------------------------------ helpers

    // Hides the character while climbing and puts every renderer back exactly as it was
    private void SetClimbVisuals(bool climbing)
    {
        if (hideWhileClimbing == null || rendererWasEnabled == null) return;
        if (climbing == visualsHidden) return;
        visualsHidden = climbing;

        for (int i = 0; i < hideWhileClimbing.Length; i++)
        {
            Renderer r = hideWhileClimbing[i];
            if (r == null) continue;

            if (climbing)
            {
                rendererWasEnabled[i] = r.enabled;
                r.enabled = false;
            }
            else
            {
                r.enabled = rendererWasEnabled[i];
            }
        }
    }

    private bool IsClimbable(Vector3 normal)
    {
        float angle = Vector3.Angle(normal, Vector3.up);
        return angle >= minClimbAngle && angle <= maxClimbAngle;
    }

    private bool IsLit(Vector3 point, Vector3 normal, Collider surface)
    {
        int layer = surface != null ? surface.gameObject.layer : 0;
        return LightSampler.IsLit(point, normal, layer, occluderMask, darkThreshold);
    }

    private bool IsGrounded(Vector3 center, float halfHeight)
    {
        return Physics.Raycast(center, Vector3.down, halfHeight + 0.15f,
                               ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore);
    }

    private void GetBody(out Vector3 center, out float radius, out float halfHeight)
    {
        Bounds b = col.bounds;
        center = b.center;
        radius = Mathf.Min(b.extents.x, b.extents.z);
        halfHeight = b.extents.y;
    }

    // WASD in world space (relative to where the player/camera is facing). Zero if no input.
    private Vector3 CameraRelativeInput()
    {
        Vector2 i = controller.MoveInput;
        if (i.sqrMagnitude < 0.0001f) return Vector3.zero;
        return transform.rotation * new Vector3(i.x, 0f, i.y);
    }

    // Horizontal and normalized, or zero if there's no horizontal component
    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
    }

    private void OnDrawGizmosSelected()
    {
        if (!IsClimbing) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(surfacePoint, 0.1f);
        Gizmos.DrawLine(surfacePoint, surfacePoint + surfaceNormal);
    }
}