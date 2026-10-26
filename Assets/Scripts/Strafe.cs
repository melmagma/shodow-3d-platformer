using UnityEngine;

public class Strafe : MonoBehaviour
{
    [SerializeField] private float speed = 2f;
    [SerializeField] private float distance = 3f;

    private Vector3 startPosition;

    void Start()
    {
        // Save the initial position of the block
        startPosition = transform.position;
    }

    void Update()
    {
        // Calculate the smooth back-and-forth offset
        float offset = Mathf.Sin(Time.time * speed) * distance;

        // Apply it to the X axis (change Vector3.right to Vector3.up or forward if needed)
        transform.position = startPosition + Vector3.right * offset;
    }
}
