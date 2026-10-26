using UnityEngine;

public class LiteDetect : MonoBehaviour
{
    [Header("Detection Settings")]
    [SerializeField] private Transform targetObject;     
    [SerializeField] private LayerMask occlusionLayers;   

    [Header("Debug Options")]
    [SerializeField] private bool logToConsole = true;    
    [SerializeField] private bool drawDebugLines = true;   // Visualizes the light rays in the Scene view

    private void Update()
    {
        if (targetObject != null)
        {
            float intensity = GetDirectLightIntensityOnObject(targetObject);
            
            if (logToConsole)
            {
                Debug.Log($"[Light Detector] {targetObject.name} current light intensity: {intensity:F2}");
            }
        }
    }

    public float GetDirectLightIntensityOnObject(Transform target)
    {
        float totalIntensity = 0f;
        
        // Pivot offset: Start the ray slightly above the pivot point 
        // to prevent the ray from scraping the floor or getting stuck inside itself.
        Vector3 rayStart = target.position + (Vector3.up * 0.5f);

        // Find all active lights in the scene
        Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);

        foreach (Light light in allLights)
        {
            if (!light.enabled || light.intensity <= 0) continue;

            Vector3 rayDir;
            float rayDist;
            float attenuation = 1f;

            // Calculate direction and distance from target TO the light source
            if (light.type == LightType.Directional)
            {
                rayDir = -light.transform.forward;
                rayDist = 200f; // Directional lights are technically infinitely far away; use a large buffer
            }
            else if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                Vector3 toLight = light.transform.position - rayStart;
                rayDist = toLight.magnitude;
                rayDir = toLight.normalized;

                if (rayDist > light.range) continue; // Out of range

                // Falloff calculation
                attenuation = Mathf.Clamp01(1.0f - (rayDist / light.range));
                attenuation *= attenuation; 

                if (light.type == LightType.Spot)
                {
                    float angle = Vector3.Angle(-light.transform.forward, -rayDir);
                    if (angle > light.spotAngle * 0.5f) continue; 
                    
                    float outerRad = light.spotAngle * 0.5f * Mathf.Deg2Rad;
                    float innerRad = Mathf.Deg2Rad * (light.spotAngle * 0.5f * (1f - light.innerSpotAngle / 100f)); 
                    float cosAngle = Vector3.Dot(-light.transform.forward, -rayDir);
                    attenuation *= Mathf.Clamp01((cosAngle - Mathf.Cos(outerRad)) / (Mathf.Cos(innerRad) - Mathf.Cos(outerRad)));
                }
            }
            else continue; 

            // --- THE SHADOW CHECK ---
            bool isVisible = true;

            // Firing the ray from our target out towards the light source
            if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, rayDist, occlusionLayers))
            {
                // If it hits a cube (or anything else) before reaching the light distance, it's a shadow!
                if (hit.transform != target)
                {
                    isVisible = false; 
                }
            }

            // Visual Scene Debugging
            if (drawDebugLines)
            {
                // Green line means light is hitting the target. Red means it's blocked by a shadow.
                Color lineColour = isVisible ? Color.green : Color.red;
                Debug.DrawRay(rayStart, rayDir * (light.type == LightType.Directional ? 15f : rayDist), lineColour);
            }

            if (isVisible)
            {
                totalIntensity += light.intensity * attenuation;
            }
        }

        return totalIntensity;
    }
}
