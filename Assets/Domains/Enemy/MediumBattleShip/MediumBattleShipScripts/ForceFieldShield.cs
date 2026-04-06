using UnityEngine;

public class ForceFieldShield : MonoBehaviour
{
    [Header("Shield Colors")]
    public Color fullHealthColor = new Color(0.2f, 0.4f, 1f, 0.4f);
    public Color midHealthColor = new Color(1f, 0.9f, 0.2f, 0.4f);
    public Color lowHealthColor = new Color(1f, 0.2f, 0.1f, 0.4f);

    [Header("Color Transition")]
    public float colorTransitionSpeed = 3f;

    [Header("Hit Flash")]
    [Tooltip("How long the shield stays fully visible after being hit.")]
    public float hitFlashDuration = 0.15f;
    [Tooltip("How fast the shield fades back to invisible after the flash.")]
    public float hitFadeSpeed = 4f;

    [Header("References")]
    public Renderer shieldRenderer;

    [Header("VFX")]
    public GameObject shieldHitEffectPrefab;

    private EnemyHealth health;
    private Collider shieldCollider;
    private Material shieldMaterial;
    private Color targetColor;
    private bool isActive = true;
    private float hitFlashTimer;
    private float currentAlpha;

    public bool IsActive => isActive;
    public System.Action onShieldDepleted;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        shieldCollider = GetComponent<Collider>();

        if (health != null)
        {
            health.onDamageTaken += OnShieldDamaged;
        }

        if (shieldRenderer != null)
        {
            shieldMaterial = shieldRenderer.material;
            targetColor = fullHealthColor;
            // Start invisible
            currentAlpha = 0f;
            SetShieldAlpha(0f);
        }
    }

    void Update()
    {
        if (!isActive || shieldMaterial == null)
        {
            return;
        }

        targetColor = CalculateTargetColor();
        Color baseColor = Color.Lerp(shieldMaterial.color, targetColor, Time.deltaTime * colorTransitionSpeed);

        // Fade alpha: hold during flash, then fade out
        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= Time.deltaTime;
            currentAlpha = targetColor.a;
        }
        else
        {
            currentAlpha = Mathf.MoveTowards(currentAlpha, 0f, hitFadeSpeed * Time.deltaTime);
        }

        baseColor.a = currentAlpha;
        shieldMaterial.color = baseColor;

        if (shieldMaterial.HasProperty("_EmissionColor"))
        {
            Color emission = baseColor * 2f;
            emission.a = currentAlpha;
            shieldMaterial.SetColor("_EmissionColor", emission);
        }
    }

    Color CalculateTargetColor()
    {
        float healthPercent = (float)health.CurrentHealth / health.maxHealth;
        healthPercent = Mathf.Clamp01(healthPercent);

        if (healthPercent > 0.5f)
        {
            float t = (healthPercent - 0.5f) / 0.5f;
            return Color.Lerp(midHealthColor, fullHealthColor, t);
        }
        else
        {
            float t = healthPercent / 0.5f;
            return Color.Lerp(lowHealthColor, midHealthColor, t);
        }
    }

    void OnShieldDamaged(int damage)
    {
        if (!isActive)
        {
            return;
        }

        SpawnHitEffect();
        ShowHitFlash();

        if (health.IsDead)
        {
            DeactivateShield();
        }
    }

    void ShowHitFlash()
    {
        hitFlashTimer = hitFlashDuration;
        currentAlpha = targetColor.a;
    }

    void SetShieldAlpha(float alpha)
    {
        if (shieldMaterial == null)
        {
            return;
        }

        Color c = shieldMaterial.color;
        c.a = alpha;
        shieldMaterial.color = c;

        if (shieldMaterial.HasProperty("_EmissionColor"))
        {
            Color e = shieldMaterial.GetColor("_EmissionColor");
            e.a = alpha;
            shieldMaterial.SetColor("_EmissionColor", e);
        }
    }

    void SpawnHitEffect()
    {
        if (shieldHitEffectPrefab != null)
        {
            var vfx = Instantiate(shieldHitEffectPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, 2f);
        }
    }

    public void DeactivateShield()
    {
        isActive = false;

        if (shieldCollider != null)
        {
            shieldCollider.enabled = false;
        }

        if (shieldRenderer != null)
        {
            shieldRenderer.enabled = false;
        }

        onShieldDepleted?.Invoke();
    }

    public void ReactivateShield(int healthAmount)
    {
        if (health == null)
        {
            return;
        }

        health.SetHealth(healthAmount);
        isActive = true;

        if (shieldCollider != null)
        {
            shieldCollider.enabled = true;
        }

        if (shieldRenderer != null)
        {
            shieldRenderer.enabled = true;
        }

        if (shieldMaterial != null)
        {
            currentAlpha = 0f;
            SetShieldAlpha(0f);
        }
    }

    void OnDestroy()
    {
        if (shieldMaterial != null)
        {
            Destroy(shieldMaterial);
        }
    }
}
