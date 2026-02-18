using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class HitStop : MonoBehaviour
{
    public static HitStop Instance;

    Coroutine hitStopRoutine;
    float targetNormalTimeScale = 1f;
    bool hitStopActive;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        RestoreTime();
    }

    void OnDisable()
    {
        RestoreTime();
    }

    void OnApplicationQuit()
    {
        RestoreTime();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RestoreTime();
    }

    // =========================
    // PUBLIC API
    // =========================

    public void Trigger(
        float stopDuration = 0.01f,
        float stopTimeScale = 0.05f,
        float recoverDuration = 0.12f
    )
    {
        if (hitStopRoutine != null)
            StopCoroutine(hitStopRoutine);

        targetNormalTimeScale = 1f;
        hitStopRoutine = StartCoroutine(
            HitStopRoutine(stopDuration, stopTimeScale, recoverDuration)
        );
    }

    // =========================
    // CORE
    // =========================

    IEnumerator HitStopRoutine(
        float stopDuration,
        float stopTimeScale,
        float recoverDuration
    )
    {
        hitStopActive = true;

        // HARD STOP
        Time.timeScale = stopTimeScale;

        yield return new WaitForSecondsRealtime(stopDuration);

        // SMOOTH RECOVERY
        float t = 0f;
        while (t < recoverDuration)
        {
            t += Time.unscaledDeltaTime;
            float lerp = t / recoverDuration;

            Time.timeScale = Mathf.Lerp(
                stopTimeScale,
                targetNormalTimeScale,
                lerp
            );

            yield return null;
        }

        RestoreTime();
    }

    // =========================
    // SAFETY
    // =========================

    void RestoreTime()
    {
        if (!hitStopActive)
            return;

        Time.timeScale = targetNormalTimeScale;
        hitStopActive = false;

        if (hitStopRoutine != null)
        {
            StopCoroutine(hitStopRoutine);
            hitStopRoutine = null;
        }
    }
}
