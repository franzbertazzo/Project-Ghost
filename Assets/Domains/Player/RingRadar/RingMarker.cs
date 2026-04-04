using UnityEngine;
using TMPro;

/// <summary>
/// Base class for ring radar markers. Each marker owns a LineRenderer ring
/// that circles the player and tilts toward its tracked target.
/// </summary>
public abstract class RingMarker : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] protected TextMeshPro label;

    [Header("Ring Visual")]
    [SerializeField] protected int ringSegments = 64;
    [SerializeField] protected float ringLineWidth = 0.02f;

    [Header("Indicator")]
    [Tooltip("Optional 3D model (e.g. arrow). Its material color and emission will match the ring.")]
    [SerializeField] private Renderer indicatorRenderer;

    [Header("Fade")]
    [SerializeField] private float fadeSpeed = 3f;

    private float currentAlpha = 1f;
    private bool fading;
    private LineRenderer ringLine;
    private float ringVisibility = 0f;

    // Cached ring params for repositioning during fade
    private Vector3 cachedAxis1;
    private Vector3 cachedAxis2;
    private float cachedRadius;
    private Color cachedColor;
    private float cachedAlpha;
    private float cachedEmission;

    public Transform TrackedTarget { get; private set; }
    public bool IsFading => fading;

    /// <summary>
    /// Sets ring visibility (0 = hidden, 1 = fully visible). Interpolated externally.
    /// </summary>
    public void SetRingVisibility(float visibility)
    {
        ringVisibility = Mathf.Clamp01(visibility);
        if (ringLine != null)
        {
            ringLine.enabled = ringVisibility > 0.001f;
        }
    }

    public virtual void Activate(Transform target)
    {
        TrackedTarget = target;
        fading = false;
        currentAlpha = 1f;
        gameObject.SetActive(true);
        EnsureRingLine();
        if (ringLine != null)
        {
            ringLine.enabled = ringVisibility > 0.001f;
        }
    }

    public virtual void UpdateMarker(Vector3 position, Quaternion rotation, Color color, float scale, float normalizedDistance, float alpha = 0.35f)
    {
        transform.position = position;
        transform.rotation = rotation;
        transform.localScale = Vector3.one * scale;

        if (label != null)
        {
            Color c = color;
            c.a = currentAlpha * alpha;
            label.color = c;
        }
    }

    /// <summary>
    /// Updates the ring circle around the player for this marker.
    /// axis1 points toward the target, axis2 is the perpendicular in-ring direction.
    /// </summary>
    public void UpdateRing(Vector3 center, Vector3 axis1, Vector3 axis2, float radius, Color color, float alpha = 0.35f, float emission = 2f)
    {
        cachedAxis1 = axis1;
        cachedAxis2 = axis2;
        cachedRadius = radius;
        cachedColor = color;
        cachedAlpha = alpha;
        cachedEmission = emission;

        if (ringLine == null)
        {
            return;
        }

        float step = 2f * Mathf.PI / ringSegments;
        for (int i = 0; i < ringSegments; i++)
        {
            float angle = step * i;
            Vector3 point = center + (Mathf.Cos(angle) * axis1 + Mathf.Sin(angle) * axis2) * radius;
            ringLine.SetPosition(i, point);
        }

        // HDR color: multiply by emission so bloom picks up values > 1
        Color hdr = color * emission;
        hdr.a = currentAlpha * alpha * ringVisibility;
        ringLine.startColor = hdr;
        ringLine.endColor = hdr;

        if (ringLine.material.HasProperty("_EmissionColor"))
        {
            ringLine.material.SetColor("_EmissionColor", hdr);
        }
        if (ringLine.material.HasProperty("_BaseColor"))
        {
            ringLine.material.SetColor("_BaseColor", hdr);
        }
        else if (ringLine.material.HasProperty("_Color"))
        {
            ringLine.material.SetColor("_Color", hdr);
        }

        // Sync indicator model color with ring
        UpdateIndicatorColor(hdr);
    }

    private void UpdateIndicatorColor(Color hdr)
    {
        if (indicatorRenderer == null)
        {
            return;
        }

        Material mat = indicatorRenderer.material;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", hdr);
        }
        else if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", hdr);
        }
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", hdr);
        }
    }

    /// <summary>
    /// Re-centers the ring on a new position using cached axes/radius/color.
    /// Use this to keep fading markers following the player.
    /// </summary>
    public void RefreshRingCenter(Vector3 newCenter)
    {
        UpdateRing(newCenter, cachedAxis1, cachedAxis2, cachedRadius, cachedColor, cachedAlpha, cachedEmission);
    }

    public void StartFadeOut()
    {
        fading = true;
    }

    public void CancelFade()
    {
        fading = false;
        currentAlpha = 1f;
    }

    /// <summary>
    /// Advances fade-out. Returns true when fully faded and deactivated.
    /// </summary>
    public bool UpdateFade(float deltaTime)
    {
        if (!fading)
        {
            return false;
        }

        currentAlpha -= fadeSpeed * deltaTime;
        if (currentAlpha <= 0f)
        {
            currentAlpha = 0f;
            Deactivate();
            return true;
        }

        if (label != null)
        {
            Color c = label.color;
            c.a = currentAlpha;
            label.color = c;
        }

        if (ringLine != null)
        {
            Color c = ringLine.startColor;
            c.a = currentAlpha;
            ringLine.startColor = c;
            ringLine.endColor = c;
        }

        return false;
    }

    public virtual void Deactivate()
    {
        TrackedTarget = null;
        fading = false;
        currentAlpha = 1f;
        if (ringLine != null)
        {
            ringLine.enabled = false;
        }
        gameObject.SetActive(false);
    }

    private void EnsureRingLine()
    {
        if (ringLine != null)
        {
            return;
        }

        GameObject go = new GameObject("Ring");
        go.transform.SetParent(transform);
        ringLine = go.AddComponent<LineRenderer>();
        ringLine.useWorldSpace = true;
        ringLine.loop = true;
        ringLine.positionCount = ringSegments;
        ringLine.startWidth = ringLineWidth;
        ringLine.endWidth = ringLineWidth;
        ringLine.material = CreateEmissiveMaterial();
        ringLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringLine.receiveShadows = false;
    }

    private Material CreateEmissiveMaterial()
    {
        // Try URP Unlit first, fall back to Standard, then Sprites/Default
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material mat = new Material(shader);

        // Enable transparency
        if (mat.HasProperty("_Surface"))
        {
            // URP Unlit: Surface = 1 (Transparent), Blend = 0 (Alpha)
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        else if (mat.HasProperty("_Mode"))
        {
            // Standard shader: Fade mode
            mat.SetFloat("_Mode", 2f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_ALPHABLEND_ON");
        }

        // Enable emission
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        return mat;
    }
}