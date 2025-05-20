using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

public class NPCController : NetworkBehaviour
{
    [Header("Area Settings")]
    [Tooltip("Center of the allowed area where the NPC can walk")]
    public Vector3 allowedAreaCenter = Vector3.zero;
    [Tooltip("Size of the allowed area (only X and Z are considered)")]
    public Vector3 allowedAreaSize = new Vector3(10f, 0, 10f);
    [Tooltip("List of GameObjects that contain BoxColliders which the NPC should avoid entering")]
    public GameObject[] forbiddenAreaObjects;

    [Header("Movement Settings")]
    [Tooltip("Walking speed of the NPC")]
    public float walkSpeed = 2f;
    [Tooltip("Speed at which the NPC rotates towards its target direction")]
    public float rotationSpeed = 2f;
    [Tooltip("Distance threshold to consider that the destination has been reached")]
    public float destinationTolerance = 0.2f;
    
    // Network variables for synchronization
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(Vector3.zero, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> networkDestination = new NetworkVariable<Vector3>(Vector3.zero,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkInGroup = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Color> networkTintColor = new NetworkVariable<Color>(Color.white,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkIsMoving = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkSpriteFlipX = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        
    // Local cached values
    private Vector3 currentDestination;
    
    [Header("Group Settings")]
    [Tooltip("Whether this NPC is currently part of a group")]
    public bool inGroup = false;
    [Tooltip("Radius to check for nearby NPCs for grouping")]
    public float groupRadius = 3f;
    [Tooltip("Minimum time (in seconds) an NPC stays in a group")]
    public float minGroupTime = 15f;
    [Tooltip("Maximum time (in seconds) an NPC stays in a group")]
    public float maxGroupTime = 30f;
    [Tooltip("Minimum number of NPCs (including this one) required to form a group")]
    public int minGroupSize = 3;
    [Tooltip("Maximum number of NPCs allowed in a group")]
    public int maxGroupSize = 5;
    private float groupTimer = 0f;
    private Vector3 formationTargetPos;

    [Header("Group Leaving Dynamics")]
    [Tooltip("Base probability per second that an NPC will leave a group.")]
    public float baseLeaveChancePerSecond = 0.02f; // e.g., 2% chance per second initially
    [Tooltip("How much the leave probability increases each second the NPC remains in the group.")]
    public float leaveChanceIncreaseRatePerSecond = 0.01f; // e.g., an additional 1% chance per second

    private float timeInGroup = 0f; // Tracks how long NPC has been in current group

    [Header("Random Tint")]
    [Tooltip("Array of up to 6 tint colors that can be randomly assigned to the NPC")]
    public Color[] tintColors;
    private Renderer npcRenderer;

    [Header("Animation")]
    [Tooltip("Animator component from the NPC prefab. Expecting two states: Idle and Running.")]
    public Animator npcAnimator;

    // Declare new variable at the top of the class (after existing private variables)
    private bool groupingEnabled = false;

    // Add this enum and variable at the top of the class, below existing private variables
    enum NPCBehavior { Walker, WalksAndGroups }
    private NPCBehavior behavior;
    private Rigidbody rb; // Add Rigidbody reference

    // Stuck Detection Variables
    [Header("Stuck Detection (Server-Side)")]
    [Tooltip("Time (seconds) an NPC must be 'slow' while walking before checking if stuck.")]
    public float stuckTimeThreshold = 2.0f;
    [Tooltip("Max distance moved during stuckTimeThreshold to be considered stuck.")]
    public float stuckDistanceThreshold = 0.5f;
    [Tooltip("Radius to check for colliders when NPC is suspected of being stuck.")]
    public float stuckCheckRadius = 0.6f;
    [Tooltip("Layers to check for obstacles when NPC is suspected of being stuck.")]
    public LayerMask obstacleDetectionLayerMask;

    [Header("Unstucking Settings (Server-Side)")]
    [Tooltip("Layers that will trigger the unsticking behavior upon collision (e.g., Buildings, Props).")]
    public LayerMask collisionUnstuckLayers;
    [Tooltip("How far the NPC will attempt to move away from an obstacle when unsticking.")]
    public float unstuckMoveDistance = 1.0f;
    [Tooltip("Speed of the NPC when performing the unsticking movement.")]
    public float unstuckMoveSpeed = 1.5f;
    [Tooltip("Radius around the NPC to check for forbidden areas. Should be at least half the NPC's width.")]
    public float npcClearanceRadius = 0.5f;

    private Vector3 lastPositionCheck;
    private float stuckTimer;
    private bool serverIsUnsticking = false;
    private Coroutine activeUnstuckingCoroutine = null;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Debug.Log($"NPCController spawned on network. Object Name: {gameObject.name}, NetworkObjectId: {NetworkObjectId}, IsOwner: {IsOwner}, IsServer: {IsServer}");
        
        // Find renderer
        npcRenderer = GetComponent<Renderer>();
        // If the root doesn't have a renderer, try finding a child named "Character"
        if(npcRenderer == null)
        {
            Transform characterTransform = transform.Find("Character");
            if(characterTransform != null)
            {
                npcRenderer = characterTransform.GetComponent<Renderer>();
            }
        }
        
        // Get the Animator component
        if(npcAnimator == null)
        {
            npcAnimator = GetComponent<Animator>();
            if(npcAnimator == null)
            {
                Transform characterTransform = transform.Find("Character");
                if(characterTransform != null)
                {
                    npcAnimator = characterTransform.GetComponent<Animator>();
                }
            }
        }
        
        // Get the Rigidbody component
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError($"NPCController on {gameObject.name} is missing a Rigidbody component! Movement will not work correctly.", this);
        }
        
        // Subscribe to network variable change events
        networkTintColor.OnValueChanged += OnTintColorChanged;
        networkPosition.OnValueChanged += OnPositionChanged;
        networkIsMoving.OnValueChanged += OnMovingChanged;
        networkSpriteFlipX.OnValueChanged += OnSpriteFlipChanged;
        
        // If we are the client, apply initial values
        if (!IsServer)
        {
            // Apply the current network position
            if (rb != null) // Interpolate if Rigidbody is present
            {
                rb.MovePosition(networkPosition.Value);
            }
            else // Fallback if no Rigidbody (should not happen)
            {
                transform.position = networkPosition.Value;
            }
            
            // Apply the current tint color
            if (npcRenderer != null)
            {
                SetTintColor(networkTintColor.Value);
            }
            
            // Apply animation state
            if (npcAnimator != null)
            {
                npcAnimator.SetBool("Running", networkIsMoving.Value);
            }
            
            // Apply sprite flip
            SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
            if (spriteR != null)
            {
                spriteR.flipX = networkSpriteFlipX.Value;
            }
        }
        
        // Server-only initialization
        if (IsServer)
        {
            // Server-side initialization
            ServerInit();
        }
    }
    
    private void ServerInit()
    {
        // Automatically find forbidden area objects tagged as "Boundary" if not assigned
        if ((forbiddenAreaObjects == null || forbiddenAreaObjects.Length == 0))
        {
            forbiddenAreaObjects = GameObject.FindGameObjectsWithTag("Boundary");
        }

        if (tintColors != null && tintColors.Length > 0)
        {
            // Pick a random color from the available list (up to 6 colors)
            int idx = Random.Range(0, Mathf.Min(tintColors.Length, 6));
            Color selectedColor = tintColors[idx];
            networkTintColor.Value = selectedColor;
            SetTintColor(selectedColor);
        }

        // Randomly assign behavior: 50% Walker, 50% WalksAndGroups
        behavior = (Random.value < 0.5f) ? NPCBehavior.Walker : NPCBehavior.WalksAndGroups;

        // If behavior is WalksAndGroups, perform grouping initialization
        if(behavior == NPCBehavior.WalksAndGroups) {
            if(Random.value < 0.4f) {
                // 40% start in a group
                inGroup = true;
                networkInGroup.Value = true;
                groupingEnabled = true;
                groupTimer = Random.Range(minGroupTime, maxGroupTime);
                ComputeFormationTarget();
            } else {
                // The NPC will walk around and not try to group for at least 10 seconds
                StartCoroutine(EnableGroupingAfterDelay(10f));
            }
        } else {
            // For Walker behavior, ensure not in a group
            inGroup = false;
            networkInGroup.Value = false;
        }

        // Set the NPC's scale to Vector3.one to match the player's size
        transform.localScale = Vector3.one;

        // Emergency Reposition: If spawned in a forbidden area, move to allowedAreaCenter first.
        if(IsPointInForbiddenArea(transform.position, npcClearanceRadius)) {
            Debug.LogWarning($"[NPC_INIT_REPOS] NPC '{gameObject.name}' (ID: {NetworkObjectId}) spawned in a forbidden area at {transform.position}. Forcing move to allowedAreaCenter: {allowedAreaCenter}.");
            transform.position = allowedAreaCenter;
            if (rb != null) rb.MovePosition(allowedAreaCenter);
            networkPosition.Value = allowedAreaCenter; // Ensure network syncs this forced move
            // Also update lastPositionCheck to prevent immediate false stuck detection
            lastPositionCheck = allowedAreaCenter;
        }

        // Then start the routines
        PickNewDestination();
        StartCoroutine(MovementRoutine());
        if(behavior == NPCBehavior.WalksAndGroups) {
            StartCoroutine(GroupingRoutine());
        }

        // Initialize stuck detection variables
        stuckTimer = 0f;
        serverIsUnsticking = false; // Ensure initialized
    }
    
    private void OnTintColorChanged(Color previousValue, Color newValue)
    {
        SetTintColor(newValue);
    }
    
    private void OnPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        if (!IsServer && rb != null) // Only clients move, and only if rb exists
        {
            // Instead of directly setting transform.position,
            // we let the Update loop handle interpolation via rb.MovePosition
            // towards the latest networkPosition.Value.
            // So, this callback might not need to do anything for position if
            // non-local NPC movement is handled in Update/LateUpdate via Lerp.
            // However, for immediate snapping on change (less smooth):
            // rb.MovePosition(newValue);
            // For now, let's assume Update handles smooth movement for non-owners.
        }
        else if (!IsServer) // Fallback if no Rigidbody (should not happen)
        {
            transform.position = newValue;
        }
    }
    
