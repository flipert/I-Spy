using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Custom/Color Harmony")]
public class ColorHarmonyEffect : MonoBehaviour
{
    [Header("Color Harmony")]
    [Tooltip("Type of color harmony to apply")]
    [Range(0, 4)]
    public float harmonyType = 0; // 0=Analogous, 1=Complementary, 2=Triadic, 3=Tetradic, 4=Monochromatic
    
    [Tooltip("Base hue for the color harmony (0-1)")]
    [Range(0, 1)]
    public float baseHue = 0.0f;
    
    [Tooltip("Shift the base hue and all harmony colors")]
    [Range(-0.5f, 0.5f)]
    public float hueShift = 0.0f;
    
    [Tooltip("Overall saturation multiplier")]
    [Range(0, 2)]
    public float saturation = 1.0f;
    
    [Tooltip("How strongly to pull colors towards the harmony colors (0=off, 1=maximum)")]
    [Range(0, 1)]
    public float saturationBalance = 0.5f;
    
    [Tooltip("Overall brightness multiplier")]
    [Range(0, 2)]
    public float brightness = 1.0f;
    
    [Tooltip("Contrast adjustment")]
    [Range(0, 2)]
    public float contrast = 1.0f;
    
    [Header("Color Grading")]
    [Tooltip("Intelligent saturation that affects less saturated colors more")]
    [Range(-1, 1)]
    public float vibrance = 0.0f;
    
    [Tooltip("Blue-Orange color temperature shift")]
    [Range(-1, 1)]
    public float colorTemperature = 0.0f;
    
    [Tooltip("Green-Magenta tint adjustment")]
    [Range(-1, 1)]
    public float tint = 0.0f;
    
    [Header("Preview")]
    [Tooltip("If true, the effect will be visible in the scene view")]
    public bool showInSceneView = true;
    
    [Tooltip("Display a color wheel preview in the corner")]
    public bool showColorWheel = true;
    
    [Tooltip("Size of the color wheel preview")]
    [Range(0.1f, 0.3f)]
    public float colorWheelSize = 0.15f;
    
    // References
    private Material colorHarmonyMaterial;
    private Shader colorHarmonyShader;
    
    // Harmony names for the inspector
    private readonly string[] harmonyNames = {
        "Analogous", "Complementary", "Triadic", "Tetradic", "Monochromatic"
    };
    
    private void OnEnable()
    {
        // Find the shader
        colorHarmonyShader = Shader.Find("Custom/ColorHarmony");
        
        if (colorHarmonyShader == null)
        {
            Debug.LogError("ColorHarmonyEffect: Shader 'Custom/ColorHarmony' not found. Make sure the shader is in your project.");
            enabled = false;
            return;
        }
        
        // Create material
        if (colorHarmonyMaterial == null)
        {
            colorHarmonyMaterial = new Material(colorHarmonyShader);
            colorHarmonyMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
    }
    
    private void OnDisable()
    {
        if (colorHarmonyMaterial != null)
        {
            DestroyImmediate(colorHarmonyMaterial);
            colorHarmonyMaterial = null;
        }
    }
    
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Skip effect in scene view if not wanted
        if (!showInSceneView && Camera.current != Camera.main)
        {
            Graphics.Blit(source, destination);
            return;
        }
        
        if (colorHarmonyMaterial != null)
        {
            // Set shader properties
            colorHarmonyMaterial.SetFloat("_HarmonyType", harmonyType);
            colorHarmonyMaterial.SetFloat("_BaseHue", baseHue);
            colorHarmonyMaterial.SetFloat("_HueShift", hueShift);
            colorHarmonyMaterial.SetFloat("_Saturation", saturation);
            colorHarmonyMaterial.SetFloat("_SaturationBalance", saturationBalance);
            colorHarmonyMaterial.SetFloat("_Brightness", brightness);
            colorHarmonyMaterial.SetFloat("_Contrast", contrast);
            colorHarmonyMaterial.SetFloat("_Vibrance", vibrance);
            colorHarmonyMaterial.SetFloat("_ColorTemperature", colorTemperature);
            colorHarmonyMaterial.SetFloat("_Tint", tint);
            
            // Apply the effect
            Graphics.Blit(source, destination, colorHarmonyMaterial);
        }
        else
        {
            // Fallback if material is missing
            Graphics.Blit(source, destination);
        }
    }
    
    // Custom inspector display for the harmony type
    private void OnValidate()
    {
        // Round the harmony type to the nearest integer
        harmonyType = Mathf.Round(harmonyType);
    }
}
