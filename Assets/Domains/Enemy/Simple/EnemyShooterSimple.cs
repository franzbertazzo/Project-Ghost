using UnityEngine;
using System.Collections;

public class EnemyShooterSimple : MonoBehaviour
{
    public Transform target;
    public Transform firePoint;
    public GameObject bulletPrefab;

    [Header("Firing Pattern")]
    public int bulletsPerBurst = 3;
    public float timeBetweenShots = 0.25f;
    public float burstCooldown = 1.5f;

    bool isFiring;

    void Update()
    {
        if (!isFiring && target != null )
        {
            StartCoroutine(FireBurst());
        }
    }

    IEnumerator FireBurst()
    {
        isFiring = true;

        for (int i = 0; i < bulletsPerBurst; i++)
        {
            Vector3 dir = (target.position - firePoint.position).normalized;
            firePoint.forward = dir;

            Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
            yield return new WaitForSeconds(timeBetweenShots);
        }

        yield return new WaitForSeconds(burstCooldown);
        isFiring = false;
    }
}
