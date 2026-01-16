using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerZeroGMovement : MonoBehaviour
{
    [Header("References")]
    public PlayerInputHandler inputHandler;
    public Transform cameraRig; // Assign CameraPivot or CameraRig transform

    [Header("Movement Settings")]
    public float thrustPower = 10f;
    public float maxSpeed = 6f;
    public float brakeForce = 5f;

    [Header("Rotation Settings")]
    public float alignmentSpeed = 10f; // How fast player aligns to camera

    [Header("Noise Settings")]
    public float noiseIntensity = 1f;

    private Rigidbody rb;
    private NoiseEmitter noiseEmitter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 0.2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (inputHandler == null)
            inputHandler = GetComponent<PlayerInputHandler>();

        noiseEmitter = GetComponent<NoiseEmitter>();
    }

    private void FixedUpdate()
    {
        ApplyMovement();
        AlignWithCamera();
    }

    private void OnCollisionEnter(Collision collision)
    {
        EmitNoise();
    }

    // -----------------------
    // Movement (camera-relative)
    // -----------------------
    private void ApplyMovement()
    {
        if (cameraRig == null || inputHandler == null)
            return;

        Vector3 inputVector = new Vector3(
            inputHandler.MoveInput.x,
            inputHandler.VerticalInput,
            inputHandler.MoveInput.y
        );

        Vector3 thrustDirection =
            cameraRig.right   * inputVector.x +
            cameraRig.up      * inputVector.y +
            cameraRig.forward * inputVector.z;

        Vector3 desiredVelocity =
            rb.linearVelocity + thrustDirection * thrustPower * Time.fixedDeltaTime;

        if (desiredVelocity.magnitude > maxSpeed)
            desiredVelocity = desiredVelocity.normalized * maxSpeed;

        rb.linearVelocity = desiredVelocity;

        if (inputHandler.IsBraking)
        {
            rb.linearVelocity = Vector3.Lerp(
                rb.linearVelocity,
                Vector3.zero,
                brakeForce * Time.fixedDeltaTime
            );
        }
    }

    // -----------------------
    // Rotation (camera authority)
    // -----------------------
    private void AlignWithCamera()
    {
        if (cameraRig == null)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(cameraRig.forward, cameraRig.up);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                alignmentSpeed * Time.fixedDeltaTime
            )
        );
    }

    // -----------------------
    // Noise
    // -----------------------
    private void EmitNoise()
    {
        if (noiseEmitter != null)
            noiseEmitter.EmitNoise(noiseIntensity);
    }
}
