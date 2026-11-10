using UnityEngine;

/// <summary>
/// Answers "is this point on a surface lit?" by checking every light in the scene:
/// range, spot cone, whether the surface faces the light, and a shadow raycast.
///
/// Unlike LightDetector / LiteDetect (which sample an object's pivot), this samples a
/// surface point + its normal, so a wall facing away from a light correctly reads as dark.
/// </summary>
public static class LightSampler
{
    private const float RefreshInterval = 0.5f;       // how often the light list is re-scanned
    private const float SurfaceSkin = 0.05f;          // start rays slightly off the surface so they don't hit the wall itself
    private const float DirectionalRayLength = 500f;

    private static Light[] lights = new Light[0];
    private static float lastRefresh = float.NegativeInfinity;

    /// <summary>Force a re-scan (call after spawning/destroying lights if you can't wait 0.5s).</summary>
    public static void RefreshLights()
    {
        lights = Object.FindObjectsByType<Light>();
        lastRefresh = Time.unscaledTime;
    }

    public static bool IsLit(Vector3 point, Vector3 normal, int surfaceLayer, int occluders, float threshold)
    {
        return SampleIntensity(point, normal, surfaceLayer, occluders, threshold) >= threshold;
    }

    /// <summary>
    /// Total light reaching a surface point. Stops early once the total reaches stopAbove
    /// (so IsLit doesn't have to check every light).
    /// </summary>
    public static float SampleIntensity(Vector3 point, Vector3 normal, int surfaceLayer, int occluders,
                                        float stopAbove = float.PositiveInfinity)
    {
        // Time.unscaledTime < lastRefresh covers "Enter Play Mode without domain reload"
        if (Time.unscaledTime < lastRefresh || Time.unscaledTime - lastRefresh > RefreshInterval)
            RefreshLights();

        Vector3 origin = point + normal * SurfaceSkin;
        float total = 0f;

        foreach (Light light in lights)
        {
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy || light.intensity <= 0f)
                continue;

            // Lights can be set to ignore certain layers
            if ((light.cullingMask & (1 << surfaceLayer)) == 0) continue;

            Vector3 toLight;
            float distance;
            float attenuation = 1f;

            if (light.type == LightType.Directional)
            {
                toLight = -light.transform.forward;
                distance = DirectionalRayLength;
            }
            else if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                Vector3 delta = light.transform.position - origin;
                distance = delta.magnitude;
                if (distance > light.range || distance < 0.0001f) continue;
                toLight = delta / distance;

                attenuation = Mathf.Clamp01(1f - distance / light.range);
                attenuation *= attenuation;

                if (light.type == LightType.Spot)
                {
                    float angle = Vector3.Angle(light.transform.forward, -toLight);
                    float outerHalf = light.spotAngle * 0.5f;
                    if (angle > outerHalf) continue;

                    // Soft edge between the inner and outer cone
                    float innerHalf = light.innerSpotAngle * 0.5f;
                    if (angle > innerHalf)
                        attenuation *= Mathf.InverseLerp(outerHalf, innerHalf, angle);
                }
            }
            else
            {
                continue; // area lights only exist in baked lighting
            }

            // Surface facing away from this light = not lit by it
            float facing = Vector3.Dot(normal, toLight);
            if (facing <= 0.001f) continue;

            // Shadow check. Lights with shadows turned off light everything, same as they do visually.
            if (light.shadows != LightShadows.None &&
                Physics.Raycast(origin, toLight, distance, occluders, QueryTriggerInteraction.Ignore))
                continue;

            total += light.intensity * attenuation * facing;
            if (total >= stopAbove) break;
        }

        return total;
    }
}