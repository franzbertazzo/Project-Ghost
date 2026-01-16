using UnityEngine;

public class CameraSpeedPullback : MonoBehaviour
{
    public Rigidbody playerRb;

    public float baseDistance = -4.8f;
    public float maxExtraDistance = -1.2f;
    public float speedForMax = 10f;
    public float lerpSpeed = 5f;

    float currentZ;

    void Start()
    {
        currentZ = baseDistance;
    }

    void LateUpdate()
    {
        if (!playerRb) return;

        float speed = playerRb.linearVelocity.magnitude;
        float t = Mathf.Clamp01(speed / speedForMax);

        float targetZ = baseDistance + maxExtraDistance * t;

        currentZ = Mathf.Lerp(
            currentZ,
            targetZ,
            lerpSpeed * Time.deltaTime
        );

        Vector3 pos = transform.localPosition;
        pos.z = currentZ;
        transform.localPosition = pos;
    }
}
