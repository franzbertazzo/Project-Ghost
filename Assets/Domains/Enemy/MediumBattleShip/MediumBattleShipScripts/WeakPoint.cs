using UnityEngine;

public class WeakPoint : MonoBehaviour
{
    [Header("References")]
    public BattleshipController battleship;

    [Header("VFX")]
    public GameObject hitEffectPrefab;

    private EnemyHealth health;
    private Collider weakPointCollider;
    private DamageFlash damageFlash;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        weakPointCollider = GetComponent<Collider>();
        damageFlash = GetComponent<DamageFlash>();

        // Weak point starts disabled until its container is destroyed
        if (weakPointCollider != null)
        {
            weakPointCollider.enabled = false;
        }

        if (health != null)
        {
            health.onDamageTaken += OnWeakPointHit;
        }
    }

    public void Activate()
    {
        if (weakPointCollider != null)
        {
            weakPointCollider.enabled = true;
        }
    }

    void OnWeakPointHit(int damage)
    {
        if (battleship != null)
        {
            battleship.ApplyDirectDamage(damage);
        }

        if (damageFlash != null)
        {
            damageFlash.Flash();
        }

        if (hitEffectPrefab != null)
        {
            var vfx = Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, 2f);
        }

        // Reset health so the weak point persists and can be hit repeatedly
        if (health != null)
        {
            health.SetHealth(health.maxHealth);
        }
    }
}
