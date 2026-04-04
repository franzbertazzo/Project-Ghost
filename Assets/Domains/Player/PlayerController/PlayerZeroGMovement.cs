using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerZeroGMovement : MonoBehaviour
{
    public bool IsDashing { get; private set; }
    public Vector3 LastDashDirection { get; private set; }
    public bool LastDashWasSurface { get; private set; }
    public Vector3 LastSurfaceNormal { get; private set; }

    // Fired immediately after a dash is applied: (worldSpaceDirection, isSurfaceDash)
    public event System.Action<Vector3, bool> OnDashPerformed;

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
    public GameObject boosterL; // For visual movement only
    public GameObject boosterR; // For visual movement only
    public GameObject upShoulderBoosterL; // For visual movement only
    public GameObject upShoulderBoosterR; // For visual movement only
    public GameObject sideShoulderBoosterL; // For visual movement only
    public GameObject sideShoulderBoosterR; // For visual movement only
    public GameObject sideBoosterL; // For visual movement only
    public GameObject sideBoosterR; // For visual movement only
    public GameObject sideFootBoosterL; // For visual movement only
    public GameObject sideFootBoosterR; // For visual movement only
    public Vector3 boosterRotationOffset = Vector3.zero; // Euler offset to fine-tune booster facing
    public float boosterSnapDuration = 1f; // Seconds to use input direction before switching to velocity
    public float boosterRotationSmoothing = 8f; // How fast boosters rotate toward target direction
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

    [Header("Surface Dash Behavior")]
    [Tooltip("Dot-product threshold to distinguish rebound/escape from glide")]
    public float reboundDotThreshold = 0.5f;
    [Tooltip("Tangent weight when gliding along surface")]
    public float surfaceTangentWeight = 0.7f;
    [Tooltip("Normal weight when gliding along surface (slight push off)")]
    public float surfaceNormalWeight = 0.3f;
    [Tooltip("Speed multiplier for hard rebound (input toward surface)")]
    public float reboundSpeedMultiplier = 1.3f;
    [Tooltip("Speed multiplier for glide (input sideways to surface)")]
    public float glideSpeedMultiplier = 1.0f;
    [Tooltip("Speed multiplier for escape dash (input away from surface)")]
    public float escapeSpeedMultiplier = 1.1f;

    [Header("Surface Stick Window")]
    [Tooltip("Time the player 'sticks' near a surface before auto-launching")]
    public float stickWindowDuration = 0.2f;
    [Tooltip("Magnetic pull strength toward the surface while sticking")]
    public float stickMagnetForce = 30f;
    [Tooltip("Speed multiplier while in stick window (slow down)")]
    public float stickSpeedDamping = 0.15f;

    [Header("Chain Bonus")]
    [Tooltip("Max time between surface dashes to keep the chain alive")]
    public float chainWindowTime = 2f;
    [Tooltip("Speed bonus per chain level (0.1 = +10%)")]
    public float chainSpeedBonusPerLevel = 0.1f;
    [Tooltip("Maximum chain level (caps the bonus)")]
    public int maxChainLevel = 5;

    [Header("Dash Charges")]
    public int maxDashCharges = 3;
    public float dashRechargeTime = 1.4f;

    private int currentDashCharges;
    private float dashRechargeTimer;

    public int CurrentDashCharges => currentDashCharges;
    public float DashRechargeProgress => dashRechargeTimer / dashRechargeTime;

    float dashGraceTimer;

    // Stick window state
    private bool _isSticking;
    private float _stickTimer;
    private Vector3 _stickSurfaceNormal;
    private Vector3 _stickSurfacePoint;

    // Chain bonus state
    private int _chainLevel;
    private float _chainTimer;

    /// <summary>Current chain level (0 = no bonus).</summary>
    public int ChainLevel => _chainLevel;

    [Header("Rotation Settings")]
    public float alignmentSpeed = 10f; // How fast player aligns to camera
    public float maxBodyAngle = 60f;   // Max angle body can be from camera before snapping back

    [Header("Noise Settings")]
    public float noiseIntensity = 1f;

    private Rigidbody rb;
    private NoiseEmitter noiseEmitter;
    private Quaternion _smoothBodyRotation;
    private float _boosterActiveTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 0.2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        _smoothBodyRotation = rb.rotation;

        currentDashCharges = maxDashCharges;

        if (inputHandler == null)
            inputHandler = GetComponent<PlayerInputHandler>();

        noiseEmitter = GetComponent<NoiseEmitter>();
    }

    void FixedUpdate()
    {
        if (_isSticking)
        {
            ApplyStickMagnet();
        }
        else if (dashGraceTimer > 0f)
        {
            dashGraceTimer -= Time.fixedDeltaTime;
        }
        else
        {
            ApplyMovement();
        }
    }

    void Update()
    {
        RechargeDashCharges();
        UpdateChainTimer();
        UpdateStickWindow();

        if (inputHandler != null && inputHandler.DashPressed)
        {
            if (_isSticking)
                ReleaseStickDash();
            else
                TrySurfaceDash();
        }
        AlignWithCamera();
        UpdateBoosters();
    }

    private void UpdateBoosters()
    {
        if (inputHandler == null) return;

        bool moving = Mathf.Abs(inputHandler.MoveInput.y) > 0.01f  // W / S
                   || inputHandler.VerticalInput > 0.01f;           // Space (up)
        bool boostersActive = moving;

        if (boosterL != null) boosterL.SetActive(boostersActive);
        if (boosterR != null) boosterR.SetActive(boostersActive);

        bool goingDown = inputHandler.VerticalInput < -0.01f; // Ctrl (down)
        if (upShoulderBoosterL != null) upShoulderBoosterL.SetActive(goingDown);
        if (upShoulderBoosterR != null) upShoulderBoosterR.SetActive(goingDown);

        if (sideBoosterR != null) sideBoosterR.SetActive(inputHandler.MoveInput.x < -0.01f); // A
        if (sideBoosterL != null) sideBoosterL.SetActive(inputHandler.MoveInput.x > 0.01f);  // D

        if (sideFootBoosterR != null) sideFootBoosterR.SetActive(inputHandler.RollInput > 0.01f);  // E
        if (sideShoulderBoosterL != null) sideShoulderBoosterL.SetActive(inputHandler.RollInput > 0.01f);  // E
        if (sideFootBoosterL != null) sideFootBoosterL.SetActive(inputHandler.RollInput < -0.01f); // Q
        if (sideShoulderBoosterR != null) sideShoulderBoosterR.SetActive(inputHandler.RollInput < -0.01f); // Q

        if (boostersActive)
        {
            _boosterActiveTimer += Time.deltaTime;

            Vector3 thrustDir = Vector3.zero;

            bool useInputDir = _boosterActiveTimer < boosterSnapDuration && moving && cameraRig != null;
            if (useInputDir)
            {
                // Snap phase: point instantly toward where the player wants to go
                Vector3 inputVec = new Vector3(0f, inputHandler.VerticalInput, inputHandler.MoveInput.y);
                thrustDir = (cameraRig.up * inputVec.y + cameraRig.forward * inputVec.z).normalized;
            }
            else if (rb.linearVelocity.sqrMagnitude > 0.01f)
            {
                // Follow phase: track actual velocity vector
                thrustDir = rb.linearVelocity.normalized;
            }

            if (thrustDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(-thrustDir)
                                          * Quaternion.Euler(boosterRotationOffset);
                float t = 1f - Mathf.Exp(-boosterRotationSmoothing * Time.deltaTime);
                if (boosterL != null) boosterL.transform.rotation = Quaternion.Slerp(boosterL.transform.rotation, targetRotation, t);
                if (boosterR != null) boosterR.transform.rotation = Quaternion.Slerp(boosterR.transform.rotation, targetRotation, t);
            }
        }
        else
        {
            _boosterActiveTimer = 0f;
        }
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
    // SURFACE STICK WINDOW
    // --------------------------------------------------
    void EnterStickWindow(Vector3 surfaceNormal)
    {
        _isSticking = true;
        _stickTimer = stickWindowDuration;
        _stickSurfaceNormal = surfaceNormal;

        // Brake hard on entry
        rb.linearVelocity *= stickSpeedDamping;
    }

    void ApplyStickMagnet()
    {
        // Gentle pull toward surface
        rb.AddForce(-_stickSurfaceNormal * stickMagnetForce, ForceMode.Acceleration);
        // Keep speed low while sticking
        rb.linearVelocity *= (1f - 5f * Time.fixedDeltaTime);
    }

    void UpdateStickWindow()
    {
        if (!_isSticking) return;

        _stickTimer -= Time.deltaTime;
        if (_stickTimer <= 0f)
        {
            // Window expired — auto-launch with current aim
            ReleaseStickDash();
        }
    }

    void ReleaseStickDash()
    {
        _isSticking = false;

        LastDashWasSurface = true;
        LastSurfaceNormal = _stickSurfaceNormal;

        Vector3 inputDir = GetCameraRelativeInput();
        Vector3 dashDir;
        float dashSpeed = surfaceDashSpeed;

        dashSpeed *= GetChainSpeedMultiplier();

        if (inputDir.sqrMagnitude < 0.01f)
        {
            dashDir = _stickSurfaceNormal;
        }
        else
        {
            float alignment = Vector3.Dot(inputDir, _stickSurfaceNormal);

            if (alignment < -reboundDotThreshold)
            {
                dashDir = _stickSurfaceNormal;
                dashSpeed *= reboundSpeedMultiplier;
            }
            else if (alignment > reboundDotThreshold)
            {
                dashDir = inputDir;
                dashSpeed *= escapeSpeedMultiplier;
            }
            else
            {
                Vector3 tangent = (inputDir - Vector3.Dot(inputDir, _stickSurfaceNormal) * _stickSurfaceNormal).normalized;
                dashDir = (tangent * surfaceTangentWeight + _stickSurfaceNormal * surfaceNormalWeight).normalized;
                dashSpeed *= glideSpeedMultiplier;
            }
        }

        AdvanceChain();
        ApplyDashImpulse(dashDir, dashSpeed);
        OnDashPerformed?.Invoke(dashDir, true);
    }

    // --------------------------------------------------
    // CHAIN BONUS SYSTEM
    // --------------------------------------------------
    void UpdateChainTimer()
    {
        if (_chainLevel > 0)
        {
            _chainTimer -= Time.deltaTime;
            if (_chainTimer <= 0f)
            {
                _chainLevel = 0;
            }
        }
    }

    void AdvanceChain()
    {
        _chainLevel = Mathf.Min(_chainLevel + 1, maxChainLevel);
        _chainTimer = chainWindowTime;
    }

    float GetChainSpeedMultiplier()
    {
        // Level 0 = 1x, Level 1 = 1x (first dash is normal), Level 2 = 1.1x, etc.
        int bonusLevels = Mathf.Max(0, _chainLevel);
        return 1f + bonusLevels * chainSpeedBonusPerLevel;
    }

    // --------------------------------------------------
    // SURFACE DASH (Tangent-Based + Rebound Control)
    // --------------------------------------------------
    void TrySurfaceDash()
    {
        if (TryGetSurface(out Vector3 surfaceNormal))
        {
            // Enter stick window instead of dashing immediately
            EnterStickWindow(surfaceNormal);
            return;
        }

        Vector3 velocity = rb.linearVelocity;
        if (currentDashCharges <= 0 || velocity.sqrMagnitude < 0.01f) return;

        LastDashWasSurface = false;
        LastSurfaceNormal = Vector3.zero;
        ApplyDashImpulse(velocity.normalized, airDashSpeed);
        OnDashPerformed?.Invoke(velocity.normalized, false);
        ConsumeDashCharge();
        cameraFOVPunch?.TriggerDashFOV();
    }

    Vector3 GetCameraRelativeInput()
    {
        if (cameraRig == null || inputHandler == null) return Vector3.zero;

        Vector3 raw = new Vector3(
            inputHandler.MoveInput.x,
            inputHandler.VerticalInput,
            inputHandler.MoveInput.y
        );
        raw = Vector3.ClampMagnitude(raw, 1f);

        return (cameraRig.right   * raw.x +
                cameraRig.up      * raw.y +
                cameraRig.forward * raw.z).normalized;
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
        LastDashDirection = direction.normalized;
        rb.linearVelocity = Vector3.zero;
        rb.linearVelocity = direction.normalized * dashSpeed;

        dashGraceTimer = dashGraceTime;

        IsDashing = true;

        PlayerHealth health = GetComponent<PlayerHealth>();
        if (health)
            health.ExternalInvulnerable = true;

        Invoke(nameof(EndDash), dashGraceTime);
    }

    void EndDash()
    {
        IsDashing = false;

        PlayerHealth health = GetComponent<PlayerHealth>();
        if (health)
            health.ExternalInvulnerable = false;
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
        if (cameraRig == null) return;

        Quaternion cameraTarget = Quaternion.LookRotation(cameraRig.forward, cameraRig.up);

        // Body chases camera at constant angular speed
        _smoothBodyRotation = Quaternion.RotateTowards(
            _smoothBodyRotation,
            cameraTarget,
            alignmentSpeed * Time.deltaTime
        );

        // Clamp: if body has fallen too far behind, pin it to maxBodyAngle from camera
        // It will still chase normally once the camera stops moving fast
        if (Quaternion.Angle(_smoothBodyRotation, cameraTarget) > maxBodyAngle)
        {
            _smoothBodyRotation = Quaternion.RotateTowards(
                cameraTarget,
                _smoothBodyRotation,
                maxBodyAngle
            );
        }

        rb.MoveRotation(_smoothBodyRotation);
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
