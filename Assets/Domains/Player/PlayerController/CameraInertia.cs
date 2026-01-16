using UnityEngine;

public class CameraInertia : MonoBehaviour
{
    public float positionLag = 12f;
    public float rotationLag = 10f;

    Vector3 velocity;
    Quaternion rotVelocity;

    Transform target;

    void Start()
    {
        target = transform; // self-reference
    }

    void LateUpdate()
    {
        // Smooth position (soft follow)
        transform.position = Vector3.SmoothDamp(
            transform.position,
            target.position,
            ref velocity,
            1f / positionLag
        );

        // Smooth rotation (weight)
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            target.rotation,
            rotationLag * Time.deltaTime
        );
    }
}
