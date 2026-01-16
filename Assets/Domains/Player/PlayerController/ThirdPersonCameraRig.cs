using UnityEngine;

public class ThirdPersonCameraRig : MonoBehaviour
{
    public Transform target;
    public float sensitivity = 0.2f;
    public float rollSpeed = 60f;

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

        // Follow player
        transform.position = target.position;
    }
}