    private void OnMovingChanged(bool previousValue, bool newValue)
    {
        if (npcAnimator != null)
        {
            npcAnimator.SetBool("Running", newValue);
        }
    }
    
    private void OnSpriteFlipChanged(bool previousValue, bool newValue)
    {
        SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
        if (spriteR != null)
        {
            spriteR.flipX = newValue;
            
            // Make the sprite face the camera
            MakeSpritesFaceCamera();
        }
    }
    
    // Make sprites face the camera (billboard effect) while maintaining correct movement direction
    private void MakeSpritesFaceCamera()
    {
        SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
        if (spriteR != null && spriteR.transform != null && Camera.main != null)
        {
            // Make the sprite face the camera (billboard effect)
            spriteR.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
            
            // If the NPC is moving, update flipX based on movement direction relative to camera view
            if (networkIsMoving.Value && IsServer)
            {
                // Calculate movement direction in world space
                Vector3 movementDir = Vector3.zero;
                if (!inGroup)
                {
                    // Regular movement - direction is toward destination
                    movementDir = (currentDestination - transform.position).normalized;
                }
                else
                {
                    // Group movement - direction is toward formation position
                    movementDir = (formationTargetPos - transform.position).normalized;
                }
                
                // Project movement direction onto camera's right vector to determine if moving left or right relative to camera view
                float movementDot = Vector3.Dot(movementDir, Camera.main.transform.right);
                
                // Update flipX based on relative movement direction
                if (Mathf.Abs(movementDot) > 0.01f) // Only update if there's significant horizontal movement
                {
                    networkSpriteFlipX.Value = (movementDot < 0); // Flip if moving left relative to camera
                }
            }
        }
    }

