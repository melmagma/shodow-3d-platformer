using UnityEngine;


public class AmbienceZones : MonoBehaviour
{
    public Collider Area;
    public GameObject Player;

    void Update()
    {
        Vector3 closestPoint = Area.ClosestPoint(Player.transform.position);
        transform.position = closestPoint;
    }
}
