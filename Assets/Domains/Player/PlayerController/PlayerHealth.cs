using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public bool ExternalInvulnerable { get; set; }

    [Header("Health")]
    public int maxHealth = 100;
    public int currentHealth;

    [Header("Damage Feedback")]
    public float invulnerabilityTime = 0.15f;

    bool invulnerable;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (invulnerable || ExternalInvulnerable)
            return;

        currentHealth -= damage;
        currentHealth = Mathf.Max(currentHealth, 0);

        Debug.Log($"Player hit! Health: {currentHealth}");

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
