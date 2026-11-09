using UnityEngine;

public class LightDetector : MonoBehaviour
{
    [SerializeField] private Transform targetObject;
    [SerializeField] private LayerMask occlusionLayers; // Layers that block light (e.g., Default, Environment)

    [Header("Debug")]
    [SerializeField] private bool logToConsole = true;
    
    private void Update()
    {
        // Make sure we have a target assigned before calculating
        if (targetObject != null)
        {
            float intensity = GetDirectLightIntensityOnObject(targetObject);
            
            if (logToConsole)
            {
                // Prints the value to the Unity Console window
                Debug.Log($"[Light Detector] {targetObject.name} current light intensity: {intensity:F2}");
            }
        }
    }

    public float GetDirectLightIntensityOnObject(Transform targetObject)
    {
        float totalIntensity = 0f;
        Vector3 targetPos = targetObject.position;

        // Find all active lights in the scene (can be cached for performance)
        Light[] allLights = FindObjectsByType<Light>();

        foreach (Light light in allLights)
        {
            if (!light.enabled || light.intensity <= 0) continue;

            Vector3 lightDir;
            float distance;
            float attenuation = 1f;

            if (light.type == LightType.Directional)
            {
                // Directional lights point along their forward axis; the "direction to the light" is the opposite
                lightDir = -light.transform.forward;
                distance = Mathf.Infinity;
            }
            else if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                Vector3 toLight = light.transform.position - targetPos;
                distance = toLight.magnitude;          // measure BEFORE normalizing
                lightDir = toLight / distance;

                if (distance > light.range) continue;

                attenuation = Mathf.Clamp01(1.0f - (distance / light.range));
                attenuation *= attenuation;

                if (light.type == LightType.Spot)
                {
                    float angle = Vector3.Angle(-light.transform.forward, lightDir);
                    if (angle > light.spotAngle * 0.5f) continue;

                    float outerRad = light.spotAngle * 0.5f * Mathf.Deg2Rad;
                    float innerRad = light.innerSpotAngle * 0.5f * Mathf.Deg2Rad;
                    float cosAngle = Vector3.Dot(-light.transform.forward, lightDir);
                    attenuation *= Mathf.Clamp01((cosAngle - Mathf.Cos(outerRad)) / (Mathf.Cos(innerRad) - Mathf.Cos(outerRad)));
                }
            }
            else continue;
            // Raycast check to see if the light is blocked by geometry
            Vector3 rayStart = (light.type == LightType.Directional) ? targetPos + (lightDir * 100f) : light.transform.position;
            Vector3 rayDir = (light.type == LightType.Directional) ? -lightDir : (targetPos - light.transform.position).normalized;
            float rayDist = (light.type == LightType.Directional) ? 100f : distance;

            // Note: Raycast towards the object. Check if it hits anything else first.
            if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, rayDist, occlusionLayers))
            {
                if (hit.transform != targetObject && !hit.transform.IsChildOf(targetObject))
                {
                    // Light is occluded by another object (in shadow)
                    continue; 
                }
            }

            // Calculate the dot product (Lambertian reflectance) for surface angle impact
            // Note: This requires a rough normal alignment. For a basic point estimation, we can default to 1.
            float angleImpact = 1.0f; 

            totalIntensity += light.intensity * attenuation * angleImpact;
        }
        
        return totalIntensity;
    }
}
