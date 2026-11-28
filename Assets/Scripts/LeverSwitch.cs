using UnityEngine;

public class LeverSwitch : MonoBehaviour
{
    public static bool switchOff = true;
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            switchOff = false;
        }
    }
}
