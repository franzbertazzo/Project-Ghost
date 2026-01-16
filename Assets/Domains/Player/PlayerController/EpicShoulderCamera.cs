using UnityEngine;

public class EpicShoulderCamera : MonoBehaviour
{
    public PlayerInputHandler input;

    [Header("Offsets")]
    public Vector3 rightShoulderOffset = new Vector3(0.6f, 0.1f, -4.5f);
    public Vector3 leftShoulderOffset  = new Vector3(-0.6f, 0.1f, -4.5f);

    [Header("Dynamics")]
    public float shoulderLerpSpeed = 6f;

    Vector3 currentOffset;

    void Start()
    {
        currentOffset = rightShoulderOffset;
    }

    void LateUpdate()
    {
        if (input == null)
            return;

        float horizontal = input.MoveInput.x;

        Vector3 targetOffset = currentOffset;

        if (horizontal > 0.1f)
            targetOffset = rightShoulderOffset;
        else if (horizontal < -0.1f)
            targetOffset = leftShoulderOffset;

        currentOffset = Vector3.Lerp(
            currentOffset,
            targetOffset,
            shoulderLerpSpeed * Time.deltaTime
        );

        transform.localPosition = currentOffset;
    }
}
