using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerZeroGMovement : MonoBehaviour
{
    [Header("References")]
    public PlayerInputHandler inputHandler;

    [Header("Movement Settings")]
    public float thrustPower = 10f;
    public float maxSpeed = 6f;
    public float brakeForce = 5f;

    [Header("Rotation Settings")]
    public float mouseSensitivity = 2f;
    public float rollSpeed = 60f; // degrees per second

    [Header("Noise Settings")]
    public float noiseIntensity = 1f;

    private Rigidbody rb;
    private NoiseEmitter noiseEmitter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;

        if (inputHandler == null)
            inputHandler = GetComponent<PlayerInputHandler>();

        noiseEmitter = GetComponent<NoiseEmitter>();

        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        ApplyRotation();
    }

    private void FixedUpdate()
    {
        ApplyMovement();
    }

    private void OnCollisionEnter(Collision collision)
    {
        EmitNoise();
    }

    private void EmitNoise()
    {
        if (noiseEmitter != null)
        {
            // 1 = normal, >1 = loud, <1 = quiet
            noiseEmitter.EmitNoise(noiseIntensity);
        }
    }

    private void ApplyRotation()
    {
        Vector2 look = inputHandler.LookInput;
        float yaw = look.x * mouseSensitivity;
        float pitch = -look.y * mouseSensitivity;

        transform.Rotate(pitch, yaw, 0f, Space.Self);

        if (Mathf.Abs(inputHandler.RollInput) > 0.01f)
            transform.Rotate(Vector3.forward, -inputHandler.RollInput * rollSpeed * Time.deltaTime, Space.Self);
    }

    private void ApplyMovement()
    {
        Vector3 inputVector = new Vector3(inputHandler.MoveInput.x, inputHandler.VerticalInput, inputHandler.MoveInput.y);
        Vector3 thrustDirection = transform.TransformDirection(inputVector);

        Vector3 desiredVelocity = rb.linearVelocity + thrustDirection * thrustPower * Time.fixedDeltaTime;

        if (desiredVelocity.magnitude > maxSpeed)
            desiredVelocity = desiredVelocity.normalized * maxSpeed;

        rb.linearVelocity = desiredVelocity;

        if (inputHandler.IsBraking)
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, brakeForce * Time.fixedDeltaTime);
    }
}
