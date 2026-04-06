using UnityEngine;
using System.Collections;

public class ShieldGenerator : MonoBehaviour
{
    [Header("VFX")]
    public GameObject explosionPrefab;
    public GameObject vulnerableIndicator;

    [Header("Debris Physics")]
    public float debrisForce = 5f;
    public float debrisTorque = 2f;

    [Header("Blink Effect")]
    public float blinkDuration = 1.5f;
    public float blinkRate = 0.05f;

    public System.Action onGeneratorDestroyed;

    private EnemyHealth health;
    private Collider generatorCollider;
    private DamageFlash damageFlash;
    private bool isDestroyed;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        generatorCollider = GetComponent<Collider>();
        damageFlash = GetComponent<DamageFlash>();

        SetVulnerable(false);

        if (health != null)
        {
            health.onDamageTaken += OnDamaged;
        }
    }

    public void SetVulnerable(bool vulnerable)
    {
        if (isDestroyed)
        {
            return;
        }

        if (generatorCollider != null)
        {
            generatorCollider.enabled = vulnerable;
        }

        if (vulnerableIndicator != null)
        {
            vulnerableIndicator.SetActive(vulnerable);
        }
    }

    void OnDamaged(int damage)
    {
        if (isDestroyed)
        {
            return;
        }

        if (health != null && health.IsDead)
        {
            DestroyGenerator();
        }
        else if (damageFlash != null)
        {
            damageFlash.Flash();
        }
    }

    void DestroyGenerator()
    {
        isDestroyed = true;

        if (explosionPrefab != null)
        {
            var vfx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, 3f);
        }

        onGeneratorDestroyed?.Invoke();

        Decouple();
    }

    public void Decouple()
    {
        if (generatorCollider != null)
        {
            generatorCollider.enabled = false;
        }

        // Detach from ship hierarchy
        transform.SetParent(null);

        // Enable/add Rigidbody for physics debris
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.AddForce(Random.insideUnitSphere * debrisForce, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * debrisTorque, ForceMode.Impulse);

        // Blink and disappear after delay
        StartCoroutine(BlinkAndDestroy());
    }

    IEnumerator BlinkAndDestroy()
    {
        yield return new WaitForSeconds(20f);

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        float elapsed = 0f;

        while (elapsed < blinkDuration)
        {
            foreach (var rend in renderers)
            {
                if (rend != null)
                {
                    rend.enabled = !rend.enabled;
                }
            }
            yield return new WaitForSeconds(blinkRate);
            elapsed += blinkRate;
        }

        foreach (var rend in renderers)
        {
            if (rend != null)
            {
                rend.enabled = false;
            }
        }

        Destroy(gameObject);
    }
}
