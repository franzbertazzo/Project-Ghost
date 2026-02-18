using UnityEngine;
using UnityEngine.SceneManagement;

public class RestartSceneOnKey : MonoBehaviour
{
    public void RestartScene()
    {
        SceneManager.LoadScene(
            SceneManager.GetActiveScene().buildIndex
        );
    }
}
