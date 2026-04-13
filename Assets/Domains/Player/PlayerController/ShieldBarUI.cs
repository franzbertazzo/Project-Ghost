using UnityEngine;
using UnityEngine.UI;

public class ShieldBarUI : MonoBehaviour
{
    [Header("References")]
    public PlayerHealth playerHealth;
    public Image shieldFillImage;

    [Header("Shield Colors")]
    public Color fullShieldColor = new Color(0.2f, 0.4f, 1f, 1f);
    public Color midShieldColor = new Color(1f, 0.9f, 0.2f, 1f);
    public Color lowShieldColor = new Color(1f, 0.2f, 0.1f, 1f);

    [Header("Emission")]
    [Tooltip("Multiplier applied to the color for the emission channel.")]
    public float emissionIntensity = 2f;
    public float colorTransitionSpeed = 5f;

    Material fillMaterial;
    Color currentColor;

    void Start()
    {
        if (shieldFillImage != null)
        {
            // Instance the material so we don't modify the shared asset
            fillMaterial = shieldFillImage.material = new Material(shieldFillImage.material);
            currentColor = fullShieldColor;
        }
    }

    void Update()
    {
        if (playerHealth == null || shieldFillImage == null)
        {
            return;
        }

        float shieldPercent = playerHealth.currentShield / playerHealth.maxShield;
        shieldPercent = Mathf.Clamp01(shieldPercent);

        // Fill amount
        shieldFillImage.fillAmount = shieldPercent;

        // Target color based on shield level
        Color targetColor = GetColorForPercent(shieldPercent);
        currentColor = Color.Lerp(currentColor, targetColor, Time.deltaTime * colorTransitionSpeed);

        shieldFillImage.color = currentColor;

        // Emission on the instanced material
        if (fillMaterial != null && fillMaterial.HasProperty("_EmissionColor"))
        {
            Color emission = currentColor * emissionIntensity;
            fillMaterial.SetColor("_EmissionColor", emission);
            fillMaterial.EnableKeyword("_EMISSION");
        }
    }

    Color GetColorForPercent(float percent)
    {
        if (percent > 0.5f)
        {
            float t = (percent - 0.5f) / 0.5f;
            return Color.Lerp(midShieldColor, fullShieldColor, t);
        }
        else
        {
            float t = percent / 0.5f;
            return Color.Lerp(lowShieldColor, midShieldColor, t);
        }
    }
}
