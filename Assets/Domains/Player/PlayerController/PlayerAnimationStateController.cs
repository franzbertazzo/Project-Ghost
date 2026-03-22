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

    [Header("Surface IK")]
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform rightFoot;
    [SerializeField] private float ikWeight = 1f;
    [SerializeField] private float ikRaycastDistance = 0.5f;
    [SerializeField] private LayerMask surfaceMask;

    private float currentMoveDirection = 0f;
    private float velocity = 0f;

    // IK state
    private Vector3 leftFootIKPos, rightFootIKPos;
    private Quaternion leftFootIKRot, rightFootIKRot;
    private float currentIKWeight = 0f;

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

        // Trigger dash animation on the frame DashPressed is true
        if (inputHandler.DashPressed)
            TriggerDashAnimation();

        // Fade IK weight out when not dashing
        float ikTarget = (movement.IsDashing && movement.LastDashWasSurface) ? 1f : 0f;
        currentIKWeight = Mathf.MoveTowards(currentIKWeight, ikTarget, Time.deltaTime * 5f);
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

        // Convert world dash direction to player local space
        Vector3 localDir = transform.InverseTransformDirection(movement.LastDashDirection);

        string stateName = GetDashStateName(localDir);
        animator.CrossFade(stateName, 0.05f);

        // If surface dash, compute IK foot positions
        if (movement.LastDashWasSurface)
            ComputeFootIKPositions(movement.LastSurfaceNormal);
    }

    private string GetDashStateName(Vector3 localDir)
    {
        // Find which of the 6 axes the direction is closest to
        float absX = Mathf.Abs(localDir.x);
        float absY = Mathf.Abs(localDir.y);
        float absZ = Mathf.Abs(localDir.z);

        if (absZ >= absX && absZ >= absY)
            return localDir.z >= 0 ? dashForward : dashBackward;
        if (absX >= absY)
            return localDir.x >= 0 ? dashRight : dashLeft;

        return localDir.y >= 0 ? dashUp : dashDown;
    }

    private void ComputeFootIKPositions(Vector3 surfaceNormal)
    {
        if (leftFoot != null)
            SolveFootIK(leftFoot.position, surfaceNormal, out leftFootIKPos, out leftFootIKRot);

        if (rightFoot != null)
            SolveFootIK(rightFoot.position, surfaceNormal, out rightFootIKPos, out rightFootIKRot);
    }

    private void SolveFootIK(Vector3 footPos, Vector3 surfaceNormal, 
        out Vector3 ikPos, out Quaternion ikRot)
    {
        // Cast from foot along the surface normal to find exact contact point
        if (Physics.Raycast(footPos, -surfaceNormal, out RaycastHit hit, 
            ikRaycastDistance, surfaceMask))
        {
            ikPos = hit.point;
            ikRot = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(transform.forward, hit.normal), 
                hit.normal);
        }
        else
        {
            ikPos = footPos;
            ikRot = Quaternion.identity;
        }
    }

    // Called by Unity's animation system when IK Pass is enabled on the layer
    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || currentIKWeight <= 0f) return;

        animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, currentIKWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, currentIKWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftFoot, leftFootIKPos);
        animator.SetIKRotation(AvatarIKGoal.LeftFoot, leftFootIKRot);

        animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, currentIKWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, currentIKWeight);
        animator.SetIKPosition(AvatarIKGoal.RightFoot, rightFootIKPos);
        animator.SetIKRotation(AvatarIKGoal.RightFoot, rightFootIKRot);
    }
}