using UnityEngine;

[DefaultExecutionOrder(100)] // Run AFTER all other LateUpdate camera scripts
public class CameraWallAvoidance : MonoBehaviour
{
    [Header("References")]
    public Transform pivot; // Assign the ThirdPersonCameraRig transform (at player position)

    [Header("Collision Settings")]
    public float sphereRadius = 0.25f;
    public float wallBuffer = 0.15f;
    public LayerMask collisionLayers = ~0; // Set this to exclude Player layer


    [Header("Smoothing")]
    public float snapInSpeed = 50f;   // Fast when pulling in (to avoid clipping)
    public float easeOutSpeed = 8f;   // Slower when returning to desired position

    [Header("Collision Offset")]
    public float collisionNormalOffset = 0.1f; // Extra offset from wall along normal


    float currentClampedDistance = -1f;
    bool wasClamped;
    Vector3 defaultLocalPosition = new Vector3(1.233f, 0.43f, -2f);

    void LateUpdate()
    {
        if (pivot == null)
        {
            return;
        }

        Vector3 origin = pivot.position;
        Vector3 desiredLocalPosition = defaultLocalPosition;
        Vector3 desiredPosition = transform.parent != null ? transform.parent.TransformPoint(desiredLocalPosition) : transform.position;
        Vector3 toCamera = desiredPosition - origin;
        float desiredDistance = toCamera.magnitude;

        if (desiredDistance < 0.01f)
        {
            return;
        }

        Vector3 direction = toCamera / desiredDistance;
        float targetDistance = desiredDistance;

        RaycastHit hit;
        bool clamped = Physics.SphereCast(origin, sphereRadius, direction, out hit, desiredDistance, collisionLayers);
        if (clamped)
        {
            targetDistance = Mathf.Max(hit.distance - wallBuffer, 0f);
        }

        // Only clamp if wall detected
        if (targetDistance < desiredDistance - 0.01f)
        {
            if (currentClampedDistance < 0f) currentClampedDistance = desiredDistance;
            float speed = snapInSpeed;
            currentClampedDistance = Mathf.Lerp(currentClampedDistance, targetDistance, speed * Time.deltaTime);
            Vector3 clampPos = origin + direction * currentClampedDistance;
            // Add offset from collision normal if clamped
            if (clamped && collisionNormalOffset > 0f)
            {
                clampPos += hit.normal * collisionNormalOffset;
            }
            transform.position = clampPos;
            wasClamped = true;
        }
        else
        {
            // No wall: set only X and Y to default, leave Z for CameraSpeedPullback
            Vector3 localPos = transform.localPosition;
            if (localPos.x != defaultLocalPosition.x || localPos.y != defaultLocalPosition.y)
            {
                transform.localPosition = new Vector3(defaultLocalPosition.x, defaultLocalPosition.y, localPos.z);
            }
            currentClampedDistance = desiredDistance;
            wasClamped = false;
        }
    }
}