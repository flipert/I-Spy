using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("The target to follow")]
    public Transform target;
    
    [Header("Camera Position Settings")]
    [Tooltip("Distance from the target")]
    public float distance = 5.0f;
    
    [Tooltip("Height offset from the target")]
    public float height = 2.0f;
    
    [Tooltip("Horizontal offset from the target (left/right)")]
    public float horizontalOffset = 0.0f;
    
    [Header("Camera Control Settings")]
    [Tooltip("Speed of camera rotation when using mouse")]
    public float rotationSpeed = 5.0f;
    
    [Tooltip("Minimum vertical angle (looking up)")]
    public float minVerticalAngle = -30.0f;
    
    [Tooltip("Maximum vertical angle (looking down)")]
    public float maxVerticalAngle = 60.0f;
    
    [Tooltip("Damping for position changes (lower = more responsive)")]
    public float positionDamping = 0.1f;
    
    [Tooltip("Damping for rotation changes (lower = more responsive)")]
    public float rotationDamping = 0.1f;
    
    [Tooltip("Invert vertical mouse input")]
    public bool invertY = false;
    
    [Header("Collision Settings")]
    [Tooltip("Enable camera collision detection")]
    public bool enableCollision = true;
    
    [Tooltip("Layers that the camera will collide with")]
    public LayerMask collisionLayers = -1;
    
    [Tooltip("Minimum distance when collision is detected")]
    public float minDistance = 1.0f;
    
    [Tooltip("Extra distance to keep from obstacles (buffer zone)")]
    public float collisionBuffer = 0.3f;
    
    [Tooltip("How quickly the camera adjusts to avoid collisions (higher = faster)")]
    public float collisionResponseSpeed = 10f;
    
    // Private variables
    private float currentDistance;
    private float currentRotationX = 0f;
    private float currentRotationY = 0f;
    private Vector3 currentVelocity = Vector3.zero;
    private Quaternion currentRotation;
    
    // Player finding variables
    private bool isSearchingForPlayer = false;
    private int retryCount = 0;
    private float retryInterval = 0.5f;
    private int maxRetries = 5;
    
    private void Start()
    {
        Debug.Log("ThirdPersonCamera: Start");
        
        // Initialize current distance
        currentDistance = distance;
        
        // If no target is set, try to find the local player
        if (target == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                target = playerObj.transform;
                Debug.Log($"ThirdPersonCamera: Found player at {target.position}");
            }
            else
            {
                Debug.Log("ThirdPersonCamera: No player found with tag 'Player', will try to find local player");
                StartCoroutine(FindLocalPlayerWithRetryCoroutine());
            }
        }
        
        // Initialize camera position and rotation
        if (target != null)
        {
            // Get initial rotation values based on current camera orientation
            Vector3 direction = transform.position - target.position;
            direction.Normalize();
            
            currentRotationY = Mathf.Asin(direction.y) * Mathf.Rad2Deg;
            currentRotationX = Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
            
            // Set initial position
            UpdateCameraPosition(true);
        }
    }
    
    private void LateUpdate()
    {
        if (target == null)
            return;
        
        // Get mouse input
        float mouseX = Input.GetAxis("Mouse X") * rotationSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * rotationSpeed * (invertY ? -1 : 1);
        
        // Update rotation based on mouse input
        currentRotationX += mouseX;
        currentRotationY = Mathf.Clamp(currentRotationY + mouseY, minVerticalAngle, maxVerticalAngle);
        
        // Calculate target rotation
        Quaternion targetRotation = Quaternion.Euler(currentRotationY, currentRotationX, 0);
        
        // Smoothly interpolate current rotation
        currentRotation = Quaternion.Slerp(currentRotation, targetRotation, 1 - Mathf.Exp(-rotationDamping * Time.deltaTime * 30));
        
        // Update camera position (run multiple times for better collision response)
        for (int i = 0; i < (enableCollision ? 3 : 1); i++)
        {
            UpdateCameraPosition(false);
        }
    }
    
    private void UpdateCameraPosition(bool instant)
    {
        if (target == null)
            return;
        
        // Calculate desired position based on target and rotation
        Vector3 direction = new Vector3(
            Mathf.Sin(currentRotationX * Mathf.Deg2Rad) * Mathf.Cos(currentRotationY * Mathf.Deg2Rad),
            Mathf.Sin(currentRotationY * Mathf.Deg2Rad),
            Mathf.Cos(currentRotationX * Mathf.Deg2Rad) * Mathf.Cos(currentRotationY * Mathf.Deg2Rad)
        );
        
        // Add horizontal offset
        Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
        direction += right * horizontalOffset;
        
        // Calculate target position
        Vector3 targetPosition = target.position;
        targetPosition.y += height;
        
        // Check for collisions with improved handling
        float finalDistance = distance;
        if (enableCollision)
        {
            // Use a spherecast instead of raycast for better collision detection
            RaycastHit hit;
            float sphereCastRadius = 0.2f; // Small radius to detect nearby obstacles
            
            // Cast in the direction from target to desired camera position
            if (Physics.SphereCast(targetPosition, sphereCastRadius, -direction, out hit, distance + collisionBuffer, collisionLayers))
            {
                // Calculate distance, accounting for the buffer and sphere radius
                finalDistance = Mathf.Max(hit.distance - collisionBuffer - sphereCastRadius, minDistance);
                
                // Debug collision detection
                Debug.DrawLine(targetPosition, hit.point, Color.red);
                Debug.DrawRay(hit.point, hit.normal * 0.5f, Color.yellow);
            }
            
            // Additional check: make sure camera is not inside any collider
            Vector3 potentialCameraPos = targetPosition - direction * finalDistance;
            Collider[] overlappingColliders = Physics.OverlapSphere(potentialCameraPos, sphereCastRadius, collisionLayers);
            
            if (overlappingColliders.Length > 0)
            {
                // Camera would be inside a collider, find the closest point outside
                foreach (Collider collider in overlappingColliders)
                {
                    Vector3 closestPoint = collider.ClosestPoint(potentialCameraPos);
                    Vector3 adjustmentDirection = (potentialCameraPos - closestPoint).normalized;
                    
                    // Move camera position to be outside the collider plus buffer
                    float adjustmentDistance = sphereCastRadius + collisionBuffer - Vector3.Distance(potentialCameraPos, closestPoint);
                    if (adjustmentDistance > 0)
                    {
                        potentialCameraPos += adjustmentDirection * adjustmentDistance;
                        
                        // Recalculate final distance based on adjusted position
                        finalDistance = Vector3.Distance(targetPosition, potentialCameraPos);
                    }
                }
            }
        }
        
        // Smoothly adjust current distance with faster response for collisions
        float damping = enableCollision ? Mathf.Max(positionDamping, collisionResponseSpeed * Time.deltaTime) : positionDamping;
        currentDistance = instant ? finalDistance : Mathf.Lerp(currentDistance, finalDistance, 1 - Mathf.Exp(-damping * Time.deltaTime * 30));
        
        // Set camera position and rotation
        Vector3 targetCameraPosition = targetPosition - direction * currentDistance;
        
        if (instant)
        {
            transform.position = targetCameraPosition;
            transform.rotation = Quaternion.LookRotation(direction);
            currentRotation = transform.rotation;
        }
        else
        {
            // Smoothly move camera
            transform.position = Vector3.SmoothDamp(transform.position, targetCameraPosition, ref currentVelocity, positionDamping);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 1 - Mathf.Exp(-rotationDamping * Time.deltaTime * 30));
        }
    }
    
    private IEnumerator FindLocalPlayerWithRetryCoroutine()
    {
        if (isSearchingForPlayer)
            yield break;
            
        isSearchingForPlayer = true;
        retryCount = 0;
        
        Debug.Log("ThirdPersonCamera: Starting to look for local player");
        
        // Wait for NetworkManager to be ready
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            Debug.Log("ThirdPersonCamera: Waiting for NetworkManager to be ready...");
            yield return new WaitForSeconds(0.5f);
        }
        
        // Wait a bit longer to ensure players are spawned
        yield return new WaitForSeconds(1.0f);
        
        while (target == null && retryCount < maxRetries)
        {
            yield return new WaitForSeconds(retryInterval);
            FindLocalPlayer();
            retryCount++;
            Debug.Log($"ThirdPersonCamera: Looking for player, attempt {retryCount}/{maxRetries}");
        }
        
        if (target == null)
        {
            Debug.LogWarning("ThirdPersonCamera: Failed to find local player after multiple attempts");
        }
        else
        {
            Debug.Log($"ThirdPersonCamera: Successfully found local player at {target.position}");
            // Initialize camera position immediately when we find the player
            UpdateCameraPosition(true);
        }
        
        isSearchingForPlayer = false;
    }
    
    // Public method to be called from NetworkManagerUI
    public void FindLocalPlayerWithRetry()
    {
        Debug.Log("ThirdPersonCamera: Starting to find local player with retry");
        isSearchingForPlayer = true;
        retryCount = 0;
        StartCoroutine(FindLocalPlayerWithRetryCoroutine());
    }
    
    private void FindLocalPlayer()
    {
        Debug.Log("ThirdPersonCamera: Searching for local player...");
        
        // First try to find using PlayerController
        var players = GameObject.FindObjectsOfType<PlayerController>();
        Debug.Log($"ThirdPersonCamera: Found {players.Length} PlayerController instances");
        
        foreach (var player in players)
        {
            if (player.IsOwner)
            {
                target = player.transform;
                Debug.Log($"ThirdPersonCamera: Found local player via PlayerController at {target.position}");
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
                Debug.Log($"ThirdPersonCamera: Found local player via tag at {target.position}");
                return;
            }
            else if (netObj == null)
            {
                // If there's no NetworkObject, assume it's our player (single player mode)
                target = taggedPlayer.transform;
                Debug.Log($"ThirdPersonCamera: Found player via tag (no network object) at {target.position}");
                return;
            }
        }
        
        Debug.Log("ThirdPersonCamera: No local player found in this search attempt");
    }
    
    // Public method to change the target at runtime
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
        {
            UpdateCameraPosition(true);
        }
    }
    
    // Public method to reset camera position instantly
    public void ResetCameraPosition()
    {
        if (target != null)
        {
            UpdateCameraPosition(true);
        }
    }
    
    // Draw gizmos for visualization in the editor
    private void OnDrawGizmosSelected()
    {
        if (target == null)
            return;
            
        Gizmos.color = Color.green;
        
        // Draw a line from target to camera
        Vector3 targetPosition = target.position;
        targetPosition.y += height;
        Gizmos.DrawLine(targetPosition, transform.position);
        
        // Draw a sphere at the target position
        Gizmos.DrawWireSphere(targetPosition, 0.5f);
    }
}
