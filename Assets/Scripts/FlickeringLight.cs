using UnityEngine;

public class FlickeringLight : MonoBehaviour
{
    private Light light;

    public float minIntensity = 0f;
    public float maxIntensity = 5f;
    public float flickerSpeed = 3f;

    private void Start()
    {
        light = GetComponent<Light>();

        InvokeRepeating("Flicker", 0f, flickerSpeed);
    }

    private void Flicker()
    {
        float randomIntensity = Random.Range(minIntensity, maxIntensity);
        light.intensity = randomIntensity;
    }
}
