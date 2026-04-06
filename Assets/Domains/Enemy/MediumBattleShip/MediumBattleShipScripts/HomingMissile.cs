using UnityEngine;

public class HomingMissile : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 18f;
    public float rotationSpeed = 180f;

    [Header("Damage")]
    public int damage = 20;
    public float lifetime = 10f;

    [Header("VFX")]
    public GameObject explosionPrefab;

    private Transform target;

    public void Initialize(Transform target, Collider[] ignoreColliders)
    {
        this.target = target;

        Collider missileCollider = GetComponent<Collider>();
        if (missileCollider != null && ignoreColliders != null)
        {
            foreach (var col in ignoreColliders)
            {
                if (col != null)
                {
                    Physics.IgnoreCollision(missileCollider, col);
                }
            }
        }

        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (target != null)
        {
            Vector3 direction = (target.position - transform.position).normalized;
            Quaternion desired = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desired, rotationSpeed * Time.deltaTime);
        }

        transform.position += transform.forward * speed * Time.deltaTime;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (explosionPrefab != null)
        {
            var vfx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, 3f);
        }

        if (collision.collider.CompareTag("Player"))
        {
            PlayerHealth playerHealth = collision.collider.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
            }
        }

        Destroy(gameObject);
    }
}
