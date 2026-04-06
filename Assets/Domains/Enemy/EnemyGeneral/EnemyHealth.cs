using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public bool isRadarDetectable = true;
    public bool isDamageable = true;
    private int currentHealth;

    public bool IsDead => currentHealth <= 0;
    public int CurrentHealth => currentHealth;
    public System.Action<int> onDamageTaken;
    public System.Action onDeath;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (!isDamageable)
        {
            return;
        }

        if (IsDead)
        {
            return;
        }

        currentHealth -= damage;
        onDamageTaken?.Invoke(damage);

        if (IsDead)
        {
            onDeath?.Invoke();
        }
    }

    public void SetHealth(int health)
    {
        currentHealth = health;
    }
}
