using UnityEngine;
using System.Collections;

public class BattleshipController : MonoBehaviour
{
    [Header("Ship Health")]
    public int maxHealth = 500;

    [Header("Awareness")]
    public float awarenessRange = 80f;

    [Header("Shield")]
    public ForceFieldShield forceField;
    public ShieldGenerator shieldGenerator;
    public float shieldRegenDelay = 10f;
    public int shieldRegenHealth = 250;

    [Header("Containers")]
    public BattleshipContainer[] containers;

    [Header("Missile Launcher")]
    public MissileLauncher missileLauncher;

    [Header("Visual Feedback")]
    public DamageFlash damageFlash;

    [Header("Death")]
    public GameObject deathExplosionPrefab;
    public int surfaceExplosionCount = 8;
    public float explosionInterval = 0.25f;
    public float explosionSpreadRadius = 5f;
    public float deathForce = 10f;
    public float deathTorque = 3f;

    [Header("Cockpit")]
    public Transform cockpit;
    public GameObject cockpitSmokePrefab;

    [Header("Destroyed Visuals")]
    public Material deathMaterial;

    [Header("Warp Entry")]
    public bool isWarping = true;
    public float warpDistance = 500f;
    public float warpDuration = 0.4f;
    public float warpStretch = 5f;

    private int currentHealth;
    private float shieldRegenTimer;
    private bool shieldCanRegen = true;
    private BattleshipMovement movement;
    private Coroutine cockpitBlinkCoroutine;

    public bool IsDead => currentHealth <= 0;
    public bool IsAware => movement != null && movement.IsAware;

    void Start()
    {
        currentHealth = maxHealth;
        movement = GetComponent<BattleshipMovement>();

        if (isWarping)
        {
            if (movement != null)
            {
                movement.enabled = false;
            }
            StartCoroutine(WarpIn());
        }

        // Ensure root EnemyHealth is non-damageable (damage goes through weak points only)
        EnemyHealth rootHealth = GetComponent<EnemyHealth>();
        if (rootHealth != null)
        {
            rootHealth.isDamageable = false;
        }

        if (forceField != null)
        {
            forceField.onShieldDepleted += OnShieldDepleted;
        }

        if (shieldGenerator != null)
        {
            shieldGenerator.onGeneratorDestroyed += OnGeneratorDestroyed;
        }

        foreach (var container in containers)
        {
            if (container != null)
            {
                container.onContainerDestroyed += OnContainerDestroyed;
            }
        }
    }

    void Update()
    {
        if (IsDead)
        {
            return;
        }

        HandleShieldRegen();
    }

    void HandleShieldRegen()
    {
        if (!shieldCanRegen || forceField == null || forceField.IsActive)
        {
            return;
        }

        shieldRegenTimer -= Time.deltaTime;
        if (shieldRegenTimer <= 0f)
        {
            forceField.ReactivateShield(shieldRegenHealth);

            if (shieldGenerator != null)
            {
                shieldGenerator.SetVulnerable(false);
            }
        }
    }

    /// <summary>
    /// Called by WeakPoint when it takes a hit. This is the ONLY way
    /// the ship's core health can be reduced.
    /// </summary>
    public void ApplyDirectDamage(int damage)
    {
        if (IsDead)
        {
            return;
        }

        currentHealth -= damage;

        if (damageFlash != null)
        {
            damageFlash.Flash();
        }

        if (IsDead)
        {
            Die();
        }
    }

    void OnShieldDepleted()
    {
        shieldRegenTimer = shieldRegenDelay;

        if (shieldGenerator != null)
        {
            shieldGenerator.SetVulnerable(true);
        }
    }

    void OnGeneratorDestroyed()
    {
        shieldCanRegen = false;

        if (forceField != null && forceField.IsActive)
        {
            forceField.DeactivateShield();
        }
    }

    void OnContainerDestroyed()
    {
        // Refresh cached renderers so detached containers no longer flash
        if (damageFlash != null)
        {
            damageFlash.CacheRenderers();
        }
    }

    void Die()
    {
        // Stop missile firing
        if (missileLauncher != null)
        {
            missileLauncher.enabled = false;
        }

        // Disable all turrets
        EnemyShooterSimple[] turrets = GetComponentsInChildren<EnemyShooterSimple>();
        foreach (var turret in turrets)
        {
            turret.enabled = false;
        }

        // Disable movement
        BattleshipMovement movement = GetComponent<BattleshipMovement>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        StartCoroutine(DeathSequence());
    }

    IEnumerator DeathSequence()
    {
        // Surface explosions
        for (int i = 0; i < surfaceExplosionCount; i++)
        {
            if (deathExplosionPrefab != null)
            {
                Vector3 offset = Random.insideUnitSphere * explosionSpreadRadius;
                Vector3 spawnPos = transform.position + offset;
                var vfx = Instantiate(deathExplosionPrefab, spawnPos, Random.rotation);
                Destroy(vfx, 3f);
            }

            yield return new WaitForSeconds(explosionInterval);
        }

        // Final big explosion
        if (deathExplosionPrefab != null)
        {
            var finalVfx = Instantiate(deathExplosionPrefab, transform.position, Quaternion.identity);
            Destroy(finalVfx, 3f);
        }

        // Hide weak points
        WeakPoint[] weakPoints = GetComponentsInChildren<WeakPoint>();
        foreach (var wp in weakPoints)
        {
            MeshRenderer mr = wp.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.enabled = false;
            }
        }

