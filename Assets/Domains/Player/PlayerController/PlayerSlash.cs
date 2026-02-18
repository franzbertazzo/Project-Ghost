using UnityEngine;

public class PlayerSlash : MonoBehaviour
{
    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Enemy"))
        {
            // Here you can add logic to damage the enemy, play effects, etc.
            Destroy(collision.gameObject); // Example: destroy the enemy on hit
        }
    }
}
