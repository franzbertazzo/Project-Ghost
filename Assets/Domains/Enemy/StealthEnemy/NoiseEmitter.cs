using UnityEngine;

public class NoiseEmitter : MonoBehaviour
{
    [Tooltip("Base loudness radius in meters for this noise source.")]
    public float baseLoudness = 10f;

    /// <summary>
    /// Call this when you do something noisy.
    /// Example: EmitNoise(1f) for walking, 3f for running, 5f for gunshots.
    /// </summary>
    public void EmitNoise(float loudnessMultiplier = 1f)
    {
        float loudness = baseLoudness * loudnessMultiplier;
        NoiseSystem.EmitNoise(transform.position, loudness, gameObject);
    }
}
