using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("The target to follow")]
    public Transform target;
    
    [Tooltip("Offset from the target position")]
    public Vector3 offset = new Vector3(0f, 5f, -10f);
    
    [Header("Follow Settings")]
    [Tooltip("How quickly the camera follows the target")]
    [Range(0.01f, 1f)]
    public float smoothSpeed = 0.125f;
    
    [Tooltip("If true, camera will look at the target")]
    public bool lookAtTarget = true;
    
    [Tooltip("Follow style to use")]
    public FollowStyle followStyle = FollowStyle.SmoothFollow;
    
    [Header("Advanced Settings")]
    [Tooltip("Damping for position changes")]
    public float positionDamping = 1f;
    
    [Tooltip("Damping for rotation changes")]
    public float rotationDamping = 1f;
    
    [Tooltip("If true, camera will maintain a fixed height")]
    public bool fixedHeight = true;
    
    [Tooltip("Height to maintain if fixed height is enabled")]
    public float fixedHeightValue = 10f;
    
    [Tooltip("If true, camera will only follow on X and Z axes")]
    public bool topDownFollow = true;
    
    [Header("Rotation Settings")]
    [Tooltip("Rotation angle in degrees when pressing E or Q")]
    public float rotationAngle = 45f;
    
    [Tooltip("How quickly the camera rotates to the new angle")]
    public float rotationSpeed = 5f;
    
    [Header("Cinematic Zoom Settings")]
    [Tooltip("Orthographic size when zoomed in for cinematic effect.")]
    public float zoomedInOrthographicSize = 3f;
    [Tooltip("How quickly the camera zooms in and out.")]
    public float zoomSpeed = 2f;
    
    // Current velocity for SmoothDamp
    private Vector3 currentVelocity = Vector3.zero;
    
    // Current and target rotation angles
    private float currentRotationAngle = 0f;
    private float targetRotationAngle = 0f;
    
    private Camera cameraComponent;
    private float initialOrthographicSize;
    private float targetOrthographicSize;
    
    // Enum for different follow styles
    public enum FollowStyle
    {
        Instant,           // Camera instantly moves to target
        SmoothFollow,      // Camera smoothly follows with Lerp
        SmoothDamp,        // Camera follows with SmoothDamp (more natural)
        FixedOffset        // Camera maintains exact offset from target
    }
    
    private bool isSearchingForPlayer = false;
    private int retryCount = 0;
    private float retryInterval = 0.5f;
    private int maxRetries = 5;
    
    private void Start()
    {
        Debug.Log("CameraFollow: Start");
        
        cameraComponent = GetComponent<Camera>();
        if (cameraComponent == null)
        {
            Debug.LogError("CameraFollow: No Camera component found on this GameObject! Zoom will not work.", this);
        }
        else if (!cameraComponent.orthographic)
        {
            Debug.LogWarning("CameraFollow: Camera is not orthographic. Cinematic zoom might not work as intended.", this);
            initialOrthographicSize = cameraComponent.orthographicSize;
        }
        else
        {
            initialOrthographicSize = cameraComponent.orthographicSize;
            Debug.Log($"CameraFollow: Initial orthographic size: {initialOrthographicSize}");
        }
        targetOrthographicSize = initialOrthographicSize;
        
        // If no target is set, try to find the local player
        if (target == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                target = playerObj.transform;
                Debug.Log($"CameraFollow: Found player at {target.position}");
            }
            else
            {
                Debug.Log("CameraFollow: No player found with tag 'Player', will try to find local player");
                StartCoroutine(FindLocalPlayerWithRetryOld());
            }
        }
        
        // Start looking for the player with retries
        FindLocalPlayerWithRetry();
        
        // Initialize camera position
        if (target != null && followStyle == FollowStyle.Instant)
        {
            UpdateCameraPosition(1f);
        }
    }
    
    private IEnumerator FindLocalPlayerWithRetryOld()
    {
        if (isSearchingForPlayer)
            yield break;
            
        isSearchingForPlayer = true;
        retryCount = 0;
        
        Debug.Log("CameraFollow: Starting to look for local player");
        
        // Wait for NetworkManager to be ready
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            Debug.Log("CameraFollow: Waiting for NetworkManager to be ready...");
            yield return new WaitForSeconds(0.5f);
        }
        
        // Wait a bit longer to ensure players are spawned
        yield return new WaitForSeconds(1.0f);
        
        while (target == null && retryCount < maxRetries)
        {
            yield return new WaitForSeconds(retryInterval);
            FindLocalPlayer();
            retryCount++;
            Debug.Log($"CameraFollow: Looking for player, attempt {retryCount}/{maxRetries}");
        }
        
        if (target == null)
        {
            Debug.LogWarning("CameraFollow: Failed to find local player after multiple attempts");
        }
        else
        {
            Debug.Log($"CameraFollow: Successfully found local player at {target.position}");
            // Initialize camera position immediately when we find the player
            ResetCameraPosition();
        }
        
        isSearchingForPlayer = false;
    }
    
    private void FindLocalPlayer()
    {
        Debug.Log("CameraFollow: Searching for local player...");
        
        // First try to find using PlayerController
        var players = GameObject.FindObjectsOfType<PlayerController>();
        Debug.Log($"CameraFollow: Found {players.Length} PlayerController instances");
        
        foreach (var player in players)
        {
            if (player.IsOwner)
            {
                target = player.transform;
                Debug.Log($"CameraFollow: Found local player via PlayerController at {target.position}");
                return;
            }
        }
        
        // If no player found via PlayerController, try tag as fallback
        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null)
        {
            // Verify it's our local player if it has a NetworkObject
            NetworkObject netObj = taggedPlayer.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsLocalPlayer)
            {
                target = taggedPlayer.transform;
                Debug.Log($"CameraFollow: Found local player via tag at {target.position}");
                return;
            }
            else if (netObj == null)
            {
                // If there's no NetworkObject, assume it's our player (single player mode)
                target = taggedPlayer.transform;
                Debug.Log($"CameraFollow: Found player via tag (no network object) at {target.position}");
                return;
            }
        }
        
        Debug.Log("CameraFollow: No local player found in this search attempt");
    }
    
    private void Update()
    {
        // Ensure the camera follows the player: if target is null or not tagged as 'Player', reassign it
        if (target == null || !target.gameObject.CompareTag("Player"))
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                target = playerObj.transform;
                Debug.Log("CameraFollow: Updated target to Player: " + target.name);
            }
            else
            {
                Debug.LogWarning("CameraFollow: No GameObject with tag 'Player' found in Update.");
                return; // Exit early if no target is found
            }
        }

        // Only proceed if we have a valid target
        if (target == null)
        {
            return; // Skip camera movement if target is null
        }
        
        // Smoothly update orthographic size if camera component is available and orthographic
        if (cameraComponent != null && cameraComponent.orthographic)
        {
            if (Mathf.Abs(cameraComponent.orthographicSize - targetOrthographicSize) > 0.01f)
            {
                cameraComponent.orthographicSize = Mathf.Lerp(cameraComponent.orthographicSize, targetOrthographicSize, Time.deltaTime * zoomSpeed);
            }
            else
            {
                cameraComponent.orthographicSize = targetOrthographicSize; // Snap to target if very close
            }
        }
        
        // Handle rotation input from E and Q keys
        HandleRotationInput();

        // Get the target position (with rotation applied)
        Vector3 targetPosition = GetTargetPosition();
        
        // Continue with camera movement logic
        switch (followStyle)
        {
            case FollowStyle.Instant:
                // Instantly move to the target position
                transform.position = targetPosition;
                break;
            case FollowStyle.SmoothFollow:
                // Smoothly move to the target position using Lerp
                transform.position = Vector3.Lerp(
                    transform.position, 
                    targetPosition, 
                    smoothSpeed * Time.deltaTime * 10f);
                break;
            case FollowStyle.SmoothDamp:
                // Smoothly move to the target position using SmoothDamp
                transform.position = Vector3.SmoothDamp(
                    transform.position, 
                    targetPosition, 
                    ref currentVelocity, 
                    positionDamping);
                break;
            case FollowStyle.FixedOffset:
                // Use the rotated offset for fixed offset mode too
                transform.position = targetPosition;
                break;
        }

        // Always make the camera look at the target
        // This is simpler and works better with the orbital rotation
        if (target != null)
        {
            // Simply look at the target
            transform.LookAt(target.position);
        }
    }
    
    // This method is no longer needed as we've moved the logic directly into the Update method
    // Keeping it for backward compatibility
    private void UpdateCameraPosition(float speed)
    {
        if (target == null) return;
        
        Vector3 targetPosition = GetTargetPosition();
        transform.position = Vector3.Lerp(transform.position, targetPosition, speed * Time.deltaTime * 10f);
    }
    
    private Vector3 GetTargetPosition()
    {
        if (target == null) return transform.position; // Return current position if target is null
        
        // Calculate distance from target (length of offset)
        float distance = offset.magnitude;
        
        // Calculate the angle in radians
        float angleRad = currentRotationAngle * Mathf.Deg2Rad;
        
        // Calculate the new position using orbit calculation
        // Start with the original offset direction
        float originalYaw = Mathf.Atan2(offset.z, offset.x);
        
        // Add the rotation angle to the original yaw
        float newYaw = originalYaw + angleRad;
        
        // Calculate the new X and Z positions
        float newX = Mathf.Cos(newYaw) * distance;
        float newZ = Mathf.Sin(newYaw) * distance;
        
        // Keep the original Y offset
        Vector3 rotatedOffset = new Vector3(newX, offset.y, newZ);
        Vector3 targetPos = target.position + rotatedOffset;
        
        // Maintain fixed height if enabled
        if (fixedHeight)
        {
            targetPos.y = fixedHeightValue;
        }
        
        // For top-down view, only follow on X and Z
        if (topDownFollow)
        {
            targetPos.y = transform.position.y;
        }
        
        return targetPos;
    }
    
    // Rotate a vector around the Y axis
    private Vector3 RotateVectorAroundY(Vector3 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        
        float newX = vector.x * cos - vector.z * sin;
        float newZ = vector.x * sin + vector.z * cos;
        
        return new Vector3(newX, vector.y, newZ);
    }
    
    // Handle rotation input from E and Q keys
    private void HandleRotationInput()
    {
        // Check for Q key (rotate left)
        if (Input.GetKeyDown(KeyCode.Q))
        {
            targetRotationAngle -= rotationAngle;
            Debug.Log($"CameraFollow: Rotating left to {targetRotationAngle} degrees");
        }
        
        // Check for E key (rotate right)
        if (Input.GetKeyDown(KeyCode.E))
        {
            targetRotationAngle += rotationAngle;
            Debug.Log($"CameraFollow: Rotating right to {targetRotationAngle} degrees");
        }
        
        // Smoothly interpolate to the target rotation angle
        // Using a stronger interpolation factor to make the rotation more noticeable
        currentRotationAngle = Mathf.Lerp(currentRotationAngle, targetRotationAngle, Time.deltaTime * rotationSpeed * 2f);
        
        // Force an immediate update when the rotation changes significantly
        if (Mathf.Abs(targetRotationAngle - currentRotationAngle) > 1f)
        {
            // This helps ensure the camera position updates right away
            transform.position = GetTargetPosition();
        }
    }
    
    // Public method to change the target at runtime
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    // Public method to reset camera position instantly
    public void ResetCameraPosition()
    {
        if (target != null)
        {
            transform.position = GetTargetPosition();
            if (lookAtTarget)
            {
                transform.LookAt(target);
            }
        }
    }
    
    // Draw a visual representation of the camera follow in the editor
    private void OnDrawGizmosSelected()
    {
        if (target == null)
            return;
            
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(GetTargetPosition(), 0.5f);
    }
    
    // Public method to be called from NetworkManagerUI
    public void FindLocalPlayerWithRetry()
    {
        Debug.Log("CameraFollow: Starting to find local player with retry");
        isSearchingForPlayer = true;
        retryCount = 0;
        StartCoroutine(FindLocalPlayerWithRetryCoroutine());
    }
    
    private IEnumerator FindLocalPlayerWithRetryCoroutine()
    {
        Debug.Log("CameraFollow: Starting search for local player");
        
        // Small delay to ensure player has time to spawn
        yield return new WaitForSeconds(0.5f);
        
        // First attempt
        FindLocalPlayer();
        
        // If target is still null, retry a few times
        int attempts = 0;
        while (target == null && attempts < maxRetries)
        {
            attempts++;
            Debug.Log($"CameraFollow: Retry {attempts}/{maxRetries} to find local player");
            yield return new WaitForSeconds(retryInterval);
            FindLocalPlayer();
        }
        
        if (target != null)
        {
            Debug.Log($"CameraFollow: Successfully found local player at {target.position}");
            ResetCameraPosition();
        }
        else
        {
            Debug.LogError("CameraFollow: Failed to find local player after multiple attempts");
        }
        
        isSearchingForPlayer = false;
    }

    // --- New Public Methods for Cinematic Zoom ---
    public void StartCinematicZoom()
    {
        if (cameraComponent != null && cameraComponent.orthographic)
        {
            targetOrthographicSize = zoomedInOrthographicSize;
            Debug.Log($"CameraFollow: Starting cinematic zoom. Target size: {targetOrthographicSize}");
        }
        else
        {
            Debug.LogWarning("CameraFollow: Cannot start cinematic zoom. Camera component missing or not orthographic.");
        }
    }

    public void EndCinematicZoom()
    {
        if (cameraComponent != null && cameraComponent.orthographic)
        {
            targetOrthographicSize = initialOrthographicSize;
            Debug.Log($"CameraFollow: Ending cinematic zoom. Target size: {targetOrthographicSize}");
        }
        else
        { 
            Debug.LogWarning("CameraFollow: Cannot end cinematic zoom. Camera component missing or not orthographic.");
        }
    }
}