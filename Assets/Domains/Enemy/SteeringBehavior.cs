using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SteeringBehavior : MonoBehaviour
{
    [Header("Movement")]
    public float maxSpeed = 6f;
    public float acceleration = 8f;
    public float damping = 1.2f;

    [Header("Arrival")]
    public float slowDownDistance = 2.5f;
    public float stopDistance = 0.5f;

    [Header("Rotation")]
    public float rotationSpeed = 6f;

    [Header("Obstacle Avoidance")]
    public float avoidRadius = 1f;
    public float avoidDistance = 4f;
    public float avoidStrength = 3f;

    [Header("Wander (Stealth)")]
    public float wanderWeight = 0.4f;
    public float wanderChangeInterval = 2.0f;
    public float wanderAngleRange = 35f;
    public float wanderResponsiveness = 2f;

    [Header("Debug")]
    public bool drawGizmos = true;

    private Rigidbody rb;
    private Transform target;

    private Vector3 desiredDirection;
    private Vector3 desiredVelocity;
    private Vector3 lookDirection;

    // Wander state
    private Vector3 wanderIntent;
    private float wanderTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        lookDirection = transform.forward;
        wanderIntent = transform.forward;
        wanderTimer = wanderChangeInterval;
    }

    // =====================================================
    // PUBLIC API
    // =====================================================
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    // =====================================================
    // PRESETS (CALL FROM STATE CONTROLLER)
    // =====================================================

    public void ApplyPatrolPreset()
    {
        maxSpeed = 4.5f;
        acceleration = 6f;
        rotationSpeed = 4f;

        wanderWeight = 0.3f;
        wanderChangeInterval = 2.5f;
        wanderAngleRange = 25f;
        wanderResponsiveness = 1.5f;
    }

    public void ApplySuspiciousPreset()
    {
        maxSpeed = 5.5f;
        acceleration = 7f;
        rotationSpeed = 5.5f;

        wanderWeight = 0.6f;
        wanderChangeInterval = 1.5f;
        wanderAngleRange = 45f;
        wanderResponsiveness = 2.5f;
    }

    public void ApplyAlertedPreset()
    {
        maxSpeed = 7.5f;
        acceleration = 10f;
        rotationSpeed = 7f;

        wanderWeight = 0f;
    }

    // =====================================================
    // CORE UPDATE
    // =====================================================
    void FixedUpdate()
    {
        if (target == null)
        {
            ApplyIdleDamping();
            return;
        }

        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // =========================
        // ARRIVAL DECELERATION
        // =========================
        float speedScale = 1f;

        if (distanceToTarget <= slowDownDistance)
        {
            speedScale = Mathf.InverseLerp(
                stopDistance,
                slowDownDistance,
                distanceToTarget
            );
        }

        if (distanceToTarget <= stopDistance)
        {
            rb.linearVelocity = Vector3.Lerp(
                rb.linearVelocity,
                Vector3.zero,
                damping * Time.fixedDeltaTime
            );
            rb.angularVelocity = Vector3.zero;
            return;
        }

        // =========================
        // BASE INTENT
        // =========================
        desiredDirection = (target.position - transform.position).normalized;

        // =========================
        // WANDER (INTENT-BASED)
        // =========================
        if (wanderWeight > 0f)
        {
            wanderTimer -= Time.fixedDeltaTime;

            if (wanderTimer <= 0f)
            {
                wanderTimer = wanderChangeInterval;

                float yaw = Random.Range(-wanderAngleRange, wanderAngleRange);
                float pitch = Random.Range(-wanderAngleRange, wanderAngleRange);

                Quaternion randomRot = Quaternion.Euler(pitch, yaw, 0f);
                wanderIntent = randomRot * transform.forward;
            }

            Vector3 blendedWander =
                Vector3.Slerp(desiredDirection, wanderIntent, wanderWeight);

            desiredDirection = Vector3.Slerp(
                desiredDirection,
                blendedWander,
                wanderResponsiveness * Time.fixedDeltaTime
            );
        }

        // =========================
        // OBSTACLE AVOIDANCE
        // =========================
        Vector3 avoid = ComputeAvoidance(desiredDirection);
        desiredDirection = (desiredDirection + avoid).normalized;

        // =========================
        // VELOCITY CONTROL
        // =========================
        desiredVelocity = desiredDirection * maxSpeed * speedScale;
        Vector3 velocityDelta = desiredVelocity - rb.linearVelocity;
        Vector3 accel = Vector3.ClampMagnitude(velocityDelta, acceleration);

        rb.AddForce(accel, ForceMode.Acceleration);

        // =========================
        // ROTATION (INTENT-DRIVEN)
        // =========================
        lookDirection = Vector3.Slerp(
            lookDirection,
            desiredDirection,
            rotationSpeed * Time.fixedDeltaTime
        );

        transform.rotation = Quaternion.LookRotation(lookDirection, transform.up);
    }

    // =====================================================
    // OBSTACLE AVOIDANCE
    // =====================================================
    Vector3 ComputeAvoidance(Vector3 forward)
    {
        RaycastHit hit;

        if (Physics.SphereCast(
            transform.position,
            avoidRadius,
            forward,
            out hit,
            avoidDistance
        ))
        {
            Vector3 slide =
                Vector3.ProjectOnPlane(forward, hit.normal).normalized;

            return slide * avoidStrength;
        }

        return Vector3.zero;
    }

    // =====================================================
    // IDLE
    // =====================================================
    void ApplyIdleDamping()
    {
        rb.linearVelocity = Vector3.Lerp(
            rb.linearVelocity,
            Vector3.zero,
            damping * Time.fixedDeltaTime
        );

        rb.angularVelocity = Vector3.zero;
    }

    // =====================================================
    // DEBUG
    // =====================================================
    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(
            transform.position,
            transform.position + lookDirection * 3f
        );

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(
            transform.position,
            transform.position + wanderIntent * 2f
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            transform.position + lookDirection * avoidDistance,
            avoidRadius
        );
    }
}
