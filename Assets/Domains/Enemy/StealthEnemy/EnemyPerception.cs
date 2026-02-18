using System;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyPerception : MonoBehaviour
{
    [Header("General")]
    [Tooltip("Player transform. If left empty, will search for object with tag 'Player'.")]
    public Transform player;

    [Tooltip("Optional eye origin. If null, transform.position is used.")]
    public Transform eyes;

    [Header("Vision")]
    [Tooltip("Main cone FOV (degrees) for sharp central vision.")]
    public float primaryFOV = 60f;

    [Tooltip("Wider cone for peripheral vision (degrees).")]
    public float peripheralFOV = 120f;

    [Tooltip("Max distance for primary vision.")]
    public float primaryViewDistance = 25f;

    [Tooltip("Max distance for peripheral vision (usually shorter).")]
    public float peripheralViewDistance = 12f;

    [Tooltip("Vertical tolerance in meters (how far above/below eyes the player can be).")]
    public float heightTolerance = 2f;

    [Tooltip("Layers that block line of sight.")]
    public LayerMask obstructionMask = ~0;

    [Header("Awareness")]
    [Tooltip("Time in seconds, looking directly at the player, to fully spot them.")]
    public float timeToFullySpot = 1.5f;

    [Tooltip("How fast awareness decays back to 0 when not seeing/hearing the player.")]
    public float timeToForget = 3f;

    [Tooltip("Minimum awareness to be considered 'suspicious'.")]
    [Range(0f, 1f)]
    public float suspiciousThreshold = 0.2f;

    [Tooltip("Awareness bonus when hearing a noise inside the hearing radius.")]
    public float hearingAwarenessBonus = 0.3f;

    [Header("Hearing")]
    [Tooltip("Max distance at which normal noises can be heard.")]
    public float hearingRadius = 15f;

    [Header("Debug")]
    public bool drawGizmos = true;

    [SerializeField, Range(0f, 1f)]
    private float _currentAwareness; // shows in Inspector

    public float CurrentAwareness
    {
        get => _currentAwareness;
        private set => _currentAwareness = Mathf.Clamp01(value);
    }
    public bool IsSuspicious => CurrentAwareness >= suspiciousThreshold && CurrentAwareness < 1f;
    public bool IsFullyAware  => CurrentAwareness >= 1f;

    public bool HasLastKnownPosition { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }

    public event Action<EnemyPerception> OnBecomeSuspicious;
    public event Action<EnemyPerception> OnFullySpotted;
    public event Action<EnemyPerception> OnLoseSuspicion;

    float _awarenessVelocity; // for smoothing
    bool _wasSuspiciousLastFrame;
    bool _wasFullyAwareLastFrame;

    void Awake()
    {
        if (!eyes) eyes = transform;

        if (!player)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj) player = playerObj.transform;
        }
    }

    void OnEnable()
    {
        NoiseSystem.OnNoiseEmitted += HandleNoise;
    }

    void OnDisable()
    {
        NoiseSystem.OnNoiseEmitted -= HandleNoise;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        float awarenessChange = 0f;

        if (player)
        {
            bool canSeePlayer = EvaluateVision(out float visionFactor);

            if (canSeePlayer)
            {
                // When looking at the player, awareness rises towards 1.
                float gainRate = 1f / Mathf.Max(timeToFullySpot, 0.01f); // per second
                awarenessChange += gainRate * visionFactor * dt;

                LastKnownPosition = player.position;
                HasLastKnownPosition = true;
            }
            else
            {
                // Decay awareness over time.
                float decayRate = -1f / Mathf.Max(timeToForget, 0.01f);
                awarenessChange += decayRate * dt;
            }
        }

        // Integrate and clamp awareness.
        CurrentAwareness += awarenessChange;

        // Events & state changes.
        bool isSuspiciousNow = IsSuspicious;
        bool isFullyAwareNow = IsFullyAware;

        if (!_wasSuspiciousLastFrame && isSuspiciousNow)
            OnBecomeSuspicious?.Invoke(this);

        if (!_wasFullyAwareLastFrame && isFullyAwareNow)
            OnFullySpotted?.Invoke(this);

        if (_wasSuspiciousLastFrame && !isSuspiciousNow && !isFullyAwareNow)
            OnLoseSuspicion?.Invoke(this);

        _wasSuspiciousLastFrame = isSuspiciousNow;
        _wasFullyAwareLastFrame = isFullyAwareNow;
    }

    bool EvaluateVision(out float visionFactor)
    {
        visionFactor = 0f;
        if (!player) return false;

        Vector3 eyePos = eyes.position;
        Vector3 toPlayer = player.position - eyePos;

        // Ignore if player is too far vertically (optional, since you're in zero-G).
        float verticalOffset = Mathf.Abs(toPlayer.y);
        if (verticalOffset > heightTolerance)
            return false;

        float distance = toPlayer.magnitude;
        if (distance <= 0.01f) return false;

        Vector3 forward = eyes.forward;
        Vector3 dir = toPlayer / distance;
        float angle = Vector3.Angle(forward, dir);

        bool inPrimary = angle <= primaryFOV * 0.5f && distance <= primaryViewDistance;
        bool inPeripheral = !inPrimary && angle <= peripheralFOV * 0.5f && distance <= peripheralViewDistance;

        if (!inPrimary && !inPeripheral)
            return false;

        // Line of sight check.
        if (Physics.Raycast(eyePos, dir, out RaycastHit hit, distance, obstructionMask))
        {
            if (hit.transform != player && !hit.transform.IsChildOf(player))
            {
                // Something else is blocking.
                return false;
            }
        }

        // Convert to a factor for awareness speed.
        if (inPrimary)
        {
            // Strong vision: full rate, modulated by distance.
            float distFactor = Mathf.InverseLerp(primaryViewDistance, 0f, distance);
            visionFactor = Mathf.Lerp(0.5f, 1f, distFactor); // closer = faster
        }
        else // peripheral
        {
            float distFactor = Mathf.InverseLerp(peripheralViewDistance, 0f, distance);
            visionFactor = 0.4f * distFactor; // slower than primary
        }

        return true;
    }

    void HandleNoise(NoiseData noise)
    {
        // Check if this noise is in range.
        float distance = Vector3.Distance(transform.position, noise.Position);
        if (distance > hearingRadius + noise.Loudness)
            return;

        // Awareness bump scaled by proximity and loudness.
        float proximity = Mathf.InverseLerp(hearingRadius + noise.Loudness, 0f, distance);
        float bonus = hearingAwarenessBonus * proximity;

        CurrentAwareness += bonus;

        LastKnownPosition = noise.Position;
        HasLastKnownPosition = true;

        // Hearing alone can make the enemy suspicious but not instantly fully aware.
        if (!_wasSuspiciousLastFrame && IsSuspicious)
        {
            OnBecomeSuspicious?.Invoke(this);
            _wasSuspiciousLastFrame = true;
        }
    }

    public bool HasDirectVisual()
    {
        if (!player) return false;
        return EvaluateVision(out _); // ignore visionFactor, just need true/false
    }


    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Transform eyeRef = eyes ? eyes : transform;

        Gizmos.color = Color.yellow;
        DrawCone(eyeRef, primaryFOV, primaryViewDistance);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        DrawCone(eyeRef, peripheralFOV, peripheralViewDistance);

        Gizmos.color = new Color(0f, 0f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, hearingRadius);

        if (HasLastKnownPosition)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(LastKnownPosition, 0.2f);
        }
    }

    void DrawCone(Transform origin, float fov, float distance)
    {
        int segments = 24;
        float halfFov = fov * 0.5f;

        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(-halfFov, halfFov, t);
            Quaternion rot = Quaternion.AngleAxis(angle, origin.up);
            Vector3 dir = rot * origin.forward;
            Vector3 point = origin.position + dir * distance;

            if (i > 0)
            {
                Gizmos.DrawLine(prev, point);
            }

            prev = point;
        }
    }
}
