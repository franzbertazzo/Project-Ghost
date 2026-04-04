using UnityEngine;

public class PlayerBullet : MonoBehaviour
{
    public GameObject hitPrefab;
    public float speed = 20f;
    public float lifetime = 5f;
    public int damage = 25;

    void Start()
    {
        // Ignore collisions between this bullet and the player
        Collider bulletCollider = GetComponent<Collider>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (bulletCollider != null && player != null)
        {
            foreach (Collider playerCol in player.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(bulletCollider, playerCol);
            }
        }

        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (speed != 0)
        {
            transform.position += transform.forward * (speed * Time.deltaTime);
        }
    }

    void OnCollisionEnter(Collision co)
    {
        speed = 0;

        ContactPoint contact = co.contacts[0];
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, contact.normal);
        Vector3 pos = contact.point;

        if (hitPrefab != null)
        {
            var hitVFX = Instantiate(hitPrefab, pos, rot);
            var psHit = hitVFX.GetComponent<ParticleSystem>();
            if (psHit != null)
            {
                Destroy(hitVFX, psHit.main.duration);
            }
            else
            {
                var psChild = hitVFX.transform.GetChild(0).GetComponent<ParticleSystem>();
                Destroy(hitVFX, psChild.main.duration);
            }
        }

        EnemyHealth enemyHealth = co.collider.GetComponentInParent<EnemyHealth>();
        if (enemyHealth != null)
        {
            enemyHealth.TakeDamage(damage);
        }

        Destroy(gameObject);
    }
}
