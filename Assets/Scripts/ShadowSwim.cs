using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Splatoon-style ink swimming, using shadow instead of ink.
///
/// Press the swim key while standing on a swimmable floor that's in shadow, or while pushing into a
/// swimmable wall that's in shadow, to sink into it. You stay in until you JUMP OUT or press the swim key again.
///  - FLOORS: you're hidden, faster and immune to light. The edge of the shadow acts like a wall,
///    so you can't swim out into light, onto a normal floor, or off a ledge.
///  - WALLS: push into a dark wall while swimming to climb it (W = up, S = down, A/D = sideways).
///    Light acts like a solid edge. Climb down onto a dark floor to keep swimming there.
///    Reaching the top vaults you over the lip: land in shadow and you keep swimming, anywhere else and you're out.
///  - JUMP: tap space to jump out. Hold it past the charge time, then let go, for a boosted jump out.
///  - Light never kicks you out. You can't swim from shadow into light, but if a light lands on you,
///    you can still swim anywhere swimmable (so you're never stuck).
///
/// Put this on the Player next to PlayerController. Needs LightSampler.cs.
/// The player's own colliders and lights (like the 'vision' spotlight) never count, whatever layer the player is on.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(PlayerController))]
public class ShadowSwim : MonoBehaviour
{
    private enum SwimButtonMode { Toggle, Hold }
    private enum Mode { None, Floor, Wall, Airborne }

    [Header("Surfaces & Light")]
    [Tooltip("Layers you can swim in. Flat surfaces on these layers are floors you sink into, steep ones are walls you climb.")]
    [FormerlySerializedAs("wallLayers")]
    [SerializeField] private LayerMask swimmableLayers;
    [Tooltip("Everything that blocks light. The player's own colliders are skipped automatically.")]
    [SerializeField] private LayerMask occluders = ~0;
    [Tooltip("Light on a surface below this counts as shadow. Tune it using the readout from your LightDetector.")]
    [SerializeField] private float darkThreshold = 0.1f;

    [Header("Input")]
    [Tooltip("Toggle: press to sink in, press again to come out. Hold: you stay in while the key is held, and sink in by yourself when you reach a shadow while holding it.")]
    [SerializeField] private SwimButtonMode swimButton = SwimButtonMode.Toggle;
    [FormerlySerializedAs("toggleKey")]
    [SerializeField] private Key swimKey = Key.LeftCtrl;
    [Tooltip("Must match PlayerController's jump key (space).")]
    [FormerlySerializedAs("exitJumpKey")]
    [SerializeField] private Key jumpKey = Key.Space;

    [Header("Floor Swimming")]
    [Tooltip("Swim speed = PlayerMovement's Move Speed times this.")]
    [SerializeField] private float swimSpeedMultiplier = 1.5f;
    [Tooltip("Units/sec². How quickly you speed up, slow down and turn while swimming.")]
    [SerializeField] private float swimAcceleration = 40f;

    [Header("Wall Climbing")]
    [SerializeField] private float climbSpeed = 7f;
    [Tooltip("How close a wall has to be to your collider (while you push into it) to stick to it.")]
    [SerializeField] private float attachReach = 0.4f;
    [Tooltip("Steepness that counts as a wall: the angle between the surface normal and straight up. 90 = vertical. Anything flatter than the min is a floor.")]
    [SerializeField] private float minClimbAngle = 60f;
    [SerializeField] private float maxClimbAngle = 110f;
    [Tooltip("Seconds before you can stick to a wall again after leaving one.")]
    [SerializeField] private float reattachDelay = 0.25f;
    [Tooltip("Vaulting over the top of a wall: the upward pop once your feet clear the lip...")]
    [SerializeField] private float ledgeHopUp = 3f;
    [Tooltip("...and the speed over the lip.")]
    [SerializeField] private float ledgeHopForward = 3.5f;

