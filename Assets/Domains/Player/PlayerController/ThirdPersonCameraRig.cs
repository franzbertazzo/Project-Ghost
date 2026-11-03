using UnityEngine;

public class ThirdPersonCameraRig : MonoBehaviour
{
    public Transform target;
    public float sensitivity = 0.2f;
    public float rollSpeed = 60f;

    /// <summary>
    /// Stops the rig following its target while leaving the look controls live.
    ///
    /// Used for the death camera: the rig holds the spot the player died on and they can still
    /// turn and watch the fight until they respawn. Set rather than disabling this component,
    /// because disabling it would take the look input away too and leave them staring at one
    /// fixed angle - and because the body teleports to its spawn point the instant it dies, so a
    /// rig that kept following would cut away from the death before the explosion had played.
    /// </summary>
    public bool HoldPosition { get; set; }

    PlayerInputHandler input;

    void Start()
    {
        if (target != null)
            input = target.GetComponent<PlayerInputHandler>();
    }

    void LateUpdate()
    {
        if (target == null || input == null)
            return;

        Vector2 look = input.LookInput;

        // 1️⃣ Rotate around local UP (yaw)
        Quaternion yawRotation =
            Quaternion.AngleAxis(look.x * sensitivity, transform.up);

        // 2️⃣ Rotate around local RIGHT (pitch)
        Quaternion pitchRotation =
            Quaternion.AngleAxis(-look.y * sensitivity, transform.right);

        // 3️⃣ Roll (Q / E) around FORWARD
        Quaternion rollRotation =
            Quaternion.AngleAxis(
                -input.RollInput * rollSpeed * Time.deltaTime,
                transform.forward
            );

        // Apply rotations incrementally
        transform.rotation =
            yawRotation *
            pitchRotation *
            rollRotation *
            transform.rotation;

        // Follow player. Skipped while holding, which is the whole death camera: the rotation
        // above still runs, so the view stays yours.
        if (!HoldPosition)
            transform.position = target.position;
    }
}
