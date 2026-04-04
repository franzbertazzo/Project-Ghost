using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 3D Ring Radar — one ring per detected entity. Each ring circles the player and
/// tilts its plane so that it passes through the direction toward that entity.
/// The marker label sits on the ring at the point closest to the entity.
///
/// Setup:
///   1. Assign enemy and projectile marker prefabs (GameObjects with EnemyMarker / ProjectileMarker).
///   2. Set enemyLayers / projectileLayers to the physics layers your enemies and projectiles use.
///   3. Attach this component to any GameObject (auto-finds Player by tag and main camera).
/// </summary>
public class RingRadarController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found via 'Player' tag if left empty")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("Auto-found via Camera.main if left empty")]
    [SerializeField] private Transform cameraTransform;

    [Header("Marker Prefabs")]
    [SerializeField] private EnemyMarker enemyMarkerPrefab;
    [SerializeField] private ProjectileMarker projectileMarkerPrefab;

    [Header("Detection")]
    [Tooltip("Maximum detection sphere radius")]
    [SerializeField] private float detectionRange = 100f;
    [Tooltip("Distance mapped to minimum ring radius")]
    [SerializeField] private float minDistance = 5f;
    [Tooltip("Distance mapped to maximum ring radius")]
    [SerializeField] private float maxDistance = 80f;
    [Tooltip("Physics layers containing enemy colliders")]
    [SerializeField] private LayerMask enemyLayers;
    [Tooltip("Physics layers containing projectile colliders")]
    [SerializeField] private LayerMask projectileLayers;

    [Header("Ring")]
    [SerializeField] private float minRadius = 1.5f;
    [SerializeField] private float maxRadius = 3f;
    [Tooltip("Offset of ring center along player up from player position")]
    [SerializeField] private float ringVerticalOffset = 0f;

    [Header("Marker Limits")]
    [SerializeField] private int maxEnemyMarkers = 10;
    [SerializeField] private int maxProjectileMarkers = 8;
    [SerializeField] private int preloadCount = 5;

    [Header("Marker Scaling")]
    [SerializeField] private float closeMarkerScale = 1.2f;
    [SerializeField] private float farMarkerScale = 0.4f;

    [Header("Colors")]
    [Tooltip("Color when enemy is unaware / no threat")]
    [SerializeField] private Color safeColor = Color.green;
    [Tooltip("Color when enemy is suspicious / partial threat")]
    [SerializeField] private Color suspiciousColor = Color.yellow;
    [Tooltip("Color when enemy is fully aware / in fire range")]
    [SerializeField] private Color dangerColor = Color.red;

    [Header("Warning")]
    [Tooltip("Distance below which a warning pulse scales markers")]
    [SerializeField] private float warningDistance = 15f;
    [SerializeField] private float warningPulseSpeed = 4f;

    [Header("Animation")]
    [SerializeField] private float swayAmount = 0.05f;
    [SerializeField] private float swaySpeed = 2f;

    [Header("Audio")]
    [Tooltip("Optional — plays a warning clip when a projectile is close")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip projectileWarningClip;
    [SerializeField] private float audioWarningDistance = 20f;
    [SerializeField] private float audioCooldown = 1f;

    [Header("Performance")]
    [Tooltip("Seconds between enemy re-scans (projectiles scan every frame)")]
    [SerializeField] private float enemyScanInterval = 0.2f;

    [Header("Visuals")]
    [Tooltip("Base opacity for the hologram look (0 = invisible, 1 = solid)")]
    [Range(0f, 1f)]
    [SerializeField] private float ringAlpha = 0.35f;
    [Tooltip("HDR emission multiplier — values > 1 trigger bloom in post-processing")]
    [SerializeField] private float emissionIntensity = 2f;

    [Header("Scan Mode (Brake Hold)")]
    [Tooltip("Detection range when holding brake to activate scan mode")]
    [SerializeField] private float scanDetectionRange = 1000f;
    [Tooltip("How long rings stay visible after releasing brake (seconds)")]
    [SerializeField] private float ringLingerDuration = 2f;
    [Tooltip("How fast rings fade in/out (1 = 1 second to full)")]
    [SerializeField] private float ringFadeSpeed = 2f;

    [Header("Projectile Clustering")]
    [Tooltip("Enable or disable projectile ring detection")]
    [SerializeField] private bool enableProjectileRings = true;
    [Tooltip("Projectiles within this angle (degrees) of each other are merged into one ring")]
    [SerializeField] private float projectileClusterAngle = 25f;
    [Tooltip("Only show projectile rings for projectiles outside the player's view cone (dot < 0 = behind)")]
    [SerializeField] private float projectileViewDotThreshold = 0.3f;

    // --------------- runtime state ---------------

    private PlayerInputHandler inputHandler;
    private bool scanActive;
    private float ringLingerTimer;
    private float ringVisibility;

    private MarkerPool<EnemyMarker> enemyPool;
    private MarkerPool<ProjectileMarker> projectilePool;

    private readonly Dictionary<Transform, EnemyMarker> activeEnemyMarkers = new Dictionary<Transform, EnemyMarker>();

    private readonly List<Transform> detectedEnemies = new List<Transform>();
    private readonly List<Transform> detectedProjectiles = new List<Transform>();
    private readonly List<Transform> toRemove = new List<Transform>();
    private readonly HashSet<Transform> detectedSet = new HashSet<Transform>();

    private Collider[] overlapBuffer = new Collider[128];
    private float enemyScanTimer;
    private float audioCooldownTimer;

    // Projectile clustering
    private struct ProjectileCluster
    {
        public Vector3 averageDirection;
        public float closestDistance;
        public int count;
    }
    private readonly List<ProjectileCluster> projectileClusters = new List<ProjectileCluster>();
    // Stable key for each cluster so pooling works across frames
    private readonly List<int> clusterKeys = new List<int>();
    private readonly Dictionary<int, ProjectileMarker> activeClusterMarkers = new Dictionary<int, ProjectileMarker>();

    // Cached per-frame
    private Vector3 camForward;
    private Vector3 camRight;
    private Vector3 camUp;
    private Vector3 camPosition;
    private Vector3 playerPosition;
    private Vector3 ringCenter;

    // =============================================
    //  Lifecycle
    // =============================================

    void Awake()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
                if (inputHandler == null)
                {
                    inputHandler = player.GetComponent<PlayerInputHandler>();
                }
            }
        }

        if (inputHandler == null && playerTransform != null)
        {
            inputHandler = playerTransform.GetComponent<PlayerInputHandler>();
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    void Start()
    {
        if (enemyMarkerPrefab != null)
        {
            enemyPool = new MarkerPool<EnemyMarker>(enemyMarkerPrefab, transform, preloadCount);
        }

        if (projectileMarkerPrefab != null)
        {
            projectilePool = new MarkerPool<ProjectileMarker>(projectileMarkerPrefab, transform, preloadCount);
        }
    }

    void LateUpdate()
    {
        if (playerTransform == null || cameraTransform == null)
        {
            return;
        }

        CacheCameraData();

        // Keep controller (and pooled children) centered on the player
        transform.position = playerPosition;

        // --- Scan mode (brake hold) ---
        UpdateScanMode();
        float activeDetectionRange = scanActive || ringLingerTimer > 0f ? scanDetectionRange : detectionRange;

        // --- Scan ---
        enemyScanTimer -= Time.deltaTime;
        if (enemyScanTimer <= 0f)
        {
            enemyScanTimer = enemyScanInterval;
            if (enemyPool != null)
            {
                ScanEntities(activeDetectionRange, enemyLayers, detectedEnemies, maxEnemyMarkers, true);
            }
        }

        if (enableProjectileRings && projectilePool != null)
        {
            ScanEntities(activeDetectionRange, projectileLayers, detectedProjectiles, maxProjectileMarkers, false);
        }

        // --- Update markers (projectiles first for visual priority) ---
        if (enableProjectileRings && projectilePool != null)
        {
            UpdateProjectileMarkers();
            CleanupClusterMarkers();
        }
        else if (!enableProjectileRings)
        {
            // Return all active cluster markers when disabled
            foreach (var kvp in activeClusterMarkers)
            {
                projectilePool.Return(kvp.Value);
            }
            activeClusterMarkers.Clear();
        }

        if (enemyPool != null)
        {
            UpdateEnemyMarkers();
            CleanupMarkers(detectedEnemies, activeEnemyMarkers, enemyPool);
        }

        // --- Audio ---
        CheckAudioWarning();
    }

    // =============================================
    //  Scan Mode
    // =============================================

    private void UpdateScanMode()
    {
        bool brakeHeld = inputHandler != null && inputHandler.IsBraking;

        if (brakeHeld)
        {
            scanActive = true;
            ringLingerTimer = ringLingerDuration;
        }
        else
        {
            scanActive = false;
            ringLingerTimer -= Time.deltaTime;
        }

        // Fade rings in/out
        float targetVisibility = (scanActive || ringLingerTimer > 0f) ? 1f : 0f;
        ringVisibility = Mathf.MoveTowards(ringVisibility, targetVisibility, ringFadeSpeed * Time.deltaTime);

        // Apply visibility to all active markers
        foreach (var kvp in activeEnemyMarkers)
        {
            kvp.Value.SetRingVisibility(ringVisibility);
        }
        foreach (var kvp in activeClusterMarkers)
        {
            kvp.Value.SetRingVisibility(ringVisibility);
        }
    }

    // =============================================
    //  Detection
    // =============================================

    private void CacheCameraData()
    {
        camForward = cameraTransform.forward;
        camRight = cameraTransform.right;
        camUp = cameraTransform.up;
        camPosition = cameraTransform.position;
        playerPosition = playerTransform.position;
        ringCenter = playerPosition + playerTransform.up * ringVerticalOffset;
    }

    private void ScanEntities(float range, LayerMask layerMask, List<Transform> results, int maxCount, bool resolveEnemyRoot)
    {
        results.Clear();
        detectedSet.Clear();

        int count = Physics.OverlapSphereNonAlloc(playerPosition, range, overlapBuffer, layerMask);

        for (int i = 0; i < count; i++)
        {
            Transform root = overlapBuffer[i].transform;

            if (resolveEnemyRoot)
            {
                EnemyHealth eh = overlapBuffer[i].GetComponentInParent<EnemyHealth>();
                if (eh != null)
                {
                    root = eh.transform;
                }
            }

            if (detectedSet.Add(root))
            {
                results.Add(root);
            }
        }

        // Sort closest-first (for priority clamping)
        results.Sort((a, b) =>
        {
            float dA = (a.position - playerPosition).sqrMagnitude;
            float dB = (b.position - playerPosition).sqrMagnitude;
            return dA.CompareTo(dB);
        });

        if (results.Count > maxCount)
        {
            results.RemoveRange(maxCount, results.Count - maxCount);
        }
    }

    // =============================================
    //  Marker updates
    // =============================================

    private void UpdateProjectileMarkers()
    {
        // 1. Filter: only keep projectiles outside the player's view
        // 2. Cluster by direction
        // 3. One ring per cluster

        projectileClusters.Clear();
        clusterKeys.Clear();

        float clusterDot = Mathf.Cos(projectileClusterAngle * Mathf.Deg2Rad);

        for (int i = 0; i < detectedProjectiles.Count; i++)
        {
            Transform proj = detectedProjectiles[i];
            if (proj == null)
            {
                continue;
            }

            Vector3 toProj = proj.position - playerPosition;
            float dist = toProj.magnitude;
            if (dist < 0.001f)
            {
                continue;
            }

            Vector3 dir = toProj / dist;

            // Skip if projectile is in front of the player (within view cone)
            if (Vector3.Dot(dir, camForward) > projectileViewDotThreshold)
            {
                continue;
            }

            // Try to merge into an existing cluster
            bool merged = false;
            for (int c = 0; c < projectileClusters.Count; c++)
            {
                if (Vector3.Dot(dir, projectileClusters[c].averageDirection.normalized) >= clusterDot)
                {
                    var cluster = projectileClusters[c];
                    cluster.averageDirection += dir;
                    cluster.closestDistance = Mathf.Min(cluster.closestDistance, dist);
                    cluster.count++;
                    projectileClusters[c] = cluster;
                    merged = true;
                    break;
                }
            }

            if (!merged)
            {
                projectileClusters.Add(new ProjectileCluster
                {
                    averageDirection = dir,
                    closestDistance = dist,
                    count = 1
                });
                // Stable key based on insertion order
                clusterKeys.Add(i);
            }
        }

        // Limit cluster count
        if (projectileClusters.Count > maxProjectileMarkers)
        {
            // Keep clusters with closest projectiles
            // Simple approach: already sorted by detection order (closest first from scan)
            projectileClusters.RemoveRange(maxProjectileMarkers, projectileClusters.Count - maxProjectileMarkers);
            clusterKeys.RemoveRange(maxProjectileMarkers, clusterKeys.Count - maxProjectileMarkers);
        }

        // Mark which cluster keys are active this frame
        detectedSet.Clear();

        for (int c = 0; c < projectileClusters.Count; c++)
        {
            var cluster = projectileClusters[c];
            int key = clusterKeys[c];

            Vector3 dir = cluster.averageDirection.normalized;
            float distance = cluster.closestDistance;
            float normalizedDistance = Mathf.Clamp01(Mathf.InverseLerp(minDistance, maxDistance, distance));

            // Build ring axes
            Vector3 referenceUp = playerTransform.up;
            Vector3 sideways = Vector3.Cross(dir, referenceUp);
            if (sideways.sqrMagnitude < 0.001f)
            {
                sideways = Vector3.Cross(dir, playerTransform.right);
            }
            sideways.Normalize();

            Vector3 axis1 = dir;
            Vector3 axis2 = sideways;
            float radius = Mathf.Lerp(minRadius, maxRadius, normalizedDistance);

            Vector3 position = ringCenter + dir * radius;

            Vector3 ringNormal = Vector3.Cross(sideways, dir).normalized;
            Quaternion rotation = Quaternion.LookRotation(dir, ringNormal);

            // Color — projectiles are always high threat
            float closeness = 1f - normalizedDistance;
            Color color = Color.Lerp(suspiciousColor, dangerColor, closeness);

            float scale = Mathf.Lerp(farMarkerScale, closeMarkerScale, closeness);

            if (distance < warningDistance)
            {
                float pulse = (Mathf.Sin(Time.time * warningPulseSpeed) + 1f) * 0.5f;
                scale *= Mathf.Lerp(1f, 1.3f, pulse);
            }

            if (!activeClusterMarkers.TryGetValue(key, out ProjectileMarker marker))
            {
                marker = projectilePool.Get();
                marker.Activate(null);
                activeClusterMarkers[key] = marker;
            }
            else if (marker.IsFading)
            {
                marker.CancelFade();
            }

            marker.UpdateMarker(position, rotation, color, scale, normalizedDistance, ringAlpha);
            marker.UpdateRing(ringCenter, axis1, axis2, radius, color, ringAlpha, emissionIntensity);
        }
    }

    private void CleanupClusterMarkers()
    {
        // Build set of active keys this frame
        HashSet<int> activeKeys = new HashSet<int>();
        for (int i = 0; i < clusterKeys.Count; i++)
        {
            activeKeys.Add(clusterKeys[i]);
        }

        var keysToRemove = new List<int>();
        foreach (var kvp in activeClusterMarkers)
        {
            if (activeKeys.Contains(kvp.Key))
            {
                continue;
            }

            ProjectileMarker marker = kvp.Value;
            if (!marker.IsFading)
            {
                marker.StartFadeOut();
            }

            // Keep fading ring centered on the player
            marker.RefreshRingCenter(ringCenter);

            bool fullyFaded = marker.UpdateFade(Time.deltaTime);
            if (fullyFaded || !marker.gameObject.activeSelf)
            {
                projectilePool.Return(marker);
                keysToRemove.Add(kvp.Key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            activeClusterMarkers.Remove(keysToRemove[i]);
        }
    }

    private void UpdateEnemyMarkers()
    {
        // Create or reactivate markers for detected enemies
        foreach (Transform target in detectedEnemies)
        {
            if (target == null)
            {
                continue;
            }

            if (!activeEnemyMarkers.TryGetValue(target, out EnemyMarker marker))
            {
                marker = enemyPool.Get();
                marker.Activate(target);
                activeEnemyMarkers[target] = marker;
            }
            else if (marker.IsFading)
            {
                marker.CancelFade();
            }
        }

        // Update positions for ALL active markers (keeps fading markers following the player)
        foreach (var kvp in activeEnemyMarkers)
        {
            Transform target = kvp.Key;
            if (target == null)
            {
                continue;
            }

            ComputePlacement(target, out Vector3 pos, out Quaternion rot,
                out Color col, out float scale, out float normDist,
                out Vector3 axis1, out Vector3 axis2, out float radius);

            kvp.Value.UpdateMarker(pos, rot, col, scale, normDist, ringAlpha);
            kvp.Value.UpdateRing(ringCenter, axis1, axis2, radius, col, ringAlpha, emissionIntensity);
        }
    }

    // =============================================
    //  Placement math (per-entity ring orientation)
    // =============================================

    /// <summary>
    /// Returns a 0-1 threat level for an enemy based on awareness and fire range.
    /// 0 = unaware/safe, 0.5 = suspicious, 1 = fully aware / in fire range.
    /// </summary>
    private float ComputeThreatLevel(Transform target)
    {
        // Stealth enemy: use perception awareness
        EnemyPerception perception = target.GetComponent<EnemyPerception>();
        if (perception != null)
        {
            return perception.CurrentAwareness;
        }

        // Turret: threat based on distance vs detection range
        EnemyShooterSimple turret = target.GetComponentInChildren<EnemyShooterSimple>();
        if (turret != null)
        {
            float dist = Vector3.Distance(playerPosition, target.position);
            if (dist <= turret.detectionRange)
            {
                return 1f;
            }
            // Ramp up from 0 to 0.5 as approaching detection range (within 2x range)
            float outerRange = turret.detectionRange * 2f;
            if (dist < outerRange)
            {
                return Mathf.InverseLerp(outerRange, turret.detectionRange, dist) * 0.5f;
            }
            return 0f;
        }

        // Fallback: use distance-based approximation
        float fallbackDist = Vector3.Distance(playerPosition, target.position);
        return 1f - Mathf.Clamp01(Mathf.InverseLerp(minDistance, maxDistance, fallbackDist));
    }

    private Color ThreatToColor(float threat)
    {
        if (threat >= 0.5f)
        {
            float t = Mathf.InverseLerp(0.5f, 1f, threat);
            return Color.Lerp(suspiciousColor, dangerColor, t);
        }
        else
        {
            float t = Mathf.InverseLerp(0f, 0.5f, threat);
            return Color.Lerp(safeColor, suspiciousColor, t);
        }
    }

    private void ComputePlacement(Transform target,
        out Vector3 position, out Quaternion rotation,
        out Color color, out float scale, out float normalizedDistance,
        out Vector3 ringAxis1, out Vector3 ringAxis2, out float radius)
    {
        Vector3 toTarget = target.position - playerPosition;
        float distance = toTarget.magnitude;

        // 0 = close, 1 = far
        normalizedDistance = Mathf.Clamp01(Mathf.InverseLerp(minDistance, maxDistance, distance));

        // Direction from player toward target
        Vector3 dirToTarget = distance > 0.001f ? toTarget / distance : playerTransform.forward;

        // Build ring plane: axis1 = dirToTarget, axis2 = sideways (cross with player up)
        // This places the ring in the plane containing both the player and enemy,
        // oriented perpendicular to player up.
        Vector3 referenceUp = playerTransform.up;
        Vector3 sideways = Vector3.Cross(dirToTarget, referenceUp);

        if (sideways.sqrMagnitude < 0.001f)
        {
            // Target is directly above/below player — fall back to player right
            sideways = Vector3.Cross(dirToTarget, playerTransform.right);
        }
        sideways.Normalize();

        ringAxis1 = dirToTarget;
        ringAxis2 = sideways;

        // Radius mapped to distance
        radius = Mathf.Lerp(minRadius, maxRadius, normalizedDistance);

        // Marker position = the point on the ring closest to the enemy (angle 0)
        position = ringCenter + dirToTarget * radius;

        // Gentle sway
        if (swayAmount > 0f)
        {
            float sway = Mathf.Sin(Time.time * swaySpeed + Mathf.Atan2(dirToTarget.z, dirToTarget.x) * 3f) * swayAmount;
            position += dirToTarget * sway;
        }

        // Text lies on the ring, tangent pointing toward the enemy.
        // Forward = dirToTarget (text reads toward enemy), Up = ring normal (flipped to face outward)
        Vector3 ringNormal = Vector3.Cross(sideways, dirToTarget).normalized;
        rotation = Quaternion.LookRotation(dirToTarget, ringNormal);

        // Color based on threat level (awareness + fire range)
        float threat = ComputeThreatLevel(target);
        color = ThreatToColor(threat);

        // Scale (close = large, far = small)
        float closeness = 1f - normalizedDistance;
        scale = Mathf.Lerp(farMarkerScale, closeMarkerScale, closeness);

        // Warning pulse for very close targets
        if (distance < warningDistance)
        {
            float pulse = (Mathf.Sin(Time.time * warningPulseSpeed) + 1f) * 0.5f;
            scale *= Mathf.Lerp(1f, 1.3f, pulse);
        }
    }

    // =============================================
    //  Cleanup
    // =============================================

    private void CleanupMarkers<T>(
        List<Transform> detected,
        Dictionary<Transform, T> activeMarkers,
        MarkerPool<T> pool) where T : RingMarker
    {
        toRemove.Clear();

        foreach (var kvp in activeMarkers)
        {
            Transform target = kvp.Key;
            T marker = kvp.Value;

            bool isGone = target == null || !ListContains(detected, target);

            if (isGone)
            {
                if (!marker.IsFading)
                {
                    marker.StartFadeOut();
                }

                // Keep fading ring centered on the player
                marker.RefreshRingCenter(ringCenter);

                bool fullyFaded = marker.UpdateFade(Time.deltaTime);
                if (fullyFaded || !marker.gameObject.activeSelf)
                {
                    toRemove.Add(target);
                }
            }
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            if (activeMarkers.TryGetValue(toRemove[i], out T marker))
            {
                pool.Return(marker);
            }
            activeMarkers.Remove(toRemove[i]);
        }
    }

    private static bool ListContains(List<Transform> list, Transform target)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == target)
            {
                return true;
            }
        }
        return false;
    }

    // =============================================
    //  Audio
    // =============================================

    private void CheckAudioWarning()
    {
        if (audioSource == null || projectileWarningClip == null)
        {
            return;
        }

        audioCooldownTimer -= Time.deltaTime;
        if (audioCooldownTimer > 0f)
        {
            return;
        }

        for (int i = 0; i < detectedProjectiles.Count; i++)
        {
            Transform proj = detectedProjectiles[i];
            if (proj == null)
            {
                continue;
            }

            if (Vector3.Distance(proj.position, playerPosition) < audioWarningDistance)
            {
                audioSource.PlayOneShot(projectileWarningClip);
                audioCooldownTimer = audioCooldown;
                break;
            }
        }
    }

    // =============================================
    //  Editor gizmos
    // =============================================

    void OnDrawGizmosSelected()
    {
        Transform center = playerTransform != null ? playerTransform : transform;
        Vector3 pos = center.position;

        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(pos, detectionRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pos, minDistance);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(pos, maxDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, warningDistance);
    }
}
