using UnityEngine;

public class HangingLights : MonoBehaviour
{
    public Transform pointA;
    public Transform pointB;
    private Transform currentPoint;
    private float proximity = 0.0001f;
    private float speed = 3.0f;

    void Start()
    {
        currentPoint = pointA;
    }

    void Update()
    {
        Vector3 distance = transform.position - currentPoint.transform.position;

        if (distance.magnitude <= proximity && LeverSwitch.switchOff == false)
        {
            currentPoint = pointB;
        }

        if (distance.magnitude <= proximity && LeverSwitch.switchOff == true)
        {
            currentPoint = pointA;
        }

        transform.position = Vector3.MoveTowards(transform.position, currentPoint.transform.position, Time.deltaTime * speed); 


    }
}

