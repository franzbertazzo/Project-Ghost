using UnityEngine;

public class CursorLock : MonoBehaviour
{
    /// <summary>
    /// Set while a menu needs the pointer.
    ///
    /// Static because the thing that needs the cursor (the session menu) and the thing that takes
    /// it away (this, on the local player) have no reference to each other and are created at
    /// different times - the menu exists before any player does. Without this gate, regaining
    /// window focus with a menu open would silently re-grab the cursor and leave the buttons
    /// visible but unclickable.
    /// </summary>
    public static bool Suspended { get; set; }

    void Start()
    {
        LockCursor();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            LockCursor();
        else
            UnlockCursor();
    }

    void LockCursor()
    {
        if (Suspended)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
