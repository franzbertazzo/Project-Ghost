using UnityEngine;

public class CameraDirectionalLag : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Rigidbody playerRb;

    [Header("Lag Settings")]
    public float lagStrength = 0.15f;
    public float lagMaxDistance = 0.6f;
    public float catchUpSpeed = 10f;

    [Header("Dash Boost")]
    public float dashLagMultiplier = 2.0f;

    Vector3 currentOffset;
    float dashLagTimer;

    void LateUpdate()
    {
        if (playerRb == null || player == null)
            return;

        // Player velocity in local camera space
        Vector3 localVelocity =
            transform.InverseTransformDirection(playerRb.linearVelocity);

        // Only care about lateral + forward motion
        Vector3 desiredOffset = new Vector3(
            -localVelocity.x,
            -localVelocity.y,
            -localVelocity.z
        ) * lagStrength;

        // Clamp for comfort
        desiredOffset = Vector3.ClampMagnitude(desiredOffset, lagMaxDistance);

        // Dash exaggeration
        if (dashLagTimer > 0f)
        {
            desiredOffset *= dashLagMultiplier;
            dashLagTimer -= Time.deltaTime;
        }

        // Smooth catch-up
        currentOffset = Vector3.Lerp(
            currentOffset,
            desiredOffset,
            Time.deltaTime * catchUpSpeed
        );

        // Apply offset
        transform.localPosition = currentOffset;
    }

    // ---------------------------------------
    // CALLED BY DASH
    // ---------------------------------------
    public void TriggerDashLag(float duration)
    {
        dashLagTimer = duration;
    }
}
