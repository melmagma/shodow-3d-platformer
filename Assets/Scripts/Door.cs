using UnityEngine;

public class Door : MonoBehaviour
{
    void OnCollisionEnter(Collision collision)
    {
        KeyHolder holder = collision.gameObject.GetComponentInParent<KeyHolder>();
        if (holder == null || holder.keys <= 0) return;

        holder.keys--;
        Open();
    }

    void Open()
    {
        // Placeholder for an open animation later
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
    }
}
