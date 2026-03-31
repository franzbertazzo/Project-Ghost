using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerShooter : MonoBehaviour
{

    public GameObject[] guns;

    [Header("Shooting")]

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
    

    public void Shoot()
    {
        foreach (GameObject gun in guns)
        {
            if (gun.GetComponent<Weapon>() != null)
            {
                gun.GetComponent<Weapon>().Shoot();
            }
        }
    }
}