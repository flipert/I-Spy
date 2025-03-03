using UnityEngine;
using System;
using System.Collections;

public class AudioManager : MonoBehaviour
{
    // Singleton instance for global access
    public static AudioManager Instance;

    [Header("Music Settings")]
    [Tooltip("AudioSource used for background music")]
    public AudioSource musicSource;
    [Tooltip("Default music volume between 0 and 1")]
    [Range(0f, 1f)]
    public float musicVolume = 1f;
    
    [Header("Sound FX Settings")]
    [Tooltip("AudioSource used for sound effects")]
    public AudioSource sfxSource;
    [Tooltip("Default sound FX volume between 0 and 1")]
    [Range(0f, 1f)]
    public float sfxVolume = 1f;

    [Header("Loop Settings")]
    [Tooltip("If enabled, fire the OnMusicLoop event each time the music track completes a loop")]
    public bool fireLoopEvent = true;
    
    // Event that is fired every time the currently playing music loops
    public event Action OnMusicLoop;

    // Used to track the looping coroutine
    private Coroutine musicLoopCoroutine;

    private void Awake()
    {
        // Set up singleton instance
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // If no AudioSource assigned for music, add one
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
        }
        musicSource.playOnAwake = false;
        musicSource.volume = musicVolume;

        // For music, we set looping on. If you want a one-shot music you can override it in PlayMusic.
        musicSource.loop = true; 

        // If no AudioSource assigned for SFX, add one.
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.volume = sfxVolume;
    }

    /// <summary>
    /// Plays the specified music clip.
    /// </summary>
    /// <param name="clip">Music AudioClip</param>
    /// <param name="loop">Whether the clip should loop</param>
    /// <param name="restartIfSame">If set false, do nothing if the same clip is already playing</param>
    public void PlayMusic(AudioClip clip, bool loop = true, bool restartIfSame = false)
    {
        if (clip == null)
        {
            Debug.LogWarning("AudioManager: No music clip provided!");
            return;
        }

        // If the clip is already playing and we are not forcing a restart, do nothing
        if (musicSource.clip == clip && musicSource.isPlaying && !restartIfSame)
            return;

        // Set the new clip and properties
        musicSource.clip = clip;
        musicSource.loop = loop;
        musicSource.volume = musicVolume;
        musicSource.Play();

        // Stop any existing loop coroutine and start one if needed
        if (musicLoopCoroutine != null)
        {
            StopCoroutine(musicLoopCoroutine);
            musicLoopCoroutine = null;
        }
        if (loop && fireLoopEvent)
        {
            musicLoopCoroutine = StartCoroutine(TrackMusicLoop());
        }
    }

    /// <summary>
    /// Stops the currently playing music.
    /// </summary>
    public void StopMusic()
    {
        if (musicSource.isPlaying)
        {
            musicSource.Stop();
        }
        if (musicLoopCoroutine != null)
        {
            StopCoroutine(musicLoopCoroutine);
            musicLoopCoroutine = null;
        }
    }

    /// <summary>
    /// Sets the music volume.
    /// </summary>
    /// <param name="volume">Volume value between 0 and 1</param>
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
        }
    }

    /// <summary>
    /// Sets the sound effects volume.
    /// </summary>
    /// <param name="volume">Volume value between 0 and 1</param>
    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        if (sfxSource != null)
        {
            sfxSource.volume = sfxVolume;
        }
    }

    /// <summary>
    /// Plays a sound effect using the SFX AudioSource.
    /// </summary>
    /// <param name="clip">Sound effect AudioClip</param>
    /// <param name="volumeMultiplier">Multiplier for volume (default is 1)</param>
    public void PlaySoundFx(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (clip == null)
        {
            Debug.LogWarning("AudioManager: No sound FX clip provided!");
            return;
        }
        if (sfxSource != null)
        {
            sfxSource.PlayOneShot(clip, sfxVolume * volumeMultiplier);
        }
    }

    /// <summary>
    /// Coroutine to track when the looping music has completed a cycle.
    /// </summary>
    private IEnumerator TrackMusicLoop()
    {
        // Ensure we have a valid music clip
        if (musicSource.clip == null)
            yield break;

        float clipLength = musicSource.clip.length;

        // Wait for the clip to finish a full cycle
        yield return new WaitForSeconds(clipLength);

        // Continue to trigger the loop event while music is playing
        while (musicSource != null && musicSource.isPlaying && musicSource.loop)
        {
            if (OnMusicLoop != null)
            {
                OnMusicLoop.Invoke();
            }
            yield return new WaitForSeconds(clipLength);
        }
    }

    /// <summary>
    /// Switch to a new music track.
    /// </summary>
    /// <param name="newClip">New AudioClip to play</param>
    /// <param name="loop">Should the new track loop</param>
    public void SwitchMusic(AudioClip newClip, bool loop = true)
    {
        StopMusic();
        PlayMusic(newClip, loop);
    }
}