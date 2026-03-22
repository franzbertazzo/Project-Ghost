using UnityEngine;

public class HairTargetAdvanced : MonoBehaviour
{
    public Transform character;
    public Rigidbody characterRigidbody; // Optional but recommended

    [Header("Base Follow")]
    public Vector3 localOffset = new Vector3(0, 1, -1);

    [Header("Per-Axis Lag")]
    public float lagX = 6f;
    public float lagY = 6f;
    public float lagZ = 2f; // slower = more trailing behind

    [Header("Velocity Influence")]
    public float velocityInfluence = 0.2f;
    public float maxVelocityOffset = 1.5f;

    [Header("Noise (Perlin)")]
    public float noiseAmplitude = 0.1f;
    public float noiseSpeed = 0.5f;

    [Header("Rotation")]
    public float rotationLagSpeed = 6f;

    private Vector3 currentVelocity;
    private Vector3 currentPosition;

    void Start()
    {
        currentPosition = transform.position; 
    }

    void Update()
    {
        if (character == null) return;

        // 1. Base position (local offset that follows rotation)
        Vector3 baseTarget = character.TransformPoint(localOffset);

        // 2. Velocity-based offset (trails opposite movement)
        Vector3 velocityOffset = Vector3.zero;

        if (characterRigidbody != null)
        {
            Vector3 vel = characterRigidbody.linearVelocity;
            velocityOffset = -vel * velocityInfluence;
            velocityOffset = Vector3.ClampMagnitude(velocityOffset, maxVelocityOffset);
        }

        // 3. Perlin noise (more natural than sine)
        float time = Time.time * noiseSpeed;

        Vector3 noiseOffset = new Vector3(
            Mathf.PerlinNoise(time, 1.23f) - 0.5f,
            Mathf.PerlinNoise(4.56f, time) - 0.5f,
            0f
        ) * noiseAmplitude;

        // 4. Final desired position
        Vector3 desired = baseTarget + velocityOffset + noiseOffset;

        // 5. Per-axis lag (independent smoothing)
        currentPosition.x = Mathf.Lerp(currentPosition.x, desired.x, lagX * Time.deltaTime);
        currentPosition.y = Mathf.Lerp(currentPosition.y, desired.y, lagY * Time.deltaTime);
        currentPosition.z = Mathf.Lerp(currentPosition.z, desired.z, lagZ * Time.deltaTime);

        transform.position = currentPosition;

        // 6. Smooth rotation
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            character.rotation,
            rotationLagSpeed * Time.deltaTime
        );
    }
}