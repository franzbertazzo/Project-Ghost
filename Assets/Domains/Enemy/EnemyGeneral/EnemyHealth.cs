using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public int maxHealth = 100;
    private int currentHealth;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage, bool fromDash = false)
    {
        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Die(fromDash);
        }
    }

    public void Die(bool fromDash = false)
    {
        if (fromDash && HitStop.Instance != null)
        {
            HitStop.Instance.Trigger(0.06f, 0.05f);
        }

        Destroy(gameObject);
    }
}