    private void SetTintColor(Color color)
    {
        if (npcRenderer != null)
        {
            // Check if npcRenderer is a SpriteRenderer
            SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
            if (spriteR != null)
            {
                spriteR.color = color;
                Debug.Log("NPCController: Set tint color on SpriteRenderer to " + color);
            }
            else
            {
                npcRenderer.material.color = color;
                Debug.Log("NPCController: Set tint color on Material to " + color);
            }
        }
    }

    // Pick a random destination within the allowed area that is not inside a forbidden area
    private void PickNewDestination()
    {
        const int maxPickAttempts = 10; // Max attempts to find a clear destination
        // float buffer = 0.5f; // Buffer to keep away from edges of allowed area and forbidden zones // Replaced by npcClearanceRadius logic

        for (int attempt = 0; attempt < maxPickAttempts; attempt++)
        {
            // Adjust allowed area size by the npcClearanceRadius to avoid picking points too close to the boundary of the allowed area itself
            float effectiveAllowedX = Mathf.Max(0, allowedAreaSize.x - 2 * npcClearanceRadius);
            float effectiveAllowedZ = Mathf.Max(0, allowedAreaSize.z - 2 * npcClearanceRadius);

            Vector3 randomPoint = allowedAreaCenter + new Vector3(
                Random.Range(-effectiveAllowedX / 2f, effectiveAllowedX / 2f),
                0, // Assuming NPCs move on a flat plane relative to allowedAreaCenter. Adjust if Y varies.
                Random.Range(-effectiveAllowedZ / 2f, effectiveAllowedZ / 2f)
            );

            // 1. Check if the point itself (considering NPC clearance) is in a forbidden area
            if (IsPointInForbiddenArea(randomPoint, npcClearanceRadius))
            {
                continue; // Try another point
            }

            // 2. Proactive Path Check: Raycast to see if the path to this point is clear of critical obstacles
            Vector3 currentPosition = rb != null ? rb.position : transform.position;
            Vector3 directionToRandomPoint = (randomPoint - currentPosition).normalized;
            float distanceToRandomPoint = Vector3.Distance(currentPosition, randomPoint);

            // Ensure we don't raycast with zero distance if NPC is already at the randomPoint (unlikely here but good practice)
            if (distanceToRandomPoint > 0.01f)
            {
                // Use the collisionUnstuckLayers for this check as well, or a dedicated one if needed.
                if (Physics.Raycast(currentPosition, directionToRandomPoint, out RaycastHit hitInfo, distanceToRandomPoint, collisionUnstuckLayers))
                {
                    // Debug.Log($"[NPC_PATH_BLOCKED] NPC '{gameObject.name}': Proposed path to {randomPoint} is blocked by '{hitInfo.collider.name}' on a critical layer. Attempting to find new destination (Attempt: {attempt + 1}/{maxPickAttempts}).");
                    continue; // Path is blocked, try to pick another destination
                }
            }
            
            // If both checks pass, this is a good destination
            currentDestination = randomPoint;
            networkDestination.Value = randomPoint;
            Debug.Log($"[NPC_DESTINATION] NPC '{gameObject.name}' picked new destination: {currentDestination}");
            return;
        }

        // Fallback: Could not find a suitable clear destination after several attempts.
        // Stay at current position or pick the last tried random point even if potentially blocked (less ideal).
        Debug.LogWarning($"[NPC_DESTINATION_FAIL] NPC '{gameObject.name}' (ID: {NetworkObjectId}): Failed to find a clear new destination after {maxPickAttempts} attempts. NPC may remain static or behave erratically. Current Position: {transform.position}. Last tried randomPoint: {currentDestination} (this might be the NPC's current position if all attempts failed early)");
        // Setting destination to current position is problematic if current position itself is bad.
        // For now, we still set it, but the NPC should ideally enter a different state or retry later.
        currentDestination = transform.position; 
        networkDestination.Value = transform.position;
    }

