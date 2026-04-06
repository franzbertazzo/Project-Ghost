using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DamageFlash : MonoBehaviour
{
    [Header("Flash Settings")]
    public Color flashColor = Color.red;
    public float flashDuration = 0.1f;
    public int flashCount = 3;

    [Header("Shader Properties")]
    [Tooltip("Color properties to override. Covers Standard (_Color) and URP/HDRP (_BaseColor).")]
    public string[] colorProperties = { "_Color", "_BaseColor" };

    [Header("Emission")]
    [Tooltip("Also set emission color during flash for maximum visibility.")]
    public bool flashEmission = true;
    public string[] emissionProperties = { "_EmissionColor" };
    public float emissionIntensity = 3f;

    private Renderer[] renderers;
    private Dictionary<Renderer, Color[]> originalColors;
    private Dictionary<Renderer, Color[]> originalEmissionColors;
    private Coroutine flashCoroutine;

    void Start()
    {
        CacheRenderers();
    }

    public void CacheRenderers()
    {
        var allRenderers = GetComponentsInChildren<Renderer>();
        bool filterWeakPoints = GetComponent<BattleshipController>() != null;
        var filtered = new System.Collections.Generic.List<Renderer>();
        foreach (var rend in allRenderers)
        {
            if (rend != null && (!filterWeakPoints || rend.GetComponentInParent<WeakPoint>() == null))
            {
                filtered.Add(rend);
            }
        }
        renderers = filtered.ToArray();
        originalColors = new Dictionary<Renderer, Color[]>();
        originalEmissionColors = new Dictionary<Renderer, Color[]>();

        foreach (var rend in renderers)
        {
            if (rend == null)
            {
                continue;
            }

            // Cache original colors for each color property
            Color[] colors = new Color[colorProperties.Length];
            for (int i = 0; i < colorProperties.Length; i++)
            {
                if (rend.material.HasProperty(colorProperties[i]))
                {
                    colors[i] = rend.material.GetColor(colorProperties[i]);
                }
            }
            originalColors[rend] = colors;

            // Cache original emission colors
            Color[] emissions = new Color[emissionProperties.Length];
            for (int i = 0; i < emissionProperties.Length; i++)
            {
                if (rend.material.HasProperty(emissionProperties[i]))
                {
                    emissions[i] = rend.material.GetColor(emissionProperties[i]);
                }
            }
            originalEmissionColors[rend] = emissions;
        }
    }

    public void Flash()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            ClearFlash();
        }

        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        for (int i = 0; i < flashCount; i++)
        {
            ApplyFlash();
            yield return new WaitForSeconds(flashDuration);
            ClearFlash();
            yield return new WaitForSeconds(flashDuration);
        }

        flashCoroutine = null;
    }

    void ApplyFlash()
    {
        foreach (var rend in renderers)
        {
            if (rend == null)
            {
                continue;
            }

            foreach (var prop in colorProperties)
            {
                if (rend.material.HasProperty(prop))
                {
                    rend.material.SetColor(prop, flashColor);
                }
            }

            if (flashEmission)
            {
                foreach (var prop in emissionProperties)
                {
                    if (rend.material.HasProperty(prop))
                    {
                        rend.material.EnableKeyword("_EMISSION");
                        rend.material.SetColor(prop, flashColor * emissionIntensity);
                    }
                }
            }
        }
    }

    void ClearFlash()
    {
        foreach (var rend in renderers)
        {
            if (rend == null)
            {
                continue;
            }

            if (originalColors.TryGetValue(rend, out Color[] colors))
            {
                for (int i = 0; i < colorProperties.Length; i++)
                {
                    if (rend.material.HasProperty(colorProperties[i]))
                    {
                        rend.material.SetColor(colorProperties[i], colors[i]);
                    }
                }
            }

            if (flashEmission && originalEmissionColors.TryGetValue(rend, out Color[] emissions))
            {
                for (int i = 0; i < emissionProperties.Length; i++)
                {
                    if (rend.material.HasProperty(emissionProperties[i]))
                    {
                        rend.material.SetColor(emissionProperties[i], emissions[i]);
                    }
                }
            }
        }
    }
}
