using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

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
    [SerializeField] private float tickInterval = 0.2f;

    [Header("Regeneration")]
    [Tooltip("Health restored per second while out of damaging light.")]
    [SerializeField] private float regenPerSecond = 5f;
    [Tooltip("Seconds out of damage before regen starts. 0 = immediate.")]
    [SerializeField] private float regenDelay = 2f;

    private float timeSinceDamage;

    [Header("Events")]
    public UnityEvent onDeath;

    [Range(0f, 1f)]
    [SerializeField] private float lightSpeedMultiplier = 0.65f;

    public float CurrentHealth { get; private set; }

    private PlayerController player;
    private float tickTimer;
    private bool isDead;

    private void Awake()
    {
        CurrentHealth = maxHealth;
        if (target == null) target = transform;
        player = GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (isDead || lightDetector == null) return;

        float intensity = lightDetector.GetDirectLightIntensityOnObject(target); // Zero light = safe. Otherwise only hurt if intensity reaches the threshold.
        bool inDamagingLight = intensity > 0f && intensity >= intensityThreshold;

        // Slow while in light, restore the moment you leave
        if (player != null)
            player.speedMultiplier = inDamagingLight ? lightSpeedMultiplier : 1f;

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
            tickTimer = 0f;
            timeSinceDamage += Time.deltaTime;

            if (timeSinceDamage >= regenDelay && CurrentHealth < maxHealth)
            {
                CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + regenPerSecond * Time.deltaTime);
            }
        }
    Debug.Log(CurrentHealth);
    }

    public void TakeDamage(float amount)
    {
        if (isDead) return;

        timeSinceDamage = 0f;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        Debug.Log($"[Health] {CurrentHealth}/{maxHealth}");

        if (CurrentHealth <= 0f) Die();
    }

    private void Die()
    {
        isDead = true;
        onDeath?.Invoke();
        Debug.Log("[Health] Player died.");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
