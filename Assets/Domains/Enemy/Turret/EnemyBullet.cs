using UnityEngine;

public class EnemyBullet : MonoBehaviour
{
    public GameObject hitPrefab;
    public float speed = 14f;
    public float lifetime = 5f;

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {

        speed = 0;

        Vector3 pos = other.ClosestPoint(transform.position);
        Vector3 normal = (transform.position - other.transform.position).normalized;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);

        if(hitPrefab != null)
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

        if (other.CompareTag("Player"))
        {
            PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();

            if (playerHealth != null)
            {
                playerHealth.TakeDamage(10);
            }
        } 
        Destroy(this.gameObject);
    }
}