    [Header("Jumping")]
    [Tooltip("Hold jump this long to charge a boosted jump.")]
    [SerializeField] private float boostChargeTime = 0.45f;
    [Tooltip("Off: the boost fires when you let go, so you can hold a full charge. On: it fires the moment the charge is full.")]
    [SerializeField] private bool boostOnFullCharge = false;
    [Tooltip("Move / climb speed multiplier while charging.")]
    [Range(0f, 1f)] [SerializeField] private float chargeMoveMultiplier = 0.35f;
    [Tooltip("A boosted jump's upward speed = the tap jump's upward speed times this (floor: PlayerJump's Jump Force, wall: Wall Jump Up Speed). 1.5 = about 2.25x the height.")]
    [SerializeField] private float boostJumpMultiplier = 1.5f;
    [Tooltip("Tap jump off a wall: speed away from it...")]
    [FormerlySerializedAs("exitJumpOutSpeed")]
    [SerializeField] private float wallJumpOutSpeed = 5f;
    [Tooltip("...and upward speed.")]
    [FormerlySerializedAs("exitJumpUpSpeed")]
    [SerializeField] private float wallJumpUpSpeed = 8f;
    [Tooltip("Boosted jump off a wall: speed away from it. Keep it low so you rocket up past the wall instead of away from it.")]
    [SerializeField] private float boostWallOutSpeed = 2f;

    [Header("Visuals")]
    [Tooltip("Renderers hidden while sunk in. Leave empty to hide every MeshRenderer and SkinnedMeshRenderer on the player.")]
    [FormerlySerializedAs("hideWhileClimbing")]
    [SerializeField] private Renderer[] hideWhileSubmerged;

    [Header("Feedback")]
    public UnityEvent onEnterSwim; // swim state turned on
    public UnityEvent onExitSwim; // swim state turned off
    public UnityEvent onSwimDenied; // pressed the swim key but there's no shadow to sink into (a "nope" sound)
    public UnityEvent onSubmerge; // sank into a shadow, floor or wall (we can either swap to a swimming model and add splash sfx)
    public UnityEvent onEmerge; // popped back out
    public UnityEvent onAttachWall;
    public UnityEvent onDetachWall;
    public UnityEvent onBoostReady; // charge is full
    public UnityEvent onJump; // tap jump while swimming
    public UnityEvent onBoostJump; // boosted jump

    public bool IsSwimming => mode != Mode.None;
    public bool IsClimbing => mode == Mode.Wall;
    public bool IsSubmerged { get; private set; } // Sunk into a shadow surface (floor or wall): hidden and immune to light.
    public bool IsCharging { get; private set; }
    public float ChargeProgress => IsCharging ? Mathf.Clamp01(chargeTimer / boostChargeTime) : 0f; // 0..1 while charging a boosted jump. Handy for UI or an FMOD parameter.
    /// <summary>Light damage is skipped while this is true (Health checks it).</summary>
    public bool IsLightImmune => IsSubmerged;

    private const int SolidMask = Physics.DefaultRaycastLayers; // Internal tuning
    private const float ProbeRadius = 0.2f; // raycasted sphere used to find a wall to stick to
    private const float MinPushDot = 0.5f; // how directly you must push into a wall to stick (0.5 = within 60 degrees)
    private const float SameFaceDot = 0.95f;       // normals closer than this (about 18 degrees) count as the same face
    private const float ReacquireReach = 0.5f;     // how far past the collider we look for the wall each step
    private const float WrapDepth = 0.15f;         // outer corners / tops: how far behind the wall plane we look back from
    private const float WrapReach = 0.6f;
    private const float CornerProbe = 0.3f;        // inner corners: how early we notice a wall ahead
    private const float LookAhead = 0.35f;         // how far ahead on the wall we check for light
    private const float PeekHeight = 0.3f;
    private const float PeekDepth = 0.4f;
    private const float SkinGap = 0.03f;           // hover this far off the wall: no contact means no friction and no jitter
    private const float MaxStickSpeed = 10f;
    private const float LostSurfaceGrace = 0.15f;
    private const float UnswimmableGrace = 0.15f;  // pushed onto something you can't swim in (moving platform, plain wall): out after this long
    private const float LedgeReattachDelay = 0.5f;
    private const float VaultClearance = 0.15f;    // how far your feet rise above the lip before you pop over it
    private const float MaxVaultTime = 0.6f;
    private const float GroundProbeExtra = 0.15f;
    private const float FloorEdgeMargin = 0.25f;   // floors: how far ahead of your center the shadow has to continue
    private const float FloorStepDown = 0.5f;      // floors: the biggest drop you can swim down (anything deeper is a ledge)
    private const float LaunchLockTime = 0.2f;     // after a swim jump, the jump key is ignored this long (no double jumps)

