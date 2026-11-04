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

    /// <summary>
    /// Forces precision/brake mode from outside the input system, without disabling this script.
    ///
    /// Used while charging the heavy attack: the player keeps control and can still manoeuvre,
    /// but at braked speed and acceleration. Disabling movement entirely made charging far too
    /// punishing in a four-player fight, and froze the booster visuals with it.
    /// </summary>
    public bool ExternalBrake { get; set; }

    /// <summary>
    /// Asks for a dash on the next Update, for a bot.
    ///
    /// A latch rather than the input handler's DashPressed flag, because that flag is set in Update
    /// and cleared in LateUpdate while this script *reads* it in Update - so whether a dash asked
    /// for by code was ever seen came down to the undefined order of two components' Update calls.
    /// A player gets away with it because their press arrives from the input system's own phase;
    /// code does not, which is why bots appeared never to dash. A latch read-and-cleared here is
    /// order-independent.
    /// </summary>
    public void RequestDash() => _dashRequested = true;

    private bool _dashRequested;

    [Header("Launch (knockback)")]
    [Tooltip("Seconds of launch state per metre/second of launch speed. This is what lets a " +
             "knockback actually travel: while launched, the arcade brake and speed cap are off.")]
    public float launchStunPerSpeed = 0.022f;

    [Tooltip("Launches slower than this do not stun at all, so chip damage from the laser shoves " +
             "you without taking control away.")]
    public float minLaunchSpeedForStun = 18f;

    [Tooltip("Hard ceiling on the launch state, so a huge hit cannot leave you a passenger for " +
             "ten seconds.")]
    public float maxLaunchStun = 2.5f;

    [Tooltip("Fraction of normal acceleration you keep while launched. Smash's directional " +
             "influence: enough to steer toward a wall to kick off, not enough to cancel the hit.")]
    [Range(0f, 1f)]
    public float launchDriftControl = 0.25f;

    /// <summary>
    /// True while being thrown by a hit.
    ///
    /// The arcade movement is built to erase momentum it did not ask for - directionalBrake kills
    /// "unwanted" velocity at 18/sec and a soft cap drags anything over maxSpeed (12) back down.
    /// That is right for flying and fatal for knockback: a 50 m/s launch was gone inside a tenth
    /// of a second, which is why percentage appeared to do nothing however high the numbers went.
    /// While this is set, both are suspended and the hit is allowed to carry you.
    /// </summary>
    public bool IsLaunched => launchStunTimer > 0f;

    private float launchStunTimer;

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

    [Header("Radar / Brake Mode (middle mouse)")]
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
        // Checked first: a launch overrides sticking to a surface and any dash grace still
        // running, because being hit should interrupt whatever you were doing.
        if (launchStunTimer > 0f)
        {
            launchStunTimer -= Time.fixedDeltaTime;
            ApplyLaunchDrift();
        }
        else if (_isSticking)
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

    /// <summary>
    /// Throws this player, and suspends arcade control for long enough that the throw lands.
    ///
    /// Duration scales with the resulting speed, so the percentage system reads the way it should:
    /// a light hit shoves you and you fly on, a heavy one at high percentage takes you away and
    /// you watch the boundary approach. Below minLaunchSpeedForStun there is no stun at all, so
    /// laser chip damage never steals control.
    /// </summary>
    public void ApplyLaunch(Vector3 impulse)
    {
        if (rb == null)
        {
            return;
        }

        // A stuck player has the magnet holding them to a surface; without this they absorb the
        // hit into the wall and barely move.
        _isSticking = false;
        dashGraceTimer = 0f;

        rb.linearVelocity += impulse;

        float speed = rb.linearVelocity.magnitude;
        if (speed < minLaunchSpeedForStun)
        {
            return;
        }

        // Takes the longer of the two rather than overwriting: a second hit mid-flight should
        // never shorten the launch already in progress.
        launchStunTimer = Mathf.Max(launchStunTimer,
            Mathf.Min(maxLaunchStun, speed * launchStunPerSpeed));
    }

    /// <summary>Clears the launch state, for respawns.</summary>
    public void ClearLaunch()
    {
        launchStunTimer = 0f;
    }

    /// <summary>
    /// Directional influence while launched: thrust, but no braking and no speed cap.
    ///
    /// Without any control a launch is a cutscene you watch; with full control it is not a launch.
    /// A quarter of normal acceleration lets a good player angle toward a surface to kick off
    /// before the boundary, which is the recovery skill this mechanic is supposed to create.
    /// </summary>
    private void ApplyLaunchDrift()
    {
        if (inputHandler == null || cameraRig == null || launchDriftControl <= 0f)
        {
            return;
        }

        Vector3 inputVector = new Vector3(
            inputHandler.MoveInput.x,
            inputHandler.VerticalInput,
            inputHandler.MoveInput.y
        );

        if (inputVector.sqrMagnitude < 0.001f)
        {
            return;
        }

        inputVector = Vector3.ClampMagnitude(inputVector, 1f);

        Vector3 desiredDirection =
            cameraRig.right   * inputVector.x +
            cameraRig.up      * inputVector.y +
            cameraRig.forward * inputVector.z;

        rb.linearVelocity += desiredDirection * acceleration * launchDriftControl * Time.fixedDeltaTime;
    }

    void Update()
    {
        RechargeDashCharges();
        UpdateChainTimer();
        UpdateStickWindow();

        // Latched requests are read here and cleared whether or not they are used, so a dash asked
        // for by code is never held over into a later frame.
        bool requested = _dashRequested;
        _dashRequested = false;

        // No dashing out of a launch. A dash writes rb.linearVelocity outright, so allowing one
        // here would let a player delete any knockback the instant it landed - and with it the
        // entire percentage system. The stun is the window where you cannot save yourself.
        if (((inputHandler != null && inputHandler.DashPressed) || requested) && !IsLaunched)
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
        // Either the player holding brake, or something else asking for it - charging the heavy
        // attack forces precision mode rather than freezing the player outright.
        bool precision = inputHandler.IsBraking || ExternalBrake;

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
        ConsumeDashCharge();
        ApplyDashImpulse(dashDir, dashSpeed);
        OnDashPerformed?.Invoke(dashDir, true);

        // Moved here from the old air dash, which was the only path that punched the FOV. Deleting
        // that path would otherwise have taken the dash's only piece of camera feedback with it.
        cameraFOVPunch?.TriggerDashFOV();
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
    /// <summary>
    /// Surfaces only. There is no mid-air dash any more.
    ///
    /// It used to be both: a kick off a nearby surface, free and unlimited, or a free-flight dash
    /// along your current velocity that cost a charge. That made the charges a resource you spent
    /// in open space and the surfaces a thing you did for nothing, which is backwards for an arena
    /// built out of surfaces to kick off. Now the charges are the cost of the surface kick, so the
    /// three of them are a budget for how much you can redirect before you have to coast - and
    /// open space is genuinely open, with nothing to do in it but carry the momentum you already
    /// have.
    ///
    /// The charge is checked here and spent in <see cref="ReleaseStickDash"/>, so sticking to a
    /// surface you cannot afford to kick off is not possible.
    /// </summary>
    void TrySurfaceDash()
    {
        if (currentDashCharges <= 0)
            return;

        if (!TryGetSurface(out Vector3 surfaceNormal))
            return;

        // Enter stick window instead of dashing immediately
        EnterStickWindow(surfaceNormal);
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
