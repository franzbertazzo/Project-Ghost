using UnityEngine;

public class PlayerAnimationStateController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerInputHandler inputHandler;
    [SerializeField] private PlayerZeroGMovement movement;

    [Header("Animation Smoothing")]
    [SerializeField] private float smoothTimeToIdle = 0.3f;
    [SerializeField] private float smoothTimeFromIdle = 0.05f;
    [SerializeField] private float smoothTimeBetweenMoves = 0.1f;

    [Header("Dash Animation State Names")]
    [SerializeField] private string dashForward  = "DashForward";
    [SerializeField] private string dashBackward = "DashBackward";
    [SerializeField] private string dashRight    = "DashRight";
    [SerializeField] private string dashLeft     = "DashLeft";
    [SerializeField] private string dashUp       = "DashUp";
    [SerializeField] private string dashDown     = "DashDown";

    private float currentMoveDirection = 0f;
    private float velocity = 0f;

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (inputHandler == null)
            inputHandler = GetComponent<PlayerInputHandler>();
        if (movement == null)
            movement = GetComponent<PlayerZeroGMovement>();
    }

    void Update()
    {
        UpdateAnimatorParameters();

        if (inputHandler.DashPressed)
            TriggerDashAnimation();
    }

    private void UpdateAnimatorParameters()
    {
        if (animator == null || inputHandler == null)
            return;

        float targetDirection = inputHandler.MoveInput.y;

        float smoothTime;
        if (Mathf.Abs(targetDirection) < 0.1f)
            smoothTime = smoothTimeToIdle;
        else if (Mathf.Abs(currentMoveDirection) < 0.1f)
            smoothTime = smoothTimeFromIdle;
        else
            smoothTime = smoothTimeBetweenMoves;

        currentMoveDirection = Mathf.SmoothDamp(
            currentMoveDirection, targetDirection, ref velocity, smoothTime);

        animator.SetFloat("MoveDirection", currentMoveDirection);
    }

    private void TriggerDashAnimation()
    {
        if (movement == null) return;

        if (!movement.LastDashWasSurface) return;

        Vector3 localDir = transform.InverseTransformDirection(movement.LastDashDirection);
        animator.CrossFadeInFixedTime(GetDashStateName(localDir), 0.05f);
    }

    private string GetDashStateName(Vector3 localDir)
    {
        float absX = Mathf.Abs(localDir.x);
        float absY = Mathf.Abs(localDir.y);
        float absZ = Mathf.Abs(localDir.z);

        if (absZ >= absX && absZ >= absY)
            return localDir.z >= 0 ? dashForward : dashBackward;
        if (absX >= absY)
            return localDir.x >= 0 ? dashRight : dashLeft;

        return localDir.y >= 0 ? dashUp : dashDown;
    }
}