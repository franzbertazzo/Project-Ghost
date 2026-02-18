using System;
using UnityEngine;

public struct NoiseData
{
    public Vector3 Position;
    public float Loudness;
    public GameObject Source;

    public NoiseData(Vector3 position, float loudness, GameObject source)
    {
        Position = position;
        Loudness = loudness;
        Source = source;
    }
}

public static class NoiseSystem
{
    /// <summary>
    /// Fired whenever something emits a noise. Enemies can subscribe to this.
    /// </summary>
    public static event Action<NoiseData> OnNoiseEmitted;

    public static void EmitNoise(Vector3 position, float loudness, GameObject source = null)
    {
        OnNoiseEmitted?.Invoke(new NoiseData(position, loudness, source));
    }
}