        // Decouple remaining containers
        foreach (var container in containers)
        {
            if (container != null)
            {
                container.DestroyContainer();
            }
        }

        // Decouple shield
        if (forceField != null)
        {
            forceField.gameObject.SetActive(false);
        }

        if (shieldGenerator != null)
        {
            shieldGenerator.Decouple();
        }

        // Decouple cockpit
        if (cockpit != null)
        {
            cockpit.SetParent(null);

            Rigidbody cockpitRb = cockpit.GetComponent<Rigidbody>();
            if (cockpitRb == null)
            {
                cockpitRb = cockpit.gameObject.AddComponent<Rigidbody>();
            }
            cockpitRb.isKinematic = false;
            cockpitRb.useGravity = false;
            cockpitRb.AddForce(Random.insideUnitSphere * deathForce * 0.5f, ForceMode.Impulse);
            cockpitRb.AddTorque(Random.insideUnitSphere * deathTorque, ForceMode.Impulse);

            // Apply death material to cockpit before spawning smoke
            if (deathMaterial != null)
            {
                Renderer[] cockpitRenderers = cockpit.GetComponentsInChildren<Renderer>();
                foreach (var rend in cockpitRenderers)
                {
                    Material[] mats = rend.materials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        mats[i] = deathMaterial;
                    }
                    rend.materials = mats;
                }
            }

            if (cockpitSmokePrefab != null)
            {
                Instantiate(cockpitSmokePrefab, cockpit.position, Quaternion.identity, cockpit);
            }

            // Enable cockpit health so player can damage it
            EnemyHealth cockpitHealth = cockpit.GetComponent<EnemyHealth>();
            if (cockpitHealth != null)
            {
                cockpitHealth.isDamageable = true;
                cockpitHealth.onDamageTaken += OnCockpitDamaged;
            }

            // Blink and destroy after delay
            cockpitBlinkCoroutine = StartCoroutine(BlinkAndDestroy(cockpit.gameObject));
        }

        // Apply death material to ship body
        if (deathMaterial != null)
        {
            Renderer[] shipRenderers = GetComponentsInChildren<Renderer>();
            foreach (var rend in shipRenderers)
            {
                Material[] mats = rend.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = deathMaterial;
                }
                rend.materials = mats;
            }
        }

        // Change layer to DashSurface (6) so the ship becomes inert
        SetLayerRecursively(gameObject, 6);

        // Push the hull in a random direction
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(Random.insideUnitSphere * deathForce, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * deathTorque, ForceMode.Impulse);
        }
    }

    void OnCockpitDamaged(int damage)
    {
        if (cockpit == null)
        {
            return;
        }

        EnemyHealth cockpitHealth = cockpit.GetComponent<EnemyHealth>();
        if (cockpitHealth != null && cockpitHealth.IsDead)
        {
            if (cockpitBlinkCoroutine != null)
            {
                StopCoroutine(cockpitBlinkCoroutine);
            }

            if (deathExplosionPrefab != null)
            {
                var vfx = Instantiate(deathExplosionPrefab, cockpit.position, Quaternion.identity);
                Destroy(vfx, 3f);
            }

            Destroy(cockpit.gameObject);
        }
    }

    void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    IEnumerator WarpIn()
    {
        Vector3 targetPosition = transform.position;
        Vector3 originalScale = transform.localScale;
        Vector3 warpDirection = transform.forward;

        // Start far away
        transform.position = targetPosition - warpDirection * warpDistance;

        // Stretch along local Z
        transform.localScale = new Vector3(originalScale.x, originalScale.y, originalScale.z * warpStretch);

        float elapsed = 0f;

        while (elapsed < warpDuration)
        {
            float t = elapsed / warpDuration;
            float eased = t * t; // ease-in for a fast arrival

            transform.position = Vector3.Lerp(targetPosition - warpDirection * warpDistance, targetPosition, eased);

            float stretchT = 1f - eased;
            float currentStretch = Mathf.Lerp(1f, warpStretch, stretchT);
            transform.localScale = new Vector3(originalScale.x, originalScale.y, originalScale.z * currentStretch);

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.position = targetPosition;
        transform.localScale = originalScale;

        if (movement != null)
        {
            movement.enabled = true;
        }
    }

    IEnumerator BlinkAndDestroy(GameObject target)
    {
        yield return new WaitForSeconds(20f);

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        float elapsed = 0f;
        float blinkDuration = 1.5f;
        float blinkRate = 0.05f;

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

        Destroy(target);
    }
}
