using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Custom/Tilt Shift")]
public class TiltShiftEffect : MonoBehaviour
{
    [Header("Tilt Shift Settings")]
    [Tooltip("The strength of the blur effect")]
    [Range(0.0f, 10.0f)]
    public float blurSize = 2.0f;
    
    [Tooltip("The vertical position of the focus area (0 = bottom, 1 = top)")]
    [Range(0.0f, 1.0f)]
    public float focusPosition = 0.5f;
    
    [Tooltip("The size of the focus area")]
    [Range(0.0f, 1.0f)]
    public float focusSize = 0.1f;
    
    [Tooltip("Controls the smoothness of transition between focused and unfocused areas (higher = smoother)")]
    [Range(0.001f, 1.0f)]
    public float feathering = 0.1f;
    
    [Tooltip("If true, the effect will be visible in the scene view")]
    public bool showInSceneView = true;
    
    // References
    private Material tiltShiftMaterial;
    private Shader tiltShiftShader;
    
    private void OnEnable()
    {
        // Find the shader
        tiltShiftShader = Shader.Find("Custom/TiltShift");
        
        if (tiltShiftShader == null)
        {
            Debug.LogError("TiltShiftEffect: Shader 'Custom/TiltShift' not found. Make sure the shader is in your project.");
            enabled = false;
            return;
        }
        
        // Create material
        if (tiltShiftMaterial == null)
        {
            tiltShiftMaterial = new Material(tiltShiftShader);
            tiltShiftMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
    }
    
    private void OnDisable()
    {
        if (tiltShiftMaterial != null)
        {
            DestroyImmediate(tiltShiftMaterial);
            tiltShiftMaterial = null;
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
        
        if (tiltShiftMaterial != null)
        {
            // Set shader properties
            tiltShiftMaterial.SetFloat("_BlurSize", blurSize);
            tiltShiftMaterial.SetFloat("_FocusPosition", focusPosition);
            tiltShiftMaterial.SetFloat("_FocusSize", focusSize);
            tiltShiftMaterial.SetFloat("_Feathering", feathering);
            
            // Apply the effect
            Graphics.Blit(source, destination, tiltShiftMaterial);
        }
        else
        {
            // Fallback if material is missing
            Graphics.Blit(source, destination);
        }
    }
}
