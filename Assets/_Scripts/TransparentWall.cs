using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Renderer))]
public class TransparentWall : MonoBehaviour
{
    [Header("Transparency Settings")]
    [Tooltip("The opacity percentage when player is behind the wall (0-100)")]
    [Range(0, 100)]
    public float transparentOpacity = 30f;

    [Tooltip("The normal opacity percentage (0-100)")]
    [Range(0, 100)]
    public float normalOpacity = 100f;

    [Tooltip("How quickly the transparency changes")]
    public float transitionSpeed = 5f;

    [Header("Detection Settings")]
    [Tooltip("The player's transform to detect")]
    public Transform player;

    [Tooltip("Additional detection radius around the player")]
    public float detectionRadius = 0.5f;

    [Tooltip("If true, will automatically find the local player")]
    public bool autoFindLocalPlayer = true;

    // Private variables
    private Renderer wallRenderer;
    private Material wallMaterial;
    private Color originalColor;
    private float currentOpacity;
    private bool playerWasBehind = false;

    private void Start()
    {
        // Get the renderer component
        wallRenderer = GetComponent<Renderer>();
        
        // Make sure we have a material that can be transparent
        wallMaterial = wallRenderer.material;
        
        // Store the original color
        originalColor = wallMaterial.color;
        currentOpacity = originalColor.a;
        
        // Make sure the shader supports transparency
        SetupMaterialForTransparency();
        
        // If no player is set and auto-find is enabled, try to find the local player
        if (player == null && autoFindLocalPlayer)
        {
            StartCoroutine(FindLocalPlayerDelayed());
        }
    }

    private IEnumerator FindLocalPlayerDelayed()
    {
        // Wait a bit longer to ensure network players are spawned
        yield return new WaitForSeconds(0.5f);
        
        // Find all player controllers
        var players = GameObject.FindObjectsOfType<PlayerController>();
        foreach (var playerController in players)
        {
            // Check if this is the local player
            if (playerController.IsOwner)
            {
                player = playerController.transform;
                Debug.Log("TransparentWall: Automatically found local player");
                break;
            }
        }
        
        if (player == null)
        {
            Debug.LogWarning("TransparentWall: Could not find local player");
        }
    }

    private void Update()
    {
        if (player == null)
            return;

        bool isPlayerBehind = IsPlayerBehindWall();
        
        // Only update opacity if the player's position relative to wall has changed
        if (isPlayerBehind != playerWasBehind || !Mathf.Approximately(currentOpacity, GetTargetOpacity(isPlayerBehind)))
        {
            UpdateOpacity(isPlayerBehind);
        }
        
        playerWasBehind = isPlayerBehind;
    }

    private void SetupMaterialForTransparency()
    {
        // Check if the material's shader supports transparency
        string shaderName = wallMaterial.shader.name.ToLower();
        
        // If using standard shader but not in transparent mode
        if (shaderName.Contains("standard") && !shaderName.Contains("transparent"))
        {
            // Create a new material instance to avoid affecting other objects
            wallMaterial = new Material(wallMaterial);
            wallMaterial.shader = Shader.Find("Standard");
            wallMaterial.SetFloat("_Mode", 3); // Transparent mode
            wallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            wallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            wallMaterial.SetInt("_ZWrite", 0);
            wallMaterial.DisableKeyword("_ALPHATEST_ON");
            wallMaterial.EnableKeyword("_ALPHABLEND_ON");
            wallMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            wallMaterial.renderQueue = 3000;
            
            // Apply the new material to the renderer
            wallRenderer.material = wallMaterial;
        }
    }

    private bool IsPlayerBehindWall()
    {
        if (Camera.main == null || player == null)
            return false;
        
        // Get the direction from the wall to the player
        Vector3 wallToPlayerDir = player.position - transform.position;
        
        // Get the direction from the wall to the camera
        Vector3 wallToCameraDir = Camera.main.transform.position - transform.position;
        
        // Check if the player and camera are on opposite sides of the wall
        // by comparing the dot products with the wall's normal (forward direction)
        float playerDot = Vector3.Dot(transform.forward, wallToPlayerDir);
        float cameraDot = Vector3.Dot(transform.forward, wallToCameraDir);
        
        // If the signs are different, they're on opposite sides of the wall
        bool onOppositeSides = (playerDot * cameraDot) < 0;
        
        // Check if the player is within the bounds of the wall (projected onto the wall's plane)
        Bounds wallBounds = wallRenderer.bounds;
        Vector3 playerPosOnWallPlane = Vector3.ProjectOnPlane(player.position - transform.position, transform.forward) + transform.position;
        
        // Expand bounds by detection radius
        Bounds expandedBounds = new Bounds(wallBounds.center, wallBounds.size + new Vector3(detectionRadius * 2, detectionRadius * 2, detectionRadius * 2));
        
        bool isWithinWallBounds = expandedBounds.Contains(playerPosOnWallPlane);
        
        // Player is "behind" the wall (from camera's perspective) if:
        // 1. Player and camera are on opposite sides of the wall
        // 2. Player is on the opposite side from the camera (player dot is opposite sign to camera dot)
        // 3. Player is within the projected bounds of the wall
        return onOppositeSides && playerDot * cameraDot < 0 && isWithinWallBounds;
    }

    private void UpdateOpacity(bool isPlayerBehind)
    {
        // Calculate target opacity
        float targetOpacity = GetTargetOpacity(isPlayerBehind);
        
        // Smoothly transition to target opacity
        currentOpacity = Mathf.Lerp(currentOpacity, targetOpacity, Time.deltaTime * transitionSpeed);
        
        // Apply the new opacity
        Color newColor = originalColor;
        newColor.a = currentOpacity;
        wallMaterial.color = newColor;
    }

    private float GetTargetOpacity(bool isPlayerBehind)
    {
        return isPlayerBehind ? (transparentOpacity / 100f) : (normalOpacity / 100f);
    }

    // Reset material when script is disabled or destroyed
    private void OnDisable()
    {
        if (wallRenderer != null && originalColor != null)
        {
            Color resetColor = originalColor;
            resetColor.a = normalOpacity / 100f;
            wallRenderer.material.color = resetColor;
        }
    }

    // Draw gizmos to visualize the detection area
    private void OnDrawGizmosSelected()
    {
        if (wallRenderer == null)
            wallRenderer = GetComponent<Renderer>();
            
        if (wallRenderer != null)
        {
            Bounds wallBounds = wallRenderer.bounds;
            Bounds expandedBounds = new Bounds(wallBounds.center, wallBounds.size + new Vector3(detectionRadius * 2, detectionRadius * 2, detectionRadius * 2));
            
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(expandedBounds.center, expandedBounds.size);
            
            if (player != null)
            {
                Gizmos.color = IsPlayerBehindWall() ? Color.red : Color.green;
                Gizmos.DrawLine(transform.position, player.position);
            }
        }
    }
} 