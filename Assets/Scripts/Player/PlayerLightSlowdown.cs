using UnityEngine;

/// <summary>Slows the player down in bright light, easing in and out.
/// Optional: remove this component to turn the slowdown off.</summary>
public class PlayerLightSlowdown : MonoBehaviour
{
    [SerializeField] private LightDetector lightDetector;
    [Tooltip("Light intensity at or below this means no slowdown.")]
    public float minLightIntensity = 0.5f;
    [Tooltip("Light intensity at or above this means maximum slowdown.")]
    public float maxLightIntensity = 5f;
    [Tooltip("Speed multiplier when standing in the brightest light.")]
    [Range(0f, 1f)] public float minSpeedMultiplier = 0.4f;
    [Tooltip("How quickly the multiplier changes, per second.")]
    public float multiplierChangeRate = 3f;

    /// <summary>What PlayerMovement multiplies move speed by. 1 = no slowdown.</summary>
    public float SpeedMultiplier { get; private set; } = 1f;

    /// <summary>One physics step (PlayerController calls it).</summary>
    public void Step(float dt)
    {
        float intensity = lightDetector != null ? lightDetector.CurrentIntensity : 0f;

        // 0 at minLightIntensity, 1 at maxLightIntensity
        float t = Mathf.InverseLerp(minLightIntensity, maxLightIntensity, intensity);

        // Bright light -> lower multiplier
        float target = Mathf.Lerp(1f, minSpeedMultiplier, t);
        SpeedMultiplier = Mathf.MoveTowards(SpeedMultiplier, target, multiplierChangeRate * dt);
    }
}
