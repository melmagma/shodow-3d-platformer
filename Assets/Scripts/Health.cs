using UnityEngine;
using UnityEngine.Events;

public class Health : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LightDetector lightDetector;
    [SerializeField] private Transform target; // Object to measure light on (defaults to this object)

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Light Damage")]
    [Tooltip("Light intensity at or above this value hurts the player. Zero light never does.")]
    [SerializeField] private float intensityThreshold = 0.5f;
    [SerializeField] private float damagePerTick = 5f;
    [SerializeField] private float tickInterval = 1f;

    [Header("Events")]
    public UnityEvent onDeath;

    public float CurrentHealth { get; private set; }

    private float tickTimer;
    private bool isDead;

    private void Awake()
    {
        CurrentHealth = maxHealth;
        if (target == null) target = transform;
    }

    private void Update()
    {
        if (isDead || lightDetector == null) return;

        float intensity = lightDetector.GetDirectLightIntensityOnObject(target);

        // Zero light = safe. Otherwise only hurt if intensity reaches the threshold.
        bool inDamagingLight = intensity > 0f && intensity >= intensityThreshold;

        if (inDamagingLight)
        {
            tickTimer += Time.deltaTime;

            if (tickTimer >= tickInterval)
            {
                tickTimer -= tickInterval;
                TakeDamage(damagePerTick);
            }
        }
        else
        {
            tickTimer = 0f; // Reset so leaving the light restarts the countdown
        }
        Debug.Log(CurrentHealth);
    }

    public void TakeDamage(float amount)
    {
        if (isDead) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        Debug.Log($"[Health] {CurrentHealth}/{maxHealth}");

        if (CurrentHealth <= 0f) Die();
    }

    private void Die()
    {
        isDead = true;
        onDeath?.Invoke();
        Debug.Log("[Health] Player died.");
    }
}
