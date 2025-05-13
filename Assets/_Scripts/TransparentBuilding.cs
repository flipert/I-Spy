using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class TransparentBuilding : MonoBehaviour
{
    [Header("Transparency Settings")]
    [Tooltip("The opacity percentage when camera is inside the building (0-100)")]
    [Range(0, 100)]
    public float transparentOpacity = 30f;

    [Tooltip("The normal opacity percentage (0-100)")]
    [Range(0, 100)]
    public float normalOpacity = 100f;

    [Tooltip("How quickly the transparency changes")]
    public float transitionSpeed = 5f;

    [Header("Detection Settings")]
    [Tooltip("Distance from camera to start fading")]
    public float fadeDistance = 2.0f;

    [Tooltip("If true, will automatically find the main camera")]
    public bool autoFindCamera = true;

    [Header("Advanced Settings")]
    [Tooltip("Renderer components to make transparent (leave empty to auto-find all child renderers)")]
    public List<Renderer> buildingRenderers = new List<Renderer>();

    // Private variables
    private Camera mainCamera;
    private List<Material> originalMaterials = new List<Material>();
    private List<Material> transparentMaterials = new List<Material>();
    private float currentOpacity;
    private bool isCameraInside = false;
    private Collider buildingCollider;
    private bool isInitialized = false;

    private void Start()
    {
        // Get the collider component
        buildingCollider = GetComponent<Collider>();
        
        // Find all renderers if not specified
        if (buildingRenderers.Count == 0)
        {
            buildingRenderers.AddRange(GetComponentsInChildren<Renderer>());
        }
        
        // Setup materials for transparency
        SetupMaterialsForTransparency();
        
        // If auto-find camera is enabled, try to find the main camera
        if (autoFindCamera)
        {
            StartCoroutine(FindCameraDelayed());
        }
    }

    private IEnumerator FindCameraDelayed()
    {
        // Wait a bit to ensure camera is set up
        yield return new WaitForSeconds(1.0f);
        
        // Find the main camera
        mainCamera = Camera.main;
        
        if (mainCamera == null)
        {
            Debug.LogWarning("TransparentBuilding: Could not find main camera");
            
            // Try to find any camera in the scene
            Camera[] cameras = FindObjectsOfType<Camera>();
            if (cameras.Length > 0)
            {
                mainCamera = cameras[0];
                Debug.Log("TransparentBuilding: Using alternative camera: " + mainCamera.name);
            }
        }
        else
        {
            Debug.Log("TransparentBuilding: Found main camera");
        }
        
        isInitialized = true;
    }

    private void SetupMaterialsForTransparency()
    {
        originalMaterials.Clear();
        transparentMaterials.Clear();
        
        foreach (Renderer renderer in buildingRenderers)
        {
            Material[] materials = renderer.materials;
            
            for (int i = 0; i < materials.Length; i++)
            {
                Material originalMat = materials[i];
                originalMaterials.Add(originalMat);
                
                // Create a new material instance to avoid affecting other objects
                Material transparentMat = new Material(originalMat);
                
                // Setup for transparency
                string shaderName = transparentMat.shader.name.ToLower();
                
                // If using standard shader but not in transparent mode
                if (shaderName.Contains("standard") && !shaderName.Contains("transparent"))
                {
                    transparentMat.shader = Shader.Find("Standard");
                    transparentMat.SetFloat("_Mode", 3); // Transparent mode
                    transparentMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    transparentMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    transparentMat.SetInt("_ZWrite", 0);
                    transparentMat.DisableKeyword("_ALPHATEST_ON");
                    transparentMat.EnableKeyword("_ALPHABLEND_ON");
                    transparentMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    transparentMat.renderQueue = 3000;
                }
                
                // Store the original color
                Color originalColor = transparentMat.color;
                currentOpacity = originalColor.a;
                
                transparentMaterials.Add(transparentMat);
            }
        }
        
        // Initialize with normal opacity
        SetOpacity(normalOpacity / 100f);
    }

    private void Update()
    {
        if (!isInitialized || mainCamera == null)
            return;

        // Check if camera is inside the building or close to it
        bool shouldBeTransparent = IsCameraInsideOrNear();
        
        // Only update opacity if the camera's position relative to building has changed
        if (shouldBeTransparent != isCameraInside || !Mathf.Approximately(currentOpacity, GetTargetOpacity(shouldBeTransparent)))
        {
            UpdateOpacity(shouldBeTransparent);
        }
        
        isCameraInside = shouldBeTransparent;
    }

    private bool IsCameraInsideOrNear()
    {
        if (mainCamera == null || buildingCollider == null)
            return false;
        
        Vector3 cameraPosition = mainCamera.transform.position;
        
        // Check if camera is inside the collider
        bool isInside = buildingCollider.bounds.Contains(cameraPosition);
        
        // If not inside, check if it's close enough to start fading
        if (!isInside)
        {
            // Find the closest point on the collider to the camera
            Vector3 closestPoint = buildingCollider.ClosestPoint(cameraPosition);
            float distance = Vector3.Distance(cameraPosition, closestPoint);
            
            // If within fade distance, consider it "near"
            if (distance < fadeDistance)
            {
                return true;
            }
        }
        
        return isInside;
    }

    private void UpdateOpacity(bool makeTransparent)
    {
        // Calculate target opacity
        float targetOpacity = GetTargetOpacity(makeTransparent);
        
        // Smoothly transition to target opacity
        currentOpacity = Mathf.Lerp(currentOpacity, targetOpacity, Time.deltaTime * transitionSpeed);
        
        // Apply the new opacity to all materials
        SetOpacity(currentOpacity);
    }

    private float GetTargetOpacity(bool makeTransparent)
    {
        return makeTransparent ? (transparentOpacity / 100f) : (normalOpacity / 100f);
    }

    private void SetOpacity(float opacity)
    {
        for (int i = 0; i < buildingRenderers.Count; i++)
        {
            Renderer renderer = buildingRenderers[i];
            Material[] currentMaterials = renderer.materials;
            
            for (int j = 0; j < currentMaterials.Length; j++)
            {
                Material mat = transparentMaterials[i * currentMaterials.Length + j];
                Color color = mat.color;
                color.a = opacity;
                mat.color = color;
                
                // Apply the transparent material
                currentMaterials[j] = mat;
            }
            
            renderer.materials = currentMaterials;
        }
    }

    // Reset materials when script is disabled or destroyed
    private void OnDisable()
    {
        ResetMaterials();
    }

    private void OnDestroy()
    {
        ResetMaterials();
    }

    private void ResetMaterials()
    {
        for (int i = 0; i < buildingRenderers.Count; i++)
        {
            Renderer renderer = buildingRenderers[i];
            if (renderer != null)
            {
                Material[] currentMaterials = renderer.materials;
                
                for (int j = 0; j < currentMaterials.Length && j < originalMaterials.Count; j++)
                {
                    int index = i * currentMaterials.Length + j;
                    if (index < originalMaterials.Count)
                    {
                        currentMaterials[j] = originalMaterials[index];
                    }
                }
                
                renderer.materials = currentMaterials;
            }
        }
    }

    // Draw gizmos to visualize the detection area
    private void OnDrawGizmosSelected()
    {
        if (buildingCollider == null)
            buildingCollider = GetComponent<Collider>();
            
        if (buildingCollider != null)
        {
            // Draw the collider bounds
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(buildingCollider.bounds.center, buildingCollider.bounds.size);
            
            // Draw the fade distance
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, fadeDistance);
        }
    }
}
