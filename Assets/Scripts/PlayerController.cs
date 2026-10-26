using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public Transform cam;

    public float moveSpeed = 6f;
    public float jumpForce = 7f;

    public float mouseSensitivity = 0.3f;
    public float cameraDistance = 5f;
    public float cameraHeight = 1.3f;

    private Rigidbody rb;
    private float yaw;
    private float pitch;

    [SerializeField] private InputActionReference moveAction;
    private void OnEnable() => moveAction.action.Enable();
    private void OnDisable() => moveAction.action.Disable();
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        Cursor.lockState = CursorLockMode.Locked;
    }

    // Update is called once per frame
    void Update()
    {
        Look();
        Jump();
    }

    private void FixedUpdate(){
        Move();
    }

    private void LateUpdate(){
        PositionCamera();
    }

    
    void Look(){
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        yaw += mouseDelta.x * mouseSensitivity;
        pitch -= mouseDelta.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -40f, 70f);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

    }

    Vector3 GetMoveInput(){
        // Reads WASD / Left Stick as a Vector2
        Vector2 input = moveAction.action.ReadValue<Vector2>(); 
        
        // Maps X to X, and Y to Z, then normalizes
        return new Vector3(input.x, 0f, input.y).normalized;
    }

    void Jump(){
        if(Keyboard.current.spaceKey.wasPressedThisFrame && IsGrounded())
            rb.AddForce(Vector2.up * jumpForce, ForceMode.Impulse);
    }

    bool IsGrounded(){
        return Physics.Raycast(transform.position, Vector3.down, 1.1f);
    }

    void PositionCamera(){
        Quaternion camRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = transform.position + Vector3.up * cameraHeight;

        cam.position = focusPoint - camRotation * Vector3.forward *cameraDistance;
        cam.rotation = camRotation;
    }

    void Move(){
        Vector3 input =  GetMoveInput();
        Vector3 moveDir = transform.forward * input.z + transform.right * input.x;

        Vector3 velocity = moveDir * moveSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;
    }
}
