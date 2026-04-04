using UnityEngine;

public class Weapon : MonoBehaviour
{
    public Camera playerCamera;
    public Transform firePoint;
    public GameObject muzzle; 
    public GameObject projectile;

    public GameObject Target;

    [Header("Shooting")]
    public float fireRate;
    public bool isAutomatic;
    public float maxShootDistance = 100f;
    public LayerMask shootMask;

    float nextFireTime;

    [Header("Audio")]
    public AudioClip[] shootSounds;
    [Range(0f, 1f)] public float shootVolume = 0.3f;
    private AudioSource audioSource;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // ParticleSystem is managed via Play/Stop, no need to hide at start

        if (projectile == null)
            Debug.LogWarning("No projectile assigned to Weapon.");

        if (playerCamera == null)
            Debug.LogWarning("Player Camera not assigned to Weapon.");

        if (firePoint == null)
            Debug.LogWarning("Fire Point not assigned to Weapon.");

        if (muzzle == null)
            Debug.LogWarning("Muzzle not assigned to Weapon.");
    }

    public bool CanShoot()
    {
        return Time.time >= nextFireTime;
    }

    public void Shoot(bool playSound = true)
    {
        if (!CanShoot())
        {
            return;
        }

        nextFireTime = Time.time + (1f / Mathf.Max(fireRate, 0.01f));

        // Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        // Vector3 targetPoint;

        // if (Physics.Raycast(ray, out RaycastHit hit, maxShootDistance, shootMask))
        //     targetPoint = hit.point;
        // else
        //     targetPoint = ray.origin + ray.direction * maxShootDistance;

        Vector3 shootDirection = (Target.transform.position - firePoint.position).normalized;

        // // 🔴 DEBUG RAY (crosshair alignment)
        // Debug.DrawRay(
        //     firePoint.position,
        //     shootDirection * 20f,
        //     Color.red,
        //     1f
        // );

        ParticleSystem muzzlePS = muzzle.GetComponent<ParticleSystem>();
        if (muzzlePS != null)
            muzzlePS.Play();

        if (playSound && shootSounds.Length > 0)
        {
            AudioClip clip = shootSounds[Random.Range(0, shootSounds.Length)];
            audioSource.PlayOneShot(clip, shootVolume);
        }

        GameObject prefabToSpawn = projectile;
        if (prefabToSpawn != null)
        {
            Instantiate(
                prefabToSpawn,
                firePoint.position,
                Quaternion.LookRotation(shootDirection)
            );
        }
    }


}
