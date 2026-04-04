using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerShooter : MonoBehaviour
{

    public GameObject[] guns;

    [Header("Shooting")]

    PlayerInputActions input;
    bool isShooting;

    void Awake()
    {
        input = new PlayerInputActions();
    }

    void OnEnable()
    {
        input.Enable();
        input.Player.Shoot.performed += OnShootPerformed;
        input.Player.Shoot.canceled += OnShootCanceled;
    }

    void OnDisable()
    {
        input.Player.Shoot.performed -= OnShootPerformed;
        input.Player.Shoot.canceled -= OnShootCanceled;
        input.Disable();
    }

    void OnShootPerformed(InputAction.CallbackContext ctx)
    {
        isShooting = true;
        Shoot();
    }

    void OnShootCanceled(InputAction.CallbackContext ctx)
    {
        isShooting = false;
    }

    void Update()
    {
        if (!isShooting)
        {
            return;
        }

        foreach (GameObject gun in guns)
        {
            Weapon weapon = gun.GetComponent<Weapon>();
            if (weapon != null && weapon.isAutomatic)
            {
                Shoot();
                return;
            }
        }
    }
    

    public void Shoot()
    {
        bool soundPlayed = false;
        foreach (GameObject gun in guns)
        {
            Weapon weapon = gun.GetComponent<Weapon>();
            if (weapon != null)
            {
                weapon.Shoot(!soundPlayed);
                soundPlayed = true;
            }
        }
    }
}