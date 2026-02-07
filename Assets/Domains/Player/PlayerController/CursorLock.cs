using UnityEngine;

public class CursorLock : MonoBehaviour
{
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
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
