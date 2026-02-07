using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{

    public bool DashPressed { get; private set; }

    [Header("Input Values (Read-Only)")]
    public Vector2 MoveInput { get; private set; }      // WASD
    public float VerticalInput { get; private set; }    // Space/Ctrl
    public Vector2 LookInput { get; private set; }      // Mouse
    public float RollInput { get; private set; }        // Q/E
    public bool IsBraking { get; private set; }         // Shift

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
