using UnityEngine;

public class MissileLauncher : MonoBehaviour
{
    [Header("Missile Settings")]
    public GameObject missilePrefab;
    public Transform[] firePoints;
    public float fireInterval = 3f;

    [Header("Detection")]
    public float detectionRange = 80f;

    private Transform target;
    private PlayerHealth playerHealth;
    private float fireTimer;
    private int currentFirePointIndex;
    private Collider[] shipColliders;

    void Start()
    {
        fireTimer = fireInterval;
        shipColliders = transform.root.GetComponentsInChildren<Collider>();
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
        if (target == null)
        {
            FindPlayer();
            return;
        }

        if (playerHealth != null && !playerHealth.isVisible)
        {
            return;
        }

        float distance = Vector3.Distance(transform.position, target.position);
        if (distance > detectionRange)
        {
            return;
        }

        fireTimer -= Time.deltaTime;
        if (fireTimer <= 0f)
        {
            FireMissile();
            fireTimer = fireInterval;
        }
    }

    void FireMissile()
    {
        if (missilePrefab == null || firePoints == null || firePoints.Length == 0)
        {
            return;
        }

        Transform spawnPoint = firePoints[currentFirePointIndex % firePoints.Length];
        currentFirePointIndex++;

        GameObject missileObj = Instantiate(missilePrefab, spawnPoint.position, spawnPoint.rotation);
        HomingMissile missile = missileObj.GetComponent<HomingMissile>();
        if (missile != null)
        {
            missile.Initialize(target, shipColliders);
        }
    }
}
