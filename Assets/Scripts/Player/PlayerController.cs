using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's "brain". It reads input, checks the ground once per physics step, and runs the
/// ability components below in a fixed order, so they never fight over the Rigidbody:
///
///   PlayerMovement       walking, air control, landing slide, gravity
///   PlayerJump           jump, coyote time, jump buffer, short hops
///   PlayerDash           dash and air dashes
///   PlayerCamera         mouse look, orbit camera, camera collision (the body faces where the camera looks)
///   PlayerFootsteps      FMOD footsteps (optional: remove it for silent feet)
///   PlayerLightSlowdown  slows you down in bright light (optional: remove it to turn that off)
///
/// Other scripts (like ShadowSwim) reach the components through the properties here.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerJump), typeof(PlayerDash))]
[RequireComponent(typeof(PlayerCamera))]
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;

    [Header("Ground Check")]
    [Tooltip("Length of the ray cast down from the player's pivot. A little more than half the capsule height.")]
    public float groundCheckDistance = 1.3f;

    [HideInInspector] public bool controlLocked; // another script (ShadowSwim) is driving the body
    [System.NonSerialized] public bool jumpInputBlocked; // another script is handling the jump key. NonSerialized so a stale value can never get saved into the scene.

    public PlayerMovement Movement { get; private set; }
    public PlayerJump Jump { get; private set; }
    public PlayerDash Dash { get; private set; }
    public PlayerCamera CameraRig { get; private set; }
    public PlayerFootsteps Footsteps { get; private set; } // can be null
    public PlayerLightSlowdown LightSlowdown { get; private set; } // can be null

    public Rigidbody Body { get; private set; }
    public bool IsGrounded { get; private set; }
    public Vector2 MoveInput { get; private set; }
    public bool JumpHeld { get; private set; }

    public Vector3 CameraRelativeMove => YawRotation * new Vector3(MoveInput.x, 0f, MoveInput.y); // move input as a flat world direction relative to the camera (length 0..1)
    public Vector3 CameraForward => YawRotation * Vector3.forward; // where the camera is facing, flattened
    private Quaternion YawRotation => Quaternion.Euler(0f, CameraRig.Yaw, 0f);

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Body.freezeRotation = true;

        // RequireComponent only adds the parts when PlayerController itself gets added,
        // so a player saved before the split gets them here instead (with default values)
        Movement = GetOrAdd<PlayerMovement>();
        Jump = GetOrAdd<PlayerJump>();
        Dash = GetOrAdd<PlayerDash>();
        CameraRig = GetOrAdd<PlayerCamera>();
        Footsteps = GetComponent<PlayerFootsteps>();
        LightSlowdown = GetComponent<PlayerLightSlowdown>();
    }

    private T GetOrAdd<T>() where T : Component
    {
        if (TryGetComponent(out T existing)) return existing;
        Debug.LogWarning($"[PlayerController] {typeof(T).Name} was missing, so it was added with default values. Add it to the player in the Inspector to tune it.", this);
        return gameObject.AddComponent<T>();
    }

    private void OnEnable() => moveAction.action.Enable();
    private void OnDisable() => moveAction.action.Disable();

    private void Update()
    {
        MoveInput = Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f);

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        JumpHeld = kb.spaceKey.isPressed;
        if (kb.spaceKey.wasReleasedThisFrame) Jump.OnJumpReleased();
        if (kb.spaceKey.wasPressedThisFrame && !jumpInputBlocked) Jump.OnJumpPressed();
        if (kb.leftShiftKey.wasPressedThisFrame) Dash.OnDashPressed();
    }

    private void FixedUpdate()
    {
        if (controlLocked)
        {
            Jump.CancelAll();
            Dash.Cancel();
            if (Footsteps != null) Footsteps.Stop();
            return;
        }

        IsGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);

        // The order matters: dashing replaces walking and jumping for that step, gravity goes last
        Movement.BeginStep(IsGrounded);
        if (LightSlowdown != null) LightSlowdown.Step(Time.fixedDeltaTime);

        bool dashing = Dash.Step(IsGrounded);
        if (!dashing) Movement.Move(IsGrounded);
        Jump.Step(IsGrounded, !dashing);

        Movement.ApplyGravity(IsGrounded);
        if (Footsteps != null) Footsteps.Step(IsGrounded);
    }

    // ------------------------------------------------------------------ called by ShadowSwim

    // Hands control back and launches the body. Swim jumps fire when space is let go,
    // so there's no release left to cut them short.
    public void SwimLaunch(Vector3 launchVelocity)
    {
        controlLocked = false;
        Jump.CancelAll();
        Dash.Cancel();
        Body.linearVelocity = launchVelocity;
    }
}
