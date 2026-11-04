using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{

    public bool DashPressed { get; private set; }

    [Header("Input Values (Read-Only)")]
    public Vector2 MoveInput { get; private set; }      // WASD
    public float VerticalInput { get; private set; }    // Space (up) / Ctrl (down)
    public Vector2 LookInput { get; private set; }      // Mouse
    public float RollInput { get; private set; }        // Q/E
    public bool IsBraking { get; private set; }         // Middle mouse (radar)

    /// <summary>
    /// Writes the same values a device would, for a bot.
    ///
    /// A bot drives the real rig rather than a flight model of its own, so this is the seam: the
    /// movement script cannot tell the difference, and every bit of movement tuning done for
    /// players applies to bots for free. DashPressed is set rather than assigned, so a false here
    /// never cancels a press made this frame - LateUpdate still owns clearing it.
    /// </summary>
    public void Drive(Vector2 move, float vertical, float roll, bool braking, bool dash)
    {
        MoveInput = move;
        VerticalInput = vertical;
        RollInput = roll;
        IsBraking = braking;

        if (dash)
            DashPressed = true;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        MoveInput = context.ReadValue<Vector2>();
    }

    public void OnVertical(InputAction.CallbackContext context)
    {
        VerticalInput = context.ReadValue<float>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        LookInput = context.ReadValue<Vector2>();
    }

    public void OnRoll(InputAction.CallbackContext context)
    {
        RollInput = context.ReadValue<float>();
    }

    public void OnBrake(InputAction.CallbackContext context)
    {
        IsBraking = context.ReadValueAsButton();
    }

    // ---- DASH (ONE-SHOT) ----
    public void OnDash(InputAction.CallbackContext context)
    {
        if (context.performed)
            DashPressed = true;
    }

    void LateUpdate()
    {
        // Reset one-frame inputs
        DashPressed = false;
    }
}
