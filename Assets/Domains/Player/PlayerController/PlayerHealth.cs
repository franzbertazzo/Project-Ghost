using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public bool ExternalInvulnerable { get; set; }

    [Header("Visibility")]
    [Tooltip("When false, all enemies are unable to detect or be aware of the player.")]
    public bool isVisible = true;

    [Header("Health")]
    public int maxHealth = 100;
    public int currentHealth;

    [Header("Shield")]
    public float maxShield = 100f;
    public float currentShield;
    [Tooltip("Seconds after last hit before shield starts recharging.")]
    public float shieldRechargeDelay = 3f;
    [Tooltip("Shield points recharged per second once recharging begins.")]
    public float shieldRechargeRate = 33f;

    [Header("Damage Feedback")]
    public float invulnerabilityTime = 0.15f;

    bool invulnerable;
    float timeSinceLastHit;

    void Awake()
    {
        currentHealth = maxHealth;
        currentShield = maxShield;
    }

    void Update()
    {
        timeSinceLastHit += Time.deltaTime;

        if (timeSinceLastHit >= shieldRechargeDelay && currentShield < maxShield)
        {
            currentShield += shieldRechargeRate * Time.deltaTime;
            currentShield = Mathf.Min(currentShield, maxShield);
        }
    }

    public void TakeDamage(int damage)
    {
        if (invulnerable || ExternalInvulnerable)
        {
            return;
        }

        timeSinceLastHit = 0f;

        int remaining = damage;

        if (currentShield > 0f)
        {
            float absorbed = Mathf.Min(currentShield, remaining);
            currentShield -= absorbed;
            remaining -= (int)absorbed;
        }

        if (remaining > 0)
        {
            currentHealth -= remaining;
            currentHealth = Mathf.Max(currentHealth, 0);
        }

        Debug.Log($"Player hit! Shield: {currentShield:F0} | Health: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            StartCoroutine(InvulnerabilityWindow());
        }
    }

    void Die()
    {
        Debug.Log("PLAYER DEAD");

        // TEMP behavior (safe for now)
        gameObject.SetActive(false);

        // Later:
        // - respawn
        // - slow motion
        // - explosion
        // - restart loop
    }

    System.Collections.IEnumerator InvulnerabilityWindow()
    {
        invulnerable = true;
        yield return new WaitForSeconds(invulnerabilityTime);
        invulnerable = false;
    }
}