    private bool IsPointInForbiddenArea(Vector3 point)
    {
        return IsPointInForbiddenArea(point, 0f); // Default to no clearance if not specified
    }

    private bool IsPointInForbiddenArea(Vector3 point, float clearanceRadius)
    {
        if (forbiddenAreaObjects == null) return false;
        foreach (var obj in forbiddenAreaObjects)
        {
            if (obj != null)
            {
                BoxCollider area = obj.GetComponent<BoxCollider>();
                if (area != null)
                {
                    Bounds expandedBounds = area.bounds;
                    // Expand the bounds by the clearance diameter (clearanceRadius * 2)
                    // The point itself should not be within these expanded bounds.
                    expandedBounds.Expand(clearanceRadius * 2.0f); 
                    if (expandedBounds.Contains(point))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    // Routine to control movement when not in a group
    private IEnumerator MovementRoutine()
    {
        while (true)
        {
            if (!IsServer) yield break;

            if (serverIsUnsticking) {
                networkIsMoving.Value = true; // Keep animation if desired
                yield return null; // Let the UnstuckRoutine do its job
                continue;
            }

            if (!inGroup)
            {
                // Current position for calculations, preferring Rigidbody's position
                Vector3 currentActualPosition = (rb != null) ? rb.position : transform.position;
                
                float distance = Vector3.Distance(currentActualPosition, currentDestination);
                if (distance < destinationTolerance)
                {
                    // Update network moving state
                    networkIsMoving.Value = false;
                    
                    // Pause briefly at destination before picking a new one
                    yield return new WaitForSeconds(Random.Range(1f, 2f));
                    PickNewDestination();
                }
                else
                {
                    // Update network moving state
                    networkIsMoving.Value = true;
                    
                    // Compute movement direction towards the destination using currentActualPosition
                    Vector3 direction = (currentDestination - currentActualPosition).normalized;

                    // Compute proposed new position using currentActualPosition
                    Vector3 proposedPos = currentActualPosition + direction * walkSpeed * Time.deltaTime;
                    // If the proposed position (considering NPC clearance) is NOT in a forbidden area, move there; otherwise, choose a new destination
                    if(!IsPointInForbiddenArea(proposedPos, npcClearanceRadius)) {
                        if (rb != null) rb.MovePosition(proposedPos); else transform.position = proposedPos;
                        networkPosition.Value = proposedPos;
                        
                        // Make the sprite face the camera - this will handle both billboard effect and proper sprite orientation
                        MakeSpritesFaceCamera();
                    } else {
                        PickNewDestination();
                    }
                }
            }
            yield return null;
        }
    }

    // Updated GroupingRoutine to add per-iteration random delay when not grouping
    private IEnumerator GroupingRoutine()
    {
        // Add a small random delay at start
        yield return new WaitForSeconds(Random.Range(0f, 3f));
        while (true)
        {
            if (!IsServer) yield break;

            if (serverIsUnsticking) {
                networkIsMoving.Value = true; // Keep animation if desired
                yield return null; // Let the UnstuckRoutine do its job
                continue;
            }

            // Only process grouping if behavior is WalksAndGroups
            if(behavior != NPCBehavior.WalksAndGroups) {
                yield break;
            }

            // Only attempt grouping if grouping is enabled for this NPC
            if(!groupingEnabled) {
                yield return null;
                continue;
            }

            if (!inGroup)
            {
                // Wait a random duration between grouping attempts
                yield return new WaitForSeconds(Random.Range(1f, 3f));
                // First, try to join an existing group
                Collider[] groupColliders = Physics.OverlapSphere(transform.position, groupRadius * 2f);
                List<NPCController> existingGroup = new List<NPCController>();
                foreach (Collider col in groupColliders)
                {
                    NPCController npc = col.GetComponent<NPCController>();
                    if(npc != null && npc.inGroup)
                    {
                        existingGroup.Add(npc);
                    }
                }
                if(existingGroup.Count > 0)
                {
                    // Calculate the group center
                    Vector3 groupCenter = Vector3.zero;
                    foreach(var member in existingGroup) { groupCenter += member.transform.position; }
                    groupCenter /= existingGroup.Count;
                    // Count how many are close to the group center
                    int count = 0;
                    foreach(var member in existingGroup)
                    {
                        if(Vector3.Distance(member.transform.position, groupCenter) < groupRadius * 2f)
                            count++;
                    }
                    if(count < maxGroupSize)
                    {
                        inGroup = true;
                        timeInGroup = 0f;
                        networkInGroup.Value = true;
                        ComputeFormationTarget();
                    }
                }
                else
                {
                    // No nearby group found, try to form a new group from non-group NPCs
                    Collider[] nonGroupColliders = Physics.OverlapSphere(transform.position, groupRadius);
                    List<NPCController> nearbyNPCs = new List<NPCController>();
                    foreach (Collider col in nonGroupColliders)
                    {
                        NPCController npc = col.GetComponent<NPCController>();
                        if(npc != null && npc != this && !npc.inGroup)
                        {
                            nearbyNPCs.Add(npc);
                        }
                    }
                    if(nearbyNPCs.Count + 1 >= minGroupSize && nearbyNPCs.Count + 1 <= maxGroupSize)
                    {
                        inGroup = true;
                        timeInGroup = 0f;
                        networkInGroup.Value = true;

                        // Make all newly grouped NPCs also set their inGroup and networkInGroup
                        foreach(var newMember in nearbyNPCs) {
                            newMember.inGroup = true;
                            newMember.networkInGroup.Value = true;
                            newMember.timeInGroup = 0f;
                            newMember.ComputeFormationTarget();
                        }
                        ComputeFormationTarget();
                    }
                }
            }
            else
            {
                // If already in a group, handle leaving likelihood
                timeInGroup += Time.deltaTime;
                float currentLeaveProbability = baseLeaveChancePerSecond + (timeInGroup * leaveChanceIncreaseRatePerSecond);
                
                if (Random.value < currentLeaveProbability * Time.deltaTime)
                {
                    inGroup = false;
                    networkInGroup.Value = false;
                    timeInGroup = 0f;
                    PickNewDestination();
                }
            }
        }
    }

    private void Update()
    {
        if (IsServer)
        {
            // Handle group movement if in a group
            if (inGroup)
            {
                HandleGroupMovement();
            }

            // Stuck detection logic
            if (networkIsMoving.Value)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer >= stuckTimeThreshold)
                {
                    float distanceMoved = Vector3.Distance(transform.position, lastPositionCheck);
                    if (distanceMoved < stuckDistanceThreshold)
                    {
                        // NPC might be stuck, check for colliders
                        // Only run stuck alert if not currently trying to unstuck from a collision
                        if (!serverIsUnsticking) 
                        {
                            Collider[] hitColliders = Physics.OverlapSphere(transform.position, stuckCheckRadius, obstacleDetectionLayerMask);
                            if (hitColliders.Length > 0)
                            {
                                string colliderNames = "";
                                foreach (var hitCollider in hitColliders)
                                {
                                    // Skip self
                                    if (hitCollider.gameObject == gameObject) continue;
                                    colliderNames += hitCollider.gameObject.name + (hitCollider.transform.parent ? " (Parent: " + hitCollider.transform.parent.name + ")" : "") + ", ";
                                }
                                if (!string.IsNullOrEmpty(colliderNames))
                                {
                                    Debug.LogWarning($"[NPC_STUCK_ALERT] NPC '{gameObject.name}' (ID: {NetworkObjectId}) might be stuck near {transform.position}. Detected obstacles: {colliderNames.TrimEnd(',', ' ')}");
                                }
                                else
                                {
                                    // This case might happen if the only colliders detected were the NPC itself, which we skip.
                                    Debug.LogWarning($"[NPC_STUCK_ALERT] NPC '{gameObject.name}' (ID: {NetworkObjectId}) might be stuck near {transform.position}. No external obstacles detected on specified layers. Current Destination: {currentDestination}. Consider checking NavMesh or other movement logic.");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"[NPC_STUCK_ALERT] NPC '{gameObject.name}' (ID: {NetworkObjectId}) might be stuck near {transform.position}. No obstacles detected on specified layers. Current Destination: {currentDestination}. Consider checking NavMesh.");
                            }
                        }
                        lastPositionCheck = transform.position;
                        stuckTimer = 0f;
                    }
                }
            }
            else
            {
                // Reset when not moving
                lastPositionCheck = transform.position;
                stuckTimer = 0f;
            }
        }

        // For non-server entities, smoothly move towards the networkPosition
        if (!IsServer && rb != null) // Note: Changed 'else if' to 'if' to allow server to also execute billboard logic below
        {
            rb.MovePosition(Vector3.Lerp(rb.position, networkPosition.Value, Time.deltaTime * 10f)); // Similar to PlayerController
        }
        else if (!IsServer) // Fallback if no Rigidbody
        {
            transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
        }
        
        // Always make sprites face the camera
        if (IsServer)
        {
            // Server updates sprite orientation and sends to clients
            MakeSpritesFaceCamera();
        }
        else
        {
            // Clients only need to apply the billboard effect without changing flipX
            SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
            if (spriteR != null && spriteR.transform != null && Camera.main != null)
            {
                // Apply billboard effect
                spriteR.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
                
                // Apply the network-synced flipX value
                spriteR.flipX = networkSpriteFlipX.Value;
            }
        }
    }

    private void HandleGroupMovement()
    {
        float step = walkSpeed * Time.deltaTime;
        Vector3 proposedGroupPos = Vector3.MoveTowards(rb != null ? rb.position : transform.position, formationTargetPos, step);
        float distanceToTarget = Vector3.Distance(rb != null ? rb.position : transform.position, formationTargetPos);
        
        if(distanceToTarget > destinationTolerance) {
            // Still moving toward formation position
            networkIsMoving.Value = true;
            
            if(!IsPointInForbiddenArea(proposedGroupPos, npcClearanceRadius)) {
                if (rb != null) rb.MovePosition(proposedGroupPos); else transform.position = proposedGroupPos;
                networkPosition.Value = proposedGroupPos;
            }
            
            // Compute direction towards the formation target
            Vector3 groupDirection = (formationTargetPos - transform.position).normalized;
            
            // Make the sprite face the camera - this will handle both billboard effect and proper sprite orientation
            MakeSpritesFaceCamera();
        } 
        else {
            // Reached formation position
            if (rb != null) rb.MovePosition(formationTargetPos); else transform.position = formationTargetPos;
            networkPosition.Value = formationTargetPos;
            networkIsMoving.Value = false;
            
            // When in position, face toward the center of the circle
            Collider[] colliders = Physics.OverlapSphere(transform.position, groupRadius * 2f);
            List<NPCController> groupMembers = new List<NPCController>();
            
            foreach (Collider col in colliders)
            {
                NPCController npc = col.GetComponent<NPCController>();
                if(npc != null && npc.inGroup)
                {
                    groupMembers.Add(npc);
                }
            }
            
            if(groupMembers.Count > 0) {
                // Calculate group center
                Vector3 groupCenter = Vector3.zero;
                foreach(var member in groupMembers){ groupCenter += member.transform.position; }
                groupCenter /= groupMembers.Count;
                
                // When stationary in group, face toward center
                Vector3 directionToCenter = (groupCenter - transform.position).normalized;
                
                // Project direction to center onto camera's right vector
                float directionDot = Vector3.Dot(directionToCenter, Camera.main.transform.right);
                
                SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
                if(spriteR != null && Mathf.Abs(directionDot) > 0.01f) {
                    networkSpriteFlipX.Value = (directionDot < 0);
                }
                
                // Make the sprite face the camera
                MakeSpritesFaceCamera();
            }
        }

        // Server already handles animation in the movement logic with networkIsMoving
    }

    private void ComputeFormationTarget()
    {
        Collider[] colliders = Physics.OverlapSphere(transform.position, groupRadius * 2f);
        List<NPCController> groupMembers = new List<NPCController>();
        foreach (Collider col in colliders)
        {
            NPCController npc = col.GetComponent<NPCController>();
            if(npc != null && npc.inGroup)
            {
                groupMembers.Add(npc);
            }
        }

        if(groupMembers.Count > 0)
        {
            // If the group is too large, only consider the closest maxGroupSize members
            if(groupMembers.Count > maxGroupSize)
            {
                groupMembers.Sort((a, b) => Vector3.Distance(a.transform.position, transform.position)
                    .CompareTo(Vector3.Distance(b.transform.position, transform.position)));
                groupMembers = groupMembers.GetRange(0, maxGroupSize);
                if(!groupMembers.Contains(this))
                {
                    // Should not happen, but fallback
                    return;
                }
            }

            // Compute formation center using current positions
            Vector3 formationCenter = Vector3.zero;
            foreach(var member in groupMembers){ formationCenter += member.transform.position; }
            formationCenter /= groupMembers.Count;

            // Sort group members by angle around the formation center
            groupMembers.Sort((a, b) => {
                float angleA = Mathf.Atan2(a.transform.position.z - formationCenter.z, a.transform.position.x - formationCenter.x);
                float angleB = Mathf.Atan2(b.transform.position.z - formationCenter.z, b.transform.position.x - formationCenter.x);
                return angleA.CompareTo(angleB);
            });

            int index = groupMembers.IndexOf(this);
            float angle = (2 * Mathf.PI / groupMembers.Count) * index;
            float formationRadius = 2.0f;
            formationTargetPos = formationCenter + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * formationRadius;
        }
    }

    // Optional: Visualize the allowed area and group radius in editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(allowedAreaCenter, allowedAreaSize);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, groupRadius);
        
        // Visualize forbidden areas
        if(forbiddenAreaObjects != null)
        {
            Gizmos.color = Color.red;
            foreach(var obj in forbiddenAreaObjects)
            {
                if(obj != null)
                {
                    BoxCollider col = obj.GetComponent<BoxCollider>();
                    if(col != null)
                    {
                        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
                    }
                }
            }
        }
    }

    // Add a new coroutine at the end of the class (before OnNetworkSpawn or after ComputeFormationTarget):
    private IEnumerator EnableGroupingAfterDelay(float delaySeconds) {
        yield return new WaitForSeconds(delaySeconds);
        groupingEnabled = true;
    }

    // Called when a collision occurs and persists
    private void OnCollisionStay(Collision collision)
    {
        if (!IsServer || serverIsUnsticking || !networkIsMoving.Value) return;

        // Check if the collision is with a layer that should trigger unstucking
        if ((collisionUnstuckLayers.value & (1 << collision.gameObject.layer)) > 0)
        {
            // Check contacts for a valid normal
            if (collision.contactCount > 0)
            {
                ContactPoint contact = collision.contacts[0];
                Vector3 awayDirection = contact.normal; // Normal points away from the obstacle

                // Ensure awayDirection is somewhat horizontal if preferred, to avoid launching upwards on flat ground collisions
                // For now, we use the direct normal.

                Debug.Log($"[NPC_UNSTICKING] NPC '{gameObject.name}' collided with '{collision.gameObject.name}' on a critical layer. Attempting to unstuck.");

                serverIsUnsticking = true;
                // networkIsMoving.Value = false; // Stop regular movement processing by other routines briefly

                if (activeUnstuckingCoroutine != null)
                {
                    StopCoroutine(activeUnstuckingCoroutine);
                }
                activeUnstuckingCoroutine = StartCoroutine(UnstuckRoutine(awayDirection));
            }
        }
    }

    private IEnumerator UnstuckRoutine(Vector3 awayDirection)
    {
        networkIsMoving.Value = true; // Ensure NPC is animated as moving

        Vector3 startPosition = transform.position;
        // Calculate target position slightly further than unstuckMoveDistance to ensure clearance
        Vector3 targetUnstuckPosition = startPosition + awayDirection * (unstuckMoveDistance + 0.1f);
        float journeyDuration = unstuckMoveDistance / unstuckMoveSpeed; 
        float elapsedTime = 0f;

        Debug.Log($"[NPC_UNSTICKING] Starting UnstuckRoutine for '{gameObject.name}'. Moving from {startPosition} towards {targetUnstuckPosition} over {journeyDuration}s.");

        while (elapsedTime < journeyDuration)
        {
            Vector3 currentLerpPos = Vector3.Lerp(startPosition, targetUnstuckPosition, elapsedTime / journeyDuration);
            if (rb != null)
            {
                rb.MovePosition(currentLerpPos);
            }
            else
            {
                transform.position = currentLerpPos;
            }
            networkPosition.Value = rb != null ? rb.position : transform.position;
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // Ensure final position is set
        Vector3 finalPos = rb != null ? rb.position : transform.position; // Use actual final position after MovePosition attempts
        if (Vector3.Distance(finalPos, targetUnstuckPosition) > 0.05f) { // If not quite there, snap
             if (rb != null) rb.MovePosition(targetUnstuckPosition); else transform.position = targetUnstuckPosition;
             networkPosition.Value = targetUnstuckPosition;
        }

        Debug.Log($"[NPC_UNSTICKING] '{gameObject.name}' finished unstuck movement. Picking new destination.");
        PickNewDestination(); 
        
        // Reset flags after picking new destination and allowing one frame for MovementRoutine to potentially start
        yield return null; 
        serverIsUnsticking = false;
        activeUnstuckingCoroutine = null;
        // networkIsMoving.Value will be controlled by MovementRoutine now
    }
} 