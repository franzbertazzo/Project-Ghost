using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerShooter : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform firePoint;
    public GameObject bulletPrefab;

    [Header("Shooting")]
    public float maxShootDistance = 100f;
    public LayerMask shootMask;

    PlayerInputActions input;

    void Awake()
    {
        input = new PlayerInputActions();
    }

    void OnEnable()
    {
        input.Enable();
        input.Player.Shoot.performed += OnShoot;
    }

    void OnDisable()
    {
        input.Player.Shoot.performed -= OnShoot;
        input.Disable();
    }

    void OnShoot(InputAction.CallbackContext ctx)
    {
        Shoot();
    }

    void Shoot()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        Vector3 targetPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, maxShootDistance, shootMask))
            targetPoint = hit.point;
        else
            targetPoint = ray.origin + ray.direction * maxShootDistance;

        Vector3 shootDirection = (targetPoint - firePoint.position).normalized;

        // 🔴 DEBUG RAY (crosshair alignment)
        Debug.DrawRay(
            firePoint.position,
            shootDirection * 20f,
            Color.red,
            1f
        );

        // Instantiate(
        //     bulletPrefab,
        //     firePoint.position,
        //     Quaternion.LookRotation(shootDirection)
        // );
    }
}