using UnityEngine;
using System.Collections;

public class BattleshipContainer : MonoBehaviour
{
    [Header("Linked Weak Point")]
    public WeakPoint weakPoint;

    [Header("Debris Physics")]
    public float debrisForce = 5f;
    public float debrisTorque = 2f;

    [Header("Blink Effect")]
    public float blinkDuration = 1.5f;
    public float blinkRate = 0.05f;

    [Header("VFX")]
    public GameObject explosionPrefab;

    public System.Action onContainerDestroyed;

    private EnemyHealth health;
    private DamageFlash damageFlash;
    private bool isDestroyed;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        damageFlash = GetComponent<DamageFlash>();

        if (health != null)
        {
            health.onDamageTaken += OnDamaged;
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
            DestroyContainer();
        }
        else if (damageFlash != null)
        {
            damageFlash.Flash();
        }
    }

    public void DestroyContainer()
    {
        if (isDestroyed)
        {
            return;
        }

        isDestroyed = true;

        if (explosionPrefab != null)
        {
            var vfx = Instantiate(explosionPrefab, transform.position, transform.rotation);
            Destroy(vfx, 3f);
        }

        if (weakPoint != null)
        {
            weakPoint.Activate();
        }

        // Destroy turrets on this container
        EnemyShooterSimple[] turrets = GetComponentsInChildren<EnemyShooterSimple>();
        foreach (var turret in turrets)
        {
            turret.Die();
        }

        onContainerDestroyed?.Invoke();

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

        // Enable collider if it was disabled
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = false;
        }

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
