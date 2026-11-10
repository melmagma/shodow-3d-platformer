using UnityEngine;

public class SecurityCam : MonoBehaviour
{
    [Tooltip("How far it swings to each side of its starting direction, in degrees.")]
    public float sweepAngle = 45f;
    [Tooltip("Seconds for a full left-right-left cycle.")]
    public float cycleTime = 6f;
    [Tooltip("0 to 1. Offsets the start so multiple cameras don't move in sync.")]
    public float phaseOffset = 0f; //for multiple cams

    private Quaternion baseRotation;

    void Awake()
    {
        baseRotation = transform.rotation;
    }

    void Update()
    {
        float t = (Time.time / cycleTime + phaseOffset) * Mathf.PI * 2f;
        float angle = Mathf.Sin(t) * sweepAngle;

        transform.rotation = Quaternion.AngleAxis(angle, Vector3.up) * baseRotation;
    }
}
