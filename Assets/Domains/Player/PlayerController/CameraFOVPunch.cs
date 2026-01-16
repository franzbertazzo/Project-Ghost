using UnityEngine;

public class CameraFOVPunch : MonoBehaviour
{
    public Rigidbody playerRb;
    public Camera cam;

    public float baseFov = 70f;
    public float maxFov = 90f;
    public float speedForMax = 12f;
    public float lerpSpeed = 6f;

    void LateUpdate()
    {
        if (!playerRb || !cam) return;

        float speed = playerRb.linearVelocity.magnitude;
        float t = Mathf.Clamp01(speed / speedForMax);

        float targetFov = Mathf.Lerp(baseFov, maxFov, t);

        cam.fieldOfView = Mathf.Lerp(
            cam.fieldOfView,
            targetFov,
            lerpSpeed * Time.deltaTime
        );
    }
}
