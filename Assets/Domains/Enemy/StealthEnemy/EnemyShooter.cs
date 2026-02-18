using UnityEngine;
using System.Collections;

public class EnemyShooter : MonoBehaviour
{
    [Header("References")]
    public Transform firePoint;
    public GameObject bulletPrefab;

    [Header("Shooting")]
    public float fireRate = 0.4f;
    public int bulletsPerBurst = 3;
    public float burstCooldown = 1.2f;

    Transform target; // kept only for state awareness (not aiming)
    Coroutine fireRoutine;

    // Called by EnemyStateController
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public void StartFiring()
    {
        if (fireRoutine == null)
            fireRoutine = StartCoroutine(FireLoop());
    }

    public void StopFiring()
    {
        if (fireRoutine != null)
        {
            StopCoroutine(fireRoutine);
            fireRoutine = null;
        }
    }

    IEnumerator FireLoop()
    {
        while (true)
        {
            for (int i = 0; i < bulletsPerBurst; i++)
            {
                Shoot();
                yield return new WaitForSeconds(fireRate);
            }

            yield return new WaitForSeconds(burstCooldown);
        }
    }

    void Shoot()
    {
        if (!firePoint || !bulletPrefab)
            return;

        // 🔑 IMPORTANT:
        // Bullet is fired using firePoint's current forward
        // SteeringBehavior controls facing
        Instantiate(
            bulletPrefab,
            firePoint.position,
            firePoint.rotation
        );
    }
}
