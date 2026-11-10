using UnityEngine;

public class UnlockKey : MonoBehaviour
{
    public float bobHeight = 0.25f;
    public float bobSpeed = 2f;
    public float spinSpeed = 90f;

    private Vector3 startPos;
    private float phase;

    void Start()
    {
        startPos = transform.position;
        phase = Random.value * Mathf.PI * 2f; // so multiple keys don't bob in sync
    }

    void Update()
    {
        float y = Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight;
        transform.position = startPos + Vector3.up * y;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    void OnTriggerEnter(Collider other)
    {
        KeyHolder holder = other.GetComponentInParent<KeyHolder>();
        if (holder == null) return;

        holder.keys++;
        Destroy(gameObject);
    }
}
