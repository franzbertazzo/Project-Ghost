using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class GlobalInputHandler : MonoBehaviour
{
    public static GlobalInputHandler Instance;

    PlayerInputActions input;

    void Awake()
    {
        // Singleton (simple & safe)
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        input = new PlayerInputActions();
        input.Enable();

        input.Global.Restart.performed += OnRestart;
    }

    void OnDestroy()
    {
        if (input != null)
            input.Global.Restart.performed -= OnRestart;
    }

    void OnRestart(InputAction.CallbackContext ctx)
    {
        SceneManager.LoadScene(
            SceneManager.GetActiveScene().buildIndex
        );
    }
}
