using UnityEngine;

public class PlayerAnimationStateController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerInputHandler inputHandler;

    [Header("Animation Smoothing")]
    [SerializeField] private float smoothTimeToIdle = 0.3f;      // Slower when releasing input
    [SerializeField] private float smoothTimeFromIdle = 0.05f;   // Fast when starting to move
    [SerializeField] private float smoothTimeBetweenMoves = 0.1f; // Medium when changing direction

    private float currentMoveDirection = 0f;
    private float velocity = 0f;

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        
        if (inputHandler == null)
            inputHandler = GetComponent<PlayerInputHandler>();
    }

    void Update()
    {
        UpdateAnimatorParameters();
    }

    private void UpdateAnimatorParameters()
    {
        if (animator == null || inputHandler == null)
            return;

        float targetDirection = inputHandler.MoveInput.y;

        // Choose smooth time based on transition type
        float smoothTime;
        
        if (Mathf.Abs(targetDirection) < 0.1f)
        {
            // Going TO idle (releasing input) - use slower smooth time
            smoothTime = smoothTimeToIdle;
        }
        else if (Mathf.Abs(currentMoveDirection) < 0.1f)
        {
            // Going FROM idle (starting to move) - use fast smooth time
            smoothTime = smoothTimeFromIdle;
        }
        else
        {
            // Changing between forward/backward - medium smooth time
            smoothTime = smoothTimeBetweenMoves;
        }

        // Smoothly interpolate
        currentMoveDirection = Mathf.SmoothDamp(
            currentMoveDirection, 
            targetDirection, 
            ref velocity, 
            smoothTime
        );

        animator.SetFloat("MoveDirection", currentMoveDirection);
    }
}