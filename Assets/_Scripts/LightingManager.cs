using UnityEngine;
using UnityEngine.SceneManagement;

public class LightingManager : MonoBehaviour
{
    [Header("Lighting Settings")]
    [SerializeField] private float ambientIntensity = 1.0f;
    [SerializeField] private Color ambientColor = Color.white;
    [SerializeField] private float shadowStrength = 0.5f;
    [SerializeField] private bool rebakeLightingOnLoad = false;
    
    [Header("Directional Light Settings")]
    [SerializeField] private Light directionalLight;
    [SerializeField] private float directionalLightIntensity = 1.0f;
    [SerializeField] private Color directionalLightColor = Color.white;
    
    private static LightingManager instance;
    
    private void Awake()
    {
        // Singleton pattern to ensure only one instance exists
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Register for scene loaded events
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void OnDestroy()
    {
        // Unregister from scene loaded events
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Give Unity a frame to initialize the scene
        StartCoroutine(InitializeLightingNextFrame());
    }
    
    private System.Collections.IEnumerator InitializeLightingNextFrame()
    {
        // Wait for end of frame to ensure scene is fully loaded
        yield return new WaitForEndOfFrame();
        
        // Wait one more frame to be safe
        yield return null;
        
        // Apply lighting settings
        ApplyLightingSettings();
        
        // Find directional light if not assigned
        if (directionalLight == null)
        {
            // Try to find the main directional light in the scene
            Light[] lights = FindObjectsOfType<Light>();
            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    directionalLight = light;
                    break;
                }
            }
        }
        
        // Apply directional light settings if found
        if (directionalLight != null)
        {
            directionalLight.intensity = directionalLightIntensity;
            directionalLight.color = directionalLightColor;
            directionalLight.shadowStrength = shadowStrength;
        }
        
        // Force Unity to update lighting
        DynamicGI.UpdateEnvironment();
        
        // Optionally rebake lighting (expensive operation, use with caution)
        if (rebakeLightingOnLoad)
        {
            Debug.Log("Rebaking lighting - this may cause a brief pause");
            DynamicGI.SetEmissive(gameObject.GetComponent<Renderer>(), Color.black);
            DynamicGI.UpdateEnvironment();
        }
        
        Debug.Log($"Lighting initialized for scene: {SceneManager.GetActiveScene().name}");
    }
    
    private void ApplyLightingSettings()
    {
        // Set ambient lighting
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.ambientLight = ambientColor;
        
        // Set shadow settings
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.High;
        
        // Force shadow distance to be reasonable
        QualitySettings.shadowDistance = 50f;
    }
    
    // Public method to manually update lighting settings
    public void UpdateLightingSettings()
    {
        ApplyLightingSettings();
        
        if (directionalLight != null)
        {
            directionalLight.intensity = directionalLightIntensity;
            directionalLight.color = directionalLightColor;
            directionalLight.shadowStrength = shadowStrength;
        }
        
        DynamicGI.UpdateEnvironment();
    }
}
