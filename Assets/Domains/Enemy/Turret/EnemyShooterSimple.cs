using UnityEngine;
using System.Collections;

public class EnemyShooterSimple : MonoBehaviour
{
    [Header("Pivot Transforms")]
    [Tooltip("Transform that rotates horizontally (Y axis). Parent of the pitch pivot.")]
    public Transform yawPivot;
    [Tooltip("Transform that rotates vertically (local X axis). Child of the yaw pivot.")]
    public Transform pitchPivot;
    public Transform firePoint;

    [Header("Detection")]
    public float detectionRange = 30f;
    [Tooltip("Max angle from aim direction to target before the turret will fire.")]
    public float fireAngleThreshold = 15f;

    [Header("Rotation")]
    public float rotationSpeed = 90f;

    [Header("Pitch Clamp (degrees, negative = look up)")]
    public float minPitch = -30f;
    public float maxPitch = 60f;

    [Header("Projectile")]
    public GameObject bulletPrefab;

    [Header("Death")]
    public GameObject explosionPrefab;
    public GameObject debrisPrefab;
    public GameObject smokeObject;

    [Header("Firing Pattern")]
    public int bulletsPerBurst = 3;
    public float timeBetweenShots = 0.25f;
    public float burstCooldown = 1.5f;

    Transform target;
    bool isFiring;
    bool isDead;
    float initialYaw;
    Transform turretBase;
    EnemyHealth enemyHealth;
    PlayerHealth playerHealth;

    void Start()
    {
        if (yawPivot == null || pitchPivot == null || firePoint == null)
        {
            Debug.LogWarning($"[Turret] {name}: Assign yawPivot, pitchPivot, and firePoint in the Inspector.", this);
            return;
        }

        // The non-rotating reference frame: parent of yawPivot (or this transform as fallback)
        turretBase = yawPivot.parent != null ? yawPivot.parent : transform;
        initialYaw = yawPivot.localEulerAngles.y;
        enemyHealth = GetComponentInParent<EnemyHealth>();
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

    void Update()
    {
        if (isDead)
        {
            return;
        }

        if (enemyHealth != null && enemyHealth.IsDead)
        {
            Die();
            return;
        }

        if (yawPivot == null || pitchPivot == null)
        {
            return;
        }

        if (target == null)
        {
            FindPlayer();
            return;
        }

        float distanceToTarget = Vector3.Distance(yawPivot.position, target.position);
        if (distanceToTarget > detectionRange)
        {
            return;
        }

        if (playerHealth != null && !playerHealth.isVisible)
        {
            return;
        }
    }

    void LateUpdate()
    {
        if (isDead)
        {
            return;
        }

        if (target == null || yawPivot == null || pitchPivot == null)
        {
            return;
        }

        float distanceToTarget = Vector3.Distance(yawPivot.position, target.position);
        if (distanceToTarget > detectionRange)
        {
            return;
        }

        if (playerHealth != null && !playerHealth.isVisible)
        {
            return;
        }

        AimAtTarget();

        if (!isFiring && IsAimedAtTarget())
        {
            StartCoroutine(FireBurst());
        }
    }

    void AimAtTarget()
    {
        // --- Yaw (horizontal) ---
        // Use the non-rotating base to compute direction, so the angle
        // doesn't cancel out when yawPivot has already rotated.
        Vector3 localTargetPos = turretBase.InverseTransformPoint(target.position);
        float desiredYaw = Mathf.Atan2(localTargetPos.x, localTargetPos.z) * Mathf.Rad2Deg;

        float currentYaw = yawPivot.localEulerAngles.y;
        float newYaw = Mathf.MoveTowardsAngle(currentYaw, desiredYaw, rotationSpeed * Time.deltaTime);
        yawPivot.localEulerAngles = new Vector3(0f, newYaw, 0f);

        // --- Pitch (vertical) ---
        Vector3 dirFromPitch = target.position - pitchPivot.position;
        Vector3 localDir = yawPivot.InverseTransformDirection(dirFromPitch);
        float horizontalDist = Mathf.Sqrt(localDir.x * localDir.x + localDir.z * localDir.z);
        float desiredPitch = -Mathf.Atan2(localDir.y, horizontalDist) * Mathf.Rad2Deg;
        desiredPitch = Mathf.Clamp(desiredPitch, minPitch, maxPitch);

        float currentPitch = pitchPivot.localEulerAngles.x;
        if (currentPitch > 180f)
        {
            currentPitch -= 360f;
        }
        float newPitch = Mathf.MoveTowardsAngle(currentPitch, desiredPitch, rotationSpeed * Time.deltaTime);
        pitchPivot.localEulerAngles = new Vector3(newPitch, 0f, 0f);
    }

    bool IsAimedAtTarget()
    {
        Vector3 directionToTarget = (target.position - firePoint.position).normalized;
        float angle = Vector3.Angle(firePoint.forward, directionToTarget);
        return angle <= fireAngleThreshold;
    }

    IEnumerator FireBurst()
    {
        isFiring = true;

        for (int i = 0; i < bulletsPerBurst; i++)
        {
            Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
            yield return new WaitForSeconds(timeBetweenShots);
        }

        yield return new WaitForSeconds(burstCooldown);
        isFiring = false;
    }

    void Die()
    {
        isDead = true;
        isFiring = false;
        StopAllCoroutines();

        SetLayerRecursively(gameObject, 6);

        if (pitchPivot != null)
        {
            pitchPivot.gameObject.SetActive(false);
        }

        if (smokeObject != null)
        {
            smokeObject.SetActive(true);
        }

        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, yawPivot.position, Quaternion.identity);
        }

        if (debrisPrefab != null)
        {
            Instantiate(debrisPrefab, yawPivot.position, Quaternion.identity);
        }
    }

    void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (yawPivot != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(yawPivot.position, detectionRange);
        }
    }
}
