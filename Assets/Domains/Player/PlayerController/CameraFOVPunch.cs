using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraFOVPunch : MonoBehaviour
{
    [Header("References")]
    public Rigidbody playerRb;

    [Header("Base FOV")]
    public float baseFOV = 72f;
    public float fovLerpSpeed = 10f;

    [Header("Speed FOV")]
    public float speedFOVMax = 10f;
    public float speedForMaxFOV = 15f;

    [Header("Dash FOV")]
    public float dashFOVBoost = 22f;
    public float dashFOVDuration = 0.15f;

    Camera cam;

    float dashTimer;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.fieldOfView = baseFOV;
    }

    void Update()
    {
        if (playerRb == null)
            return;

        // ----------------------------
        // 1️⃣ SPEED-BASED FOV
        // ----------------------------
        float speed = playerRb.linearVelocity.magnitude;
        float speed01 = Mathf.Clamp01(speed / speedForMaxFOV);
        float speedFOV = speed01 * speedFOVMax;

        // ----------------------------
        // 2️⃣ DASH FOV (TEMPORARY)
        // ----------------------------
        float dashFOV = dashTimer > 0f ? dashFOVBoost : 0f;

        // ----------------------------
        // 3️⃣ FINAL TARGET FOV
        // ----------------------------
        float targetFOV = baseFOV + speedFOV + dashFOV;

        cam.fieldOfView = Mathf.Lerp(
            cam.fieldOfView,
            targetFOV,
            fovLerpSpeed * Time.deltaTime
        );

        // ----------------------------
        // 4️⃣ DASH TIMER
        // ----------------------------
        if (dashTimer > 0f)
            dashTimer -= Time.deltaTime;
    }

    // --------------------------------------------------
    // PUBLIC API — CALLED BY DASH
    // --------------------------------------------------
    public void TriggerDashFOV()
    {
        dashTimer = dashFOVDuration;
    }
}
