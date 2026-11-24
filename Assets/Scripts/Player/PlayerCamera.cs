using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Mouse look and the third-person orbit camera, with collision so it doesn't clip into walls.
/// The player's body always faces where the camera looks; WASD never turns it.</summary>
public class PlayerCamera : MonoBehaviour
{
    [Header("References")]
    public Transform cam;

    [Header("Look")]
    public float mouseSensitivity = 0.1f;
    public float minPitch = -40f;
    public float maxPitch = 70f;

    [Header("Orbit")]
    public float cameraDistance = 5f;
    public float cameraHeight = 1.1f;

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

    public float Yaw => yaw;
    public float Pitch => pitch;

    private Camera camComponent;
    private float yaw;
    private float pitch;
    private float currentCamDistance;

    private void Awake()
    {
        if (cam == null)
        {
            // e.g. added at runtime: use the camera under the player, or the main camera
            Camera found = GetComponentInChildren<Camera>();
            if (found == null) found = Camera.main;
            if (found != null) cam = found.transform;
        }
        if (cam != null) camComponent = cam.GetComponent<Camera>();

        Cursor.lockState = CursorLockMode.Locked;
        currentCamDistance = cameraDistance;
        yaw = transform.eulerAngles.y;    // start behind the player, whichever way it was placed
    }

    private void Update() => Look();

    private void LateUpdate() => PositionCamera();

    private void Look()
    {
        if (Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        yaw += mouseDelta.x * mouseSensitivity;
        pitch -= mouseDelta.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // The body faces where the camera looks
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // Distance from the camera's center to the corners of its near clip plane.
    // The cast sphere has to be at least this big or the view clips into walls.
    private float GetCameraClearance()
    {
        if (camComponent == null) return cameraRadius;

        float near = camComponent.nearClipPlane;
        float halfH = near * Mathf.Tan(camComponent.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * camComponent.aspect;
        float corner = Mathf.Sqrt(near * near + halfH * halfH + halfW * halfW);

        return Mathf.Max(cameraRadius, corner);
    }

    private void PositionCamera()
    {
        if (cam == null) return;

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
}
