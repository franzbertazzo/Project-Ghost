using UnityEngine;

/// <summary>
/// Marker for an opposing player on the ring radar.
///
/// Labelled with that player's name rather than "ENEMY". With four players in an arena, knowing
/// something is behind you is half the information: which of them it is decides whether you turn
/// and fight or keep running, and the colour alone cannot carry that once a ring is small or
/// partly occluded.
/// </summary>
public class EnemyMarker : RingMarker
{
    private const string FALLBACK_TEXT = "ENEMY";

    public override void Activate(Transform target)
    {
        base.Activate(target);

        // Named by the radar as soon as it resolves the owner. Until then this reads as the old
        // generic label, which is the right thing to show for a contact we cannot yet name.
        SetLabel(FALLBACK_TEXT);
    }

    public void SetLabel(string text)
    {
        if (label != null && label.text != text)
        {
            label.text = text;
        }
    }
}
