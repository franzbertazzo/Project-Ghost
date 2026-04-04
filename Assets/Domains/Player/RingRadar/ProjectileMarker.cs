using UnityEngine;

/// <summary>
/// Marker for projectiles on the ring radar. Displays "DANGER" with a pulsing effect.
/// Renders with visual priority over enemy markers.
/// </summary>
public class ProjectileMarker : RingMarker
{
    private const string LABEL_TEXT = "DANGER";

    [Header("Pulse")]
    [SerializeField] private float pulseSpeed = 8f;
    [SerializeField] private float pulseMinAlpha = 0.3f;

    public override void Activate(Transform target)
    {
        base.Activate(target);
        if (label != null)
        {
            label.text = LABEL_TEXT;
            label.fontSize = 3f;
            label.sortingOrder = 1;
        }
    }

    public override void UpdateMarker(Vector3 position, Quaternion rotation, Color color, float scale, float normalizedDistance, float alpha = 0.35f)
    {
        // Offset slightly toward camera so projectile markers render on top of enemies
        Vector3 camForward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
        position -= camForward * 0.01f;

        base.UpdateMarker(position, rotation, color, scale, normalizedDistance, alpha);

        // Pulse / flicker effect
        if (label != null && !IsFading)
        {
            float pulse = Mathf.Lerp(pulseMinAlpha, 1f, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            Color c = label.color;
            c.a *= pulse;
            label.color = c;
        }
    }
}
