using UnityEngine;

/// <summary>
/// Marker for enemies on the ring radar. Displays "ENEMY" with distance-based coloring.
/// </summary>
public class EnemyMarker : RingMarker
{
    private const string LABEL_TEXT = "ENEMY";

    public override void Activate(Transform target)
    {
        base.Activate(target);
        if (label != null)
        {
            label.text = LABEL_TEXT;
        }
    }
}
