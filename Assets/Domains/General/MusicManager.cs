using UnityEngine;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    public AudioClip[] tracks;
    [Range(0f, 1f)] public float volume = 0.5f;
    public bool playOnStart = true;
    public bool loop = true;

    private AudioSource audioSource;
    private int currentTrackIndex = -1;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.loop = loop;
        audioSource.volume = volume;
    }

    void Start()
    {
        if (playOnStart && tracks.Length > 0)
        {
            Play(0);
        }
    }

    public void Play(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= tracks.Length)
        {
            return;
        }

        currentTrackIndex = trackIndex;
        audioSource.clip = tracks[currentTrackIndex];
        audioSource.Play();
    }

    public void PlayRandom()
    {
        if (tracks.Length == 0)
        {
            return;
        }

        Play(Random.Range(0, tracks.Length));
    }

    public void Stop()
    {
        audioSource.Stop();
    }

    public void Pause()
    {
        audioSource.Pause();
    }

    public void Resume()
    {
        audioSource.UnPause();
    }

    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        audioSource.volume = volume;
    }

    public void NextTrack()
    {
        if (tracks.Length == 0)
        {
            return;
        }

        Play((currentTrackIndex + 1) % tracks.Length);
    }
}
