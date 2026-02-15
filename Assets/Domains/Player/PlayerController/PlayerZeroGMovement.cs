using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerZeroGMovement : MonoBehaviour
{
    [SerializeField] private CameraFOVPunch cameraFOVPunch; // Optional reference for dash FOV effect

    [Header("References")]
    public PlayerInputHandler inputHandler;
    public Transform cameraRig; // Assign CameraPivot or CameraRig transform

    [Header("Arcade Control")]
    public float acceleration = 25f;      // how fast you gain speed
    public float directionalBrake = 18f;  // how fast you cancel unwanted velocity
    public float maxSpeed = 12f;           // higher than before
    public float inputResponsiveness = 1.5f; // exaggerates input

    [Header("Movement Settings")]
    public float thrustPower = 10f;
    public float brakeForce = 5f;

    [Header("Precision / Brake Mode (Shift)")]
    public float precisionSpeedMultiplier = 0.4f;   // 40% speed
    public float precisionBrakeMultiplier = 2.5f;   // stronger braking
    public float precisionAccelerationMultiplier = 0.6f;

    [Header("Surface Dash")]
    public float dashImpulse = 22f;
    public float surfaceCheckRadius = 1.2f;
    public float surfaceCheckDistance = 1.5f;
    public float minDashSpeed = 10f;
    public LayerMask surfaceMask;

    [Header("Dash Control")]
    public float dashGraceTime = 0.15f;

    [Header("Dash Strength")]
    public float surfaceDashSpeed = 18f;
    public float airDashSpeed = 10f;

    [Header("Dash Charges")]
    public int maxDashCharges = 3;
    public float dashRechargeTime = 1.4f;

    private int currentDashCharges;
    private float dashRechargeTimer;

    float dashGraceTimer;

    [Header("Rotation Settings")]
    public float alignmentSpeed = 10f; // How fast player aligns to camera

    [Header("Noise Settings")]
    public float noiseIntensity = 1f;

    private Rigidbody rb;
    private NoiseEmitter noiseEmitter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 0.2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        currentDashCharges = maxDashCharges;

        if (inputHandler == null)
            inputHandler = GetComponent<PlayerInputHandler>();

        noiseEmitter = GetComponent<NoiseEmitter>();
    }

    void FixedUpdate()
    {
        if (dashGraceTimer > 0f)
        {
            dashGraceTimer -= Time.fixedDeltaTime;
        }
        else
        {
            ApplyMovement();
        }

        AlignWithCamera();
    }

    void Update()
    {
        RechargeDashCharges();

        if (inputHandler != null && inputHandler.DashPressed)
        {
            TrySurfaceDash();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        EmitNoise();
    }

    // -----------------------
    // Movement (camera-relative)
    // -----------------------
    private void ApplyMovement()
    {
        if (cameraRig == null || inputHandler == null)
            return;

        // 1️⃣ Read input
        Vector3 inputVector = new Vector3(
            inputHandler.MoveInput.x,
            inputHandler.VerticalInput,
            inputHandler.MoveInput.y
        );

        inputVector = Vector3.ClampMagnitude(inputVector, 1f);
        inputVector *= inputResponsiveness;

        // 2️⃣ Camera-relative direction
        Vector3 desiredDirection =
            cameraRig.right   * inputVector.x +
            cameraRig.up      * inputVector.y +
            cameraRig.forward * inputVector.z;

        // 3️⃣ Precision mode (Shift)
        bool precision = inputHandler.IsBraking;

        float speedCap = precision
            ? maxSpeed * precisionSpeedMultiplier
            : maxSpeed;

        float accel = precision
            ? acceleration * precisionAccelerationMultiplier
            : acceleration;

        float brake = precision
            ? directionalBrake * precisionBrakeMultiplier
            : directionalBrake;

        // 4️⃣ Current velocity
        Vector3 velocity = rb.linearVelocity;

        // Split velocity into wanted / unwanted
        Vector3 velocityAlongInput =
            desiredDirection.sqrMagnitude > 0.001f
            ? Vector3.Project(velocity, desiredDirection)
            : Vector3.zero;

        Vector3 unwantedVelocity = velocity - velocityAlongInput;

        // 5️⃣ Kill unwanted momentum (arcade control)
        velocity -= unwantedVelocity * brake * Time.fixedDeltaTime;

        // 6️⃣ Accelerate in desired direction
        velocity += desiredDirection * accel * Time.fixedDeltaTime;

        // 7️⃣ Soft speed cap
        if (velocity.magnitude > speedCap)
        {
            velocity = Vector3.Lerp(
                velocity,
                velocity.normalized * speedCap,
                12f * Time.fixedDeltaTime
            );
        }

        rb.linearVelocity = velocity;
    }

    // --------------------------------------------------
    // SURFACE DASH
    // --------------------------------------------------
    void TrySurfaceDash()
    {
        // Case A: touching a surface
        if (TryGetSurface(out Vector3 surfaceNormal))
        {
            ApplyDashImpulse(surfaceNormal, surfaceDashSpeed);
            return;
        }

        // Case B: not touching any surface → dash along movement
        Vector3 velocity = rb.linearVelocity;

        if (currentDashCharges <= 0)
        {
            return;
        }

        if (velocity.sqrMagnitude < 0.01f)
            return;
        ApplyDashImpulse(velocity.normalized, airDashSpeed);
        ConsumeDashCharge();
        cameraFOVPunch?.TriggerDashFOV();
    }

    bool TryGetSurface(out Vector3 surfaceNormal)
    {

        Collider[] colliders = Physics.OverlapSphere(
            rb.worldCenterOfMass,
            surfaceCheckRadius,
            surfaceMask,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.MaxValue;
        surfaceNormal = Vector3.zero;

        foreach (var col in colliders)
        {
            Vector3 closestPoint = col.ClosestPoint(rb.worldCenterOfMass);
            Vector3 direction = rb.worldCenterOfMass - closestPoint;
            float distance = direction.magnitude;

            if (distance < closestDistance && distance > 0.001f)
            {
                closestDistance = distance;
                surfaceNormal = direction.normalized;
            }
        }

        return surfaceNormal != Vector3.zero;
    }

    void ConsumeDashCharge()
    {
        currentDashCharges--;
        dashRechargeTimer = 0f;
    }

   void ApplyDashImpulse(Vector3 direction, float dashSpeed)
    {
        rb.linearVelocity = Vector3.zero;
        rb.linearVelocity = direction.normalized * dashSpeed;

        dashGraceTimer = dashGraceTime;
    }

    void RechargeDashCharges()
    {
        if (currentDashCharges >= maxDashCharges)
            return;

        dashRechargeTimer += Time.unscaledDeltaTime;

        if (dashRechargeTimer >= dashRechargeTime)
        {
            dashRechargeTimer -= dashRechargeTime;
            currentDashCharges = Mathf.Min(currentDashCharges + 1, maxDashCharges);
        }
    }


    // -----------------------
    // Rotation (camera authority)
    // -----------------------
    private void AlignWithCamera()
    {
        if (cameraRig == null)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(cameraRig.forward, cameraRig.up);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                alignmentSpeed * Time.fixedDeltaTime
            )
        );
    }

    // -----------------------
    // Noise
    // -----------------------
    private void EmitNoise()
    {
        if (noiseEmitter != null)
            noiseEmitter.EmitNoise(noiseIntensity);
    }
}
