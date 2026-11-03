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

    bool isCharging;

    void OnEnable()
    {
        input.Enable();
        input.Player.Shoot.performed += OnShootPerformed;
        input.Player.Shoot.canceled += OnShootCanceled;
        input.Player.Rocket.performed += OnRocketPerformed;
        input.Player.Rocket.canceled += OnRocketCanceled;
    }

    void OnDisable()
    {
        input.Player.Shoot.performed -= OnShootPerformed;
        input.Player.Shoot.canceled -= OnShootCanceled;
        input.Player.Rocket.performed -= OnRocketPerformed;
        input.Player.Rocket.canceled -= OnRocketCanceled;
        input.Disable();
    }

    void OnRocketPerformed(InputAction.CallbackContext ctx) => isCharging = true;

    void OnRocketCanceled(InputAction.CallbackContext ctx)
    {
        isCharging = false;
        Launcher()?.OnRocketReleased();
    }

    void OnShootPerformed(InputAction.CallbackContext ctx)
    {
        isShooting = true;
        Shoot();
    }

    void OnShootCanceled(InputAction.CallbackContext ctx)
    {
        isShooting = false;

        if (launcher == null)
            launcher = GetComponentInParent<Game.Weapons.RocketLauncher>();

        if (launcher != null)
            launcher.OnFireReleased();
    }

    void Update()
    {
        // The windup needs a frame-by-frame signal, not just press and release: the launcher
        // measures how long the button has been down and decides when to apply the brake.
        if (isCharging)
            Launcher()?.OnRocketHeld();

        if (!isShooting)
        {
            return;
        }

        // The launcher rate-limits itself, so holding fire simply shoots at its own interval
        // rather than needing an isAutomatic flag.
        if (launcher != null)
        {
            Shoot();
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
    

    Game.Weapons.RocketLauncher launcher;

    /// <summary>Resolved lazily and cached: this component sits beside the launcher, not on it.</summary>
    Game.Weapons.RocketLauncher Launcher()
    {
        if (launcher == null)
            launcher = GetComponentInParent<Game.Weapons.RocketLauncher>();

        return launcher;
    }

    public void Shoot()
    {
        // Rockets are the weapon in versus. The dual-pistol path below stays only so single-player
        // test scenes keep working - it fires once per hand, damages EnemyHealth only, and resolves
        // hits client-side, none of which suits PvP.
        if (launcher == null)
            launcher = GetComponentInParent<Game.Weapons.RocketLauncher>();

        if (launcher != null)
        {
            // Held-fire drives the tap/hold split; the release is what fires a charged shot.
            launcher.OnFireHeld();
            return;
        }

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