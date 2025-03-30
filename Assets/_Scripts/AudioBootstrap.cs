using UnityEngine;

/// <summary>
/// This script ensures proper initialization of audio systems
/// and prevents FMOD errors related to system initialization
/// </summary>
public class AudioBootstrap : MonoBehaviour
{
    private static bool isInitialized = false;
    
    private void Awake()
    {
        if (!isInitialized)
        {
            // Set the flag first to prevent multiple initialization attempts
            isInitialized = true;
            
            Debug.Log("Initializing audio systems...");
            
            // Ensure there's only one AudioListener in the scene
            AudioListener[] listeners = FindObjectsOfType<AudioListener>();
            if (listeners.Length > 1)
            {
                Debug.LogWarning($"Found {listeners.Length} AudioListeners in the scene. Only one is needed.");
                for (int i = 1; i < listeners.Length; i++)
                {
                    Debug.Log($"Disabling extra AudioListener on {listeners[i].gameObject.name}");
                    listeners[i].enabled = false;
                }
            }
            
            // Make sure this object persists across scenes
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // If we already have an instance, destroy this one
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// Sets up audio for the entire game
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeAudio()
    {
        // Create a persistent audio bootstrap object if it doesn't exist
        if (!isInitialized)
        {
            GameObject bootstrapper = new GameObject("AudioBootstrap");
            bootstrapper.AddComponent<AudioBootstrap>();
            Debug.Log("Audio bootstrap created");
        }
    }
} 