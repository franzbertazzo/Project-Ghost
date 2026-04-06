using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BattleshipMovement : MonoBehaviour
{
    [Header("Approach")]
    public float approachSpeed = 12f;
    public float approachAcceleration = 4f;

    [Header("Orbit")]
    public float orbitRadius = 40f;
    public float orbitSpeed = 15f;
    public float orbitAcceleration = 3f;

    [Header("Transition")]
    [Tooltip("How close to orbitRadius the ship needs to be before it starts orbiting.")]
    public float orbitEntryThreshold = 5f;

    [Header("Rotation")]
    public float rotationSpeed = 2f;

    [Header("Vertical")]
    [Tooltip("Gentle vertical oscillation while orbiting.")]
    public float verticalAmplitude = 3f;
    public float verticalFrequency = 0.3f;

    private Rigidbody rb;
    private Transform target;
    private PlayerHealth playerHealth;
    private BattleshipController battleship;
    private bool isOrbiting;
    private bool isAware;
    public bool IsAware => isAware;
    private float orbitAngle;
    private float verticalOffset;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        battleship = GetComponent<BattleshipController>();
    }

    void Start()
    {
        FindPlayer();
    }

    void FindPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            target = player.transform;
            playerHealth = player.GetComponent<PlayerHealth>();
        }
    }

    void FixedUpdate()
    {
        if (target == null)
        {
            FindPlayer();
            return;
        }

        if (!isAware)
        {
            float distToPlayer = Vector3.Distance(rb.position, target.position);
            float awarenessRange = battleship != null ? battleship.awarenessRange : 80f;
            bool playerVisible = playerHealth == null || playerHealth.isVisible;

            if (distToPlayer <= awarenessRange && playerVisible)
            {
                isAware = true;
            }
            else
            {
                return;
            }
        }

        float distanceToTarget = Vector3.Distance(rb.position, target.position);

        if (!isOrbiting)
        {
            if (distanceToTarget <= orbitRadius + orbitEntryThreshold)
            {
                isOrbiting = true;
                // Initialize orbit angle from current position relative to player
                Vector3 offset = rb.position - target.position;
                orbitAngle = Mathf.Atan2(offset.z, offset.x);
            }
            else
            {
                Approach(distanceToTarget);
            }
        }

        if (isOrbiting)
        {
            Orbit();
        }

        RotateTowardsPlayer();
    }

    void Approach(float distance)
    {
        Vector3 direction = (target.position - rb.position).normalized;

        // Slow down as we get closer to orbit radius
        float slowFactor = Mathf.Clamp01((distance - orbitRadius) / orbitRadius);
        float targetSpeed = approachSpeed * slowFactor;

        Vector3 desiredVelocity = direction * targetSpeed;
        Vector3 steer = (desiredVelocity - rb.linearVelocity) * approachAcceleration;

        rb.AddForce(steer, ForceMode.Acceleration);
    }

    void Orbit()
    {
        // Advance the orbit angle
        float angularSpeed = orbitSpeed / orbitRadius;
        orbitAngle += angularSpeed * Time.fixedDeltaTime;

        // Vertical bob
        verticalOffset = Mathf.Sin(Time.time * verticalFrequency * Mathf.PI * 2f) * verticalAmplitude;

        // Desired position on the orbit circle
        Vector3 desiredPos = target.position + new Vector3(
            Mathf.Cos(orbitAngle) * orbitRadius,
            verticalOffset,
            Mathf.Sin(orbitAngle) * orbitRadius
        );

        Vector3 toDesired = desiredPos - rb.position;
        Vector3 desiredVelocity = toDesired * orbitAcceleration;

        // Clamp to prevent overshooting
        if (desiredVelocity.magnitude > orbitSpeed)
        {
            desiredVelocity = desiredVelocity.normalized * orbitSpeed;
        }

        Vector3 steer = (desiredVelocity - rb.linearVelocity) * orbitAcceleration;
        rb.AddForce(steer, ForceMode.Acceleration);
    }

    void RotateTowardsPlayer()
    {
        Vector3 directionToPlayer = (target.position - rb.position).normalized;
        if (directionToPlayer.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer);
        Quaternion newRotation = Quaternion.Slerp(
            rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);

        rb.MoveRotation(newRotation);
    }
}