    private PlayerController controller;
    private Rigidbody rb;
    private Collider col;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private Mode mode;

    // Input, read in Update and used in FixedUpdate
    private bool swimPressed, swimHeld;
    private bool jumpPressed, jumpReleased, jumpHeld;

    // Ground
    private bool grounded;
    private RaycastHit groundHit;

    // Surface
    private Vector3 surfacePoint;
    private Vector3 surfaceNormal;
    private Vector3 rightHint;      // keeps "right" consistent around corners
    private Vector3 lastMoveDir;    // last direction we moved along the wall
    private float reattachTimer;
    private float unswimmableTimer;
    private float lostTimer;
    private bool onDark;            // the surface under us was in shadow last step
    private bool savedUseGravity;
    private bool vaulting;
    private float vaultTopY;
    private float vaultTimer;

    // Jump
    private float chargeTimer;
    private bool boostReady;
    private float launchLock;

    // Visuals
    private bool[] rendererWasEnabled;
    private bool visualsHidden;

    private void Reset()
    {
        swimmableLayers = LayerMask.GetMask("Wall");
    }

    private void OnValidate()
    {
        maxClimbAngle = Mathf.Max(maxClimbAngle, minClimbAngle);
        boostChargeTime = Mathf.Max(0.01f, boostChargeTime);
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

        if (swimmableLayers.value == 0)
            Debug.LogWarning("[ShadowSwim] Swimmable Layers is empty, so there's nothing to swim in. Tick the Wall layer (or whatever layer your shadow surfaces use).", this);

        if (hideWhileSubmerged == null || hideWhileSubmerged.Length == 0)
        {
            var found = new List<Renderer>();
            found.AddRange(GetComponentsInChildren<MeshRenderer>());
            found.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>());
            hideWhileSubmerged = found.ToArray();
        }
        rendererWasEnabled = new bool[hideWhileSubmerged.Length];
    }

    private void OnDisable()
    {
        // Quietly put everything back. No events: this also runs when the scene unloads.
        if (mode == Mode.Wall && rb != null) rb.useGravity = savedUseGravity;
        if (controller != null)
        {
            if (mode == Mode.Floor || mode == Mode.Wall) controller.controlLocked = false;
            controller.jumpInputBlocked = false;
        }
        SetHidden(false);

        mode = Mode.None;
        IsSubmerged = IsCharging = false;
        vaulting = false;
        launchLock = 0f;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        swimHeld = kb[swimKey].isPressed;
        jumpHeld = kb[jumpKey].isPressed;
        if (kb[swimKey].wasPressedThisFrame) swimPressed = true;
        if (kb[jumpKey].wasPressedThisFrame) jumpPressed = true;
        if (kb[jumpKey].wasReleasedThisFrame) jumpReleased = true;
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        reattachTimer -= dt;
        launchLock -= dt;

        UpdateGround();
        HandleSwimButton();
        if (IsSwimming) HandleJump(dt);

        switch (mode)
        {
            case Mode.Floor: FloorSwimStep(dt); break;
            case Mode.Wall: ClimbStep(dt); break;
            case Mode.Airborne: AirborneStep(); break;
        }

        // While swimming (and just after jumping out) the jump key is ours, not PlayerController's
        controller.jumpInputBlocked = IsSwimming || launchLock > 0f;
        swimPressed = jumpPressed = jumpReleased = false;
    }

    // ------------------------------------------------------------------ swim state

    private void HandleSwimButton()
    {
        if (swimButton == SwimButtonMode.Toggle)
        {
            if (!swimPressed) return;
            if (IsSwimming) ExitSwimState();
            else if (!TryEnterSwim()) onSwimDenied?.Invoke();
        }
        else
        {
            if (swimHeld && !IsSwimming)
            {
                if (!TryEnterSwim() && swimPressed) onSwimDenied?.Invoke();
            }
            else if (!swimHeld && IsSwimming)
            {
                ExitSwimState();
            }
        }
    }

    // Sinks into the dark wall you're pushing into, or the dark floor you're standing on. False if there's neither.
    private bool TryEnterSwim()
    {
        if (launchLock > 0f) return false;

        Vector3 dir = controller.CameraRelativeMove;
        if (dir.sqrMagnitude < 0.01f) dir = controller.CameraForward;   // not pushing anywhere: check where you're facing

        if (reattachTimer <= 0f && FindDarkWallAhead(dir, out RaycastHit wall))
        {
            onEnterSwim?.Invoke();
            Attach(wall);
            return true;
        }
        if (grounded && IsDarkFloor(groundHit))
        {
            onEnterSwim?.Invoke();
            EnterFloor();
            return true;
        }
        return false;
    }

    private void ExitSwimState()
    {
        if (!IsSwimming) return;

        CancelCharge();
        if (mode == Mode.Wall)
        {
            DetachFromWall(reattachDelay);
            rb.linearVelocity = Vector3.zero;   // drop off the wall
        }
        controller.controlLocked = false;       // PlayerController takes over again
        mode = Mode.None;
        SetSubmerged(false);
        onExitSwim?.Invoke();
    }

    // ------------------------------------------------------------------ floor swimming

    private void UpdateGround()
    {
        grounded = false;
        if (launchLock > 0f) return;   // just jumped: don't count the floor we're leaving

        GetBody(out Vector3 center, out float radius, out float halfHeight);
        float r = radius * 0.9f;
        float dist = Mathf.Max(0.01f, halfHeight - r + GroundProbeExtra);
        grounded = Cast(center, Vector3.down, dist, SolidMask, out groundHit, r) && IsFloor(groundHit.normal);
    }

    private void EnterFloor()
    {
        mode = Mode.Floor;
        unswimmableTimer = 0f;
        lostTimer = 0f;
        controller.controlLocked = true;    // this script drives the body while you're sunk in
        SetSubmerged(true);
    }

    private void FloorSwimStep(float dt)
    {
        // Pushing into a dark wall: climb it
        if (reattachTimer <= 0f && FindDarkWallAhead(controller.CameraRelativeMove, out RaycastHit wall))
        {
            Attach(wall);
            return;
        }

        // The floor went away (moving platform, knocked off): you're in the air until you land
        if (!grounded)
        {
            lostTimer += dt;
            if (lostTimer >= LostSurfaceGrace) GoAirborne();
            return;
        }
        lostTimer = 0f;

        // Got pushed onto something you can't swim in at all -> out (nothing left to swim in)
        bool swimmable = IsSwimmable(groundHit.collider);
        unswimmableTimer = swimmable ? 0f : unswimmableTimer + dt;
        if (unswimmableTimer >= UnswimmableGrace)
        {
            ExitSwimState();
            return;
        }

        // Swim, but never from shadow into light, onto a normal floor, or off a ledge.
        // If a light is on you right now, light doesn't block you (so you can always swim back out of it).
        bool dark = swimmable && !IsLit(groundHit);
        float speed = controller.Movement.moveSpeed * swimSpeedMultiplier * (IsCharging ? chargeMoveMultiplier : 1f);
        Vector3 v = rb.linearVelocity;
        Vector3 horizontal = new Vector3(v.x, 0f, v.z);
        horizontal = Vector3.MoveTowards(horizontal, controller.CameraRelativeMove * speed, swimAcceleration * dt);

        if (horizontal.sqrMagnitude > 0.0001f)
            horizontal = KeepInsideShadow(horizontal, dt, dark);

        rb.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
    }

    // Stops the move (or slides along the edge) if it would take you into light, onto a floor you can't swim in, or off a ledge
    private Vector3 KeepInsideShadow(Vector3 horizontal, float dt, bool avoidLight)
    {
        float reach = horizontal.magnitude * dt;
        if (!IsFloorBlocked(horizontal, reach, avoidLight)) return horizontal;

        // Blocked diagonally? Try just the forward/back part, then just the sideways part.
        Vector3 along = Vector3.Project(horizontal, controller.CameraForward);
        Vector3 side = horizontal - along;
        if (along.sqrMagnitude > 0.0001f && side.sqrMagnitude > 0.0001f)
        {
            if (!IsFloorBlocked(along, reach, avoidLight)) return along;
            if (!IsFloorBlocked(side, reach, avoidLight)) return side;
        }
        return Vector3.zero;
    }

    private bool IsFloorBlocked(Vector3 dir, float reach, bool avoidLight)
    {
        GetBody(out Vector3 center, out _, out float halfHeight);
        Vector3 ahead = center + dir.normalized * (FloorEdgeMargin + reach);

        if (!Cast(ahead, Vector3.down, halfHeight + FloorStepDown, SolidMask, out RaycastHit h))
            return true;   // a ledge: nothing to swim onto
        return !IsSwimFloor(h, avoidLight);
    }

    // Off the surface but still swimming (vaulted over a wall top, or the floor went away). You're visible for
    // the hop, then sink into the floor you land on if it's dark, or come out properly if it isn't.
    private void GoAirborne()
    {
        mode = Mode.Airborne;
        controller.controlLocked = false;   // PlayerController handles the air
        SetSubmerged(false);
    }

    private void AirborneStep()
    {
        if (reattachTimer <= 0f && FindDarkWallAhead(controller.CameraRelativeMove, out RaycastHit wall))
        {
            Attach(wall);
            return;
        }

        if (!grounded || rb.linearVelocity.y > 0.1f) return;   // still in the air

        if (IsDarkFloor(groundHit)) EnterFloor();
        else ExitSwimState();
    }

    // ------------------------------------------------------------------ jumping

    private void HandleJump(float dt)
    {
        bool onSurface = mode == Mode.Floor || mode == Mode.Wall;

        if (jumpPressed && onSurface && launchLock <= 0f)
            StartCharge();

        if (!IsCharging) return;

        if (!onSurface) { CancelCharge(); return; }   // got knocked off mid-charge

        chargeTimer += dt;
        if (!boostReady && chargeTimer >= boostChargeTime)
        {
            boostReady = true;
            onBoostReady?.Invoke();
            if (boostOnFullCharge) { Launch(true); return; }
        }

        if (jumpReleased || !jumpHeld) Launch(boostReady);
    }

    private void StartCharge()
    {
        IsCharging = true;
        chargeTimer = 0f;
        boostReady = false;
    }

    private void CancelCharge()
    {
        IsCharging = false;
        chargeTimer = 0f;
        boostReady = false;
    }

    // Jumping out: the only way out besides the swim key
    private void Launch(bool boosted)
    {
        CancelCharge();
        launchLock = LaunchLockTime;

        float boost = boosted ? boostJumpMultiplier : 1f;
        Vector3 velocity;
        if (mode == Mode.Wall)
        {
            Vector3 away = Flatten(surfaceNormal);
            velocity = Vector3.up * (wallJumpUpSpeed * boost)
                     + away * (boosted ? boostWallOutSpeed : wallJumpOutSpeed);
            DetachFromWall(reattachDelay);
        }
        else
        {
            Vector3 v = rb.linearVelocity;
            velocity = new Vector3(v.x, controller.Jump.jumpForce * boost, v.z);
        }

        mode = Mode.None;
        SetSubmerged(false);
        controller.SwimLaunch(velocity);
        (boosted ? onBoostJump : onJump)?.Invoke();
        onExitSwim?.Invoke();
    }

    // ------------------------------------------------------------------ attaching

    // A dark swimmable wall within reach in 'dir' (which has to point into it)
    private bool FindDarkWallAhead(Vector3 dir, out RaycastHit hit)
    {
        hit = default;
        dir = Flatten(dir);
        if (dir == Vector3.zero) return false;

        GetBody(out Vector3 center, out float radius, out _);
        return Cast(center, dir, radius + attachReach - ProbeRadius, SolidMask, out hit, ProbeRadius)
               && Vector3.Dot(dir, -hit.normal) >= MinPushDot
               && IsDarkWall(hit);
    }

    private void Attach(RaycastHit hit)
    {
        mode = Mode.Wall;
        vaulting = false;
        surfacePoint = hit.point;
        surfaceNormal = hit.normal;
        rightHint = Vector3.zero;
        lastMoveDir = Vector3.zero;
        unswimmableTimer = 0f;
        lostTimer = 0f;
        onDark = true;

        savedUseGravity = rb.useGravity;
        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero;      // kill fall speed so you stick instead of sliding
        controller.controlLocked = true;       // this script drives the body while climbing

        SetSubmerged(true);
        onAttachWall?.Invoke();
    }

    // ------------------------------------------------------------------ climbing

    private void ClimbStep(float dt)
    {
        GetBody(out Vector3 center, out float radius, out float halfHeight);

        if (vaulting)
        {
            VaultStep(center, halfHeight, dt);
            return;
        }

        bool movingUp = Vector3.Dot(lastMoveDir, Vector3.up) > 0.3f;

        // 1) Find the surface under us again. It may curve, wrap round a corner, or end at the top.
        bool found = Cast(center, -surfaceNormal, radius + ReacquireReach, SolidMask, out RaycastHit hit);
        if (!found && lastMoveDir.sqrMagnitude > 0.01f)
        {
            // Just past an edge: look back at the face round the corner (or the top) from behind the wall plane
            Vector3 wrapOrigin = center - surfaceNormal * (radius + WrapDepth);
            found = Cast(wrapOrigin, -lastMoveDir, radius + WrapReach, SolidMask, out hit);
        }

        if (found)
        {
            lostTimer = 0f;

            if (movingUp && IsFloor(hit.normal))
            {
                StartVault(Mathf.Max(hit.point.y, surfacePoint.y));   // reached the top
                return;
            }

            // Follow the surface. Round a corner only onto a face you could move onto
            // (from shadow: a dark one. From under a light: any swimmable one).
            bool swimmableWall = IsSwimmable(hit.collider) && IsClimbable(hit.normal);
            bool dark = swimmableWall && !IsLit(hit);
            if (Vector3.Dot(hit.normal, surfaceNormal) > SameFaceDot || (swimmableWall && (dark || !onDark)))
                AdoptSurface(hit);
            onDark = dark;

            // 2) Got pushed onto something you can't swim in at all -> out (nothing left to swim in)
            unswimmableTimer = swimmableWall ? 0f : unswimmableTimer + dt;
            if (unswimmableTimer >= UnswimmableGrace)
            {
                ExitSwimState();
                return;
            }
        }
        else
        {
            lostTimer += dt;
            if (lostTimer >= LostSurfaceGrace)
            {
                if (movingUp) StartVault(surfacePoint.y);
                else ExitSwimState();   // ran out of wall
                return;
            }
        }

        // 3) Input along the wall (W = up, S = down, A/D = sideways)
        Vector2 input = controller.MoveInput;
        if (input.y < -0.1f && grounded)
        {
            // Climbed down to the floor: keep swimming there if it's dark, otherwise that's as low as you go
            if (IsDarkFloor(groundHit))
            {
                DetachFromWall(reattachDelay);
                EnterFloor();
                return;
            }
            input.y = 0f;
        }

        GetSurfaceAxes(out Vector3 right, out Vector3 up);
        Vector3 wish = right * input.x + up * input.y;

        if (wish.sqrMagnitude > 0.0001f)
        {
            lastMoveDir = wish.normalized;

            // Inner corner: a wall you can swim on right ahead -> climb onto it
            if (Cast(center, lastMoveDir, radius + CornerProbe, SolidMask, out RaycastHit ahead)
                && Vector3.Dot(ahead.normal, surfaceNormal) < SameFaceDot
                && IsSwimWall(ahead, onDark))
            {
                AdoptSurface(ahead);
                GetSurfaceAxes(out right, out up);
                wish = right * input.x + up * input.y;
            }

            // Light (from shadow) and unswimmable surfaces act like a solid edge. Slide along it if you can.
            if (IsBlockedAhead(wish, onDark)) wish = SlideAlongEdge(wish, right, onDark);

            if (IsCharging) wish *= chargeMoveMultiplier;
        }

        // 4) Move along the wall, hovering a hair off it
        float gap = Vector3.Dot(center - surfacePoint, surfaceNormal) - radius;
        float pull = Mathf.Clamp((SkinGap - gap) / dt, -MaxStickSpeed, MaxStickSpeed);
        rb.linearVelocity = wish * climbSpeed + surfaceNormal * pull;
    }

    private void AdoptSurface(RaycastHit hit)
    {
        // Carry "right" and the move direction round the corner so the controls stay continuous
        if (Vector3.Dot(hit.normal, surfaceNormal) < 0.999f)
        {
            Quaternion turn = Quaternion.FromToRotation(surfaceNormal, hit.normal);
            rightHint = turn * rightHint;
            lastMoveDir = turn * lastMoveDir;
        }
        surfacePoint = hit.point;
        surfaceNormal = hit.normal;
    }

    // True if moving a little way along 'dir' would take us onto something we can't swim in
    // (or into light, when avoidLight is on)
    private bool IsBlockedAhead(Vector3 dir, bool avoidLight)
    {
        if (dir.sqrMagnitude < 0.0001f) return false;
        Vector3 d = dir.normalized;
        Vector3 ahead = surfacePoint + d * LookAhead;

        // Same face, a little further on
        if (Cast(ahead + surfaceNormal * PeekHeight, -surfaceNormal, PeekHeight + PeekDepth, SolidMask, out RaycastHit h))
            return !IsSwimWall(h, avoidLight);

        // Past an edge: check the face round the corner. The top of the wall is fine, we vault over it.
        if (Cast(ahead - surfaceNormal * WrapDepth, -d, LookAhead + WrapReach, SolidMask, out h))
            return !IsFloor(h.normal) && !IsSwimWall(h, avoidLight);

        return false;   // nothing there at all: the climb step deals with running out of wall
    }

    // Blocked diagonally? Try just the sideways part, then just the up/down part.
    private Vector3 SlideAlongEdge(Vector3 wish, Vector3 right, bool avoidLight)
    {
        Vector3 side = Vector3.Project(wish, right);
        Vector3 vertical = wish - side;
        if (side.sqrMagnitude < 0.0001f || vertical.sqrMagnitude < 0.0001f) return Vector3.zero;   // pushing straight into it

        if (!IsBlockedAhead(side, avoidLight)) return side;
        if (!IsBlockedAhead(vertical, avoidLight)) return vertical;
        return Vector3.zero;
    }

    // "Up" on the wall is world-up. "Right" matches the screen's right, and stays consistent around corners.
    private void GetSurfaceAxes(out Vector3 right, out Vector3 up)
    {
        Vector3 n = surfaceNormal;
        Transform cam = controller.CameraRig.cam;

        up = Vector3.ProjectOnPlane(Vector3.up, n);
        if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(cam.forward, n);
        up.Normalize();

        right = Vector3.Cross(n, up);

        // The camera picks the sign. When it looks along the wall (corners), keep last step's right instead.
        float camDot = Vector3.Dot(right, cam.right);
        float sign = (Mathf.Abs(camDot) > 0.3f || rightHint == Vector3.zero) ? camDot : Vector3.Dot(right, rightHint);
        if (sign < 0f) right = -right;

        rightHint = right;
    }

    // Reached the top: rise straight up until your feet clear the lip, then pop over it
    private void StartVault(float lipHeight)
    {
        vaulting = true;
        vaultTopY = lipHeight;
        vaultTimer = 0f;
    }

    private void VaultStep(Vector3 center, float halfHeight, float dt)
    {
        vaultTimer += dt;
        bool cleared = center.y - halfHeight >= vaultTopY + VaultClearance;
        if (!cleared && vaultTimer < MaxVaultTime)
        {
            rb.linearVelocity = Vector3.up * climbSpeed;
            return;
        }

        Vector3 over = Flatten(-surfaceNormal);     // into the wall = forward over its top
        DetachFromWall(LedgeReattachDelay);
        rb.linearVelocity = Vector3.up * ledgeHopUp + over * ledgeHopForward;
        GoAirborne();                               // land in shadow to keep swimming
    }

    // Lets go of the wall (gravity back on). The caller decides what happens next.
    private void DetachFromWall(float cooldown)
    {
        vaulting = false;
        reattachTimer = cooldown;
        unswimmableTimer = 0f;
        lostTimer = 0f;
        rb.useGravity = savedUseGravity;
        onDetachWall?.Invoke();
    }

    // ------------------------------------------------------------------ helpers

    private void SetSubmerged(bool value)
    {
        if (IsSubmerged == value) return;
        IsSubmerged = value;
        SetHidden(value);
        (value ? onSubmerge : onEmerge)?.Invoke();
    }

    // Hides the character and puts every renderer back exactly as it was
    private void SetHidden(bool hidden)
    {
        if (hideWhileSubmerged == null || rendererWasEnabled == null) return;
        if (hidden == visualsHidden) return;
        visualsHidden = hidden;

        for (int i = 0; i < hideWhileSubmerged.Length; i++)
        {
            Renderer r = hideWhileSubmerged[i];
            if (r == null) continue;

            if (hidden)
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

    private bool IsSwimmable(Collider c) => (swimmableLayers.value & (1 << c.gameObject.layer)) != 0;

    private bool IsFloor(Vector3 normal) => Vector3.Angle(normal, Vector3.up) < minClimbAngle;

    private bool IsClimbable(Vector3 normal)
    {
        float angle = Vector3.Angle(normal, Vector3.up);
        return angle >= minClimbAngle && angle <= maxClimbAngle;
    }

    // Surfaces you can swim in: swimmable layer, the right steepness, and (if avoidLight) in shadow.
    // The light check runs last, so it's skipped when it isn't needed.
    private bool IsSwimWall(RaycastHit h, bool avoidLight) => IsSwimmable(h.collider) && IsClimbable(h.normal) && (!avoidLight || !IsLit(h));
    private bool IsSwimFloor(RaycastHit h, bool avoidLight) => IsSwimmable(h.collider) && IsFloor(h.normal) && (!avoidLight || !IsLit(h));
    private bool IsDarkWall(RaycastHit h) => IsSwimWall(h, true);
    private bool IsDarkFloor(RaycastHit h) => IsSwimFloor(h, true);

    // Passing our own transform makes LightSampler skip the player's body and the player's own lights
    private bool IsLit(RaycastHit h) =>
        LightSampler.IsLit(h.point, h.normal, h.collider.gameObject.layer, occluders, darkThreshold, transform);

    // Raycast (or sphere cast when radius > 0) that skips the player's own colliders. Returns the closest hit.
    private bool Cast(Vector3 origin, Vector3 dir, float distance, int mask, out RaycastHit closest, float radius = 0f)
    {
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, dir, hits, distance, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, dir, hits, distance, mask, QueryTriggerInteraction.Ignore);

        closest = default;
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hits[i];
            if (h.distance <= 0f || h.distance >= best) continue;   // overlapping at the start, or further than one we have
            if (h.collider.attachedRigidbody == rb) continue;      // our own body
            closest = h;
            best = h.distance;
        }
        return best < float.PositiveInfinity;
    }

    private void GetBody(out Vector3 center, out float radius, out float halfHeight)
    {
        Bounds b = col.bounds;
        center = b.center;
        radius = Mathf.Min(b.extents.x, b.extents.z);
        halfHeight = b.extents.y;
    }

    // Horizontal and normalized, or zero if there's no horizontal part
    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
    }

    private void OnDrawGizmosSelected()
    {
        if (!IsClimbing) return;
        Gizmos.color = vaulting ? Color.yellow : Color.cyan;
        Gizmos.DrawWireSphere(surfacePoint, 0.1f);
        Gizmos.DrawLine(surfacePoint, surfacePoint + surfaceNormal);
    }
}
