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
    [Tooltip("Tag used to identify buildings that NPCs should avoid")]
    public string buildingTag = "Building";

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
        
        // Subscribe to network variable change events
        networkTintColor.OnValueChanged += OnTintColorChanged;
        networkPosition.OnValueChanged += OnPositionChanged;
        networkIsMoving.OnValueChanged += OnMovingChanged;
        networkSpriteFlipX.OnValueChanged += OnSpriteFlipChanged;
        
        // If we are the client, apply initial values
        if (!IsServer)
        {
            // Apply the current network position
            transform.position = networkPosition.Value;
            
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
        
        // Also add buildings to forbidden areas
        GameObject[] buildingObjects = GameObject.FindGameObjectsWithTag(buildingTag);
        if (buildingObjects.Length > 0)
        {
            // Create a new array that combines both boundary and building objects
            GameObject[] combinedForbiddenAreas = new GameObject[forbiddenAreaObjects.Length + buildingObjects.Length];
            forbiddenAreaObjects.CopyTo(combinedForbiddenAreas, 0);
            buildingObjects.CopyTo(combinedForbiddenAreas, forbiddenAreaObjects.Length);
            forbiddenAreaObjects = combinedForbiddenAreas;
            Debug.Log($"NPCController: Added {buildingObjects.Length} buildings to forbidden areas");
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

        // Then start the routines
        PickNewDestination();
        StartCoroutine(MovementRoutine());
        if(behavior == NPCBehavior.WalksAndGroups) {
            StartCoroutine(GroupingRoutine());
        }

        // Set the NPC's scale to Vector3.one to match the player's size
        transform.localScale = Vector3.one;

        if(IsPointInForbiddenArea(transform.position)) {
            PickNewDestination();
            transform.position = currentDestination;
            networkPosition.Value = currentDestination;
            Debug.Log("NPCController: Spawn position was inside a forbidden area. Repositioning NPC.");
        }
    }
    
    private void OnTintColorChanged(Color previousValue, Color newValue)
    {
        SetTintColor(newValue);
    }
    
    private void OnPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        if (!IsServer)
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
    
    private void OnSpriteFlipChanged(bool previous, bool current)
    {
        SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
        if (spriteR != null)
        {
            spriteR.flipX = current;
        }
    }
    
    // Add LateUpdate to ensure NPCs always face the camera, even on clients
    private void LateUpdate()
    {
        // Make the NPC sprite always face the camera (billboarding)
        SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
        if(spriteR != null)
        {
            // Get the main camera
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                // Get the character transform (parent of the sprite)
                Transform characterTransform = spriteR.transform;
                
                // Get the camera's Y rotation
                float cameraYRotation = mainCamera.transform.rotation.eulerAngles.y;
                
                // Set the character's rotation to match camera's Y rotation
                characterTransform.rotation = Quaternion.Euler(0, cameraYRotation, 0);
                
                // Find and handle the shadow object
                Transform shadowTransform = transform.Find("Shadow");
                if (shadowTransform != null)
                {
                    // Keep the shadow flat on the ground (no rotation)
                    shadowTransform.rotation = Quaternion.Euler(90, 0, 0);
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
        // Try a few attempts to find a valid destination
        for (int attempts = 0; attempts < 10; attempts++)
        {
            Vector3 randomPoint = allowedAreaCenter + new Vector3(
                Random.Range(-allowedAreaSize.x / 2f, allowedAreaSize.x / 2f),
                0,
                Random.Range(-allowedAreaSize.z / 2f, allowedAreaSize.z / 2f)
            );
            if (!IsPointInForbiddenArea(randomPoint))
            {
                currentDestination = randomPoint;
                networkDestination.Value = randomPoint;
                return;
            }
        }
        // Fallback to current position if no valid point is found
        currentDestination = transform.position;
        networkDestination.Value = transform.position;
    }

    private bool IsPointInForbiddenArea(Vector3 point)
    {
        if (forbiddenAreaObjects == null) return false;
        foreach (var obj in forbiddenAreaObjects)
        {
            if (obj != null)
            {
                // Check for any type of collider, not just BoxCollider
                Collider area = obj.GetComponent<Collider>();
                if (area != null)
                {
                    // For box colliders, use bounds.Contains
                    if (area is BoxCollider && area.bounds.Contains(point))
                    {
                        return true;
                    }
                    // For other collider types, use ClosestPoint to check if point is inside or very close
                    else
                    {
                        Vector3 closestPoint = area.ClosestPoint(point);
                        float distance = Vector3.Distance(point, closestPoint);
                        if (distance < 0.1f) // If point is inside or very close to collider
                        {
                            return true;
                        }
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

            if (!inGroup)
            {
                float distance = Vector3.Distance(transform.position, currentDestination);
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
                    
                    // Compute movement direction towards the destination
                    Vector3 direction = (currentDestination - transform.position).normalized;

                    // Cast a ray in the movement direction to check for obstacles
                    RaycastHit hit;
                    float rayDistance = walkSpeed * Time.deltaTime * 2.0f; // Look slightly ahead
                    bool hitObstacle = Physics.Raycast(transform.position, direction, out hit, rayDistance);
                    
                    // Compute proposed new position
                    Vector3 proposedPos = transform.position + direction * walkSpeed * Time.deltaTime;
                    
                    // Check if we would hit a building or enter a forbidden area
                    if(hitObstacle && (hit.collider.CompareTag(buildingTag) || hit.collider.CompareTag("Boundary")))
                    {
                        // We're about to hit a building or boundary, pick a new destination
                        Debug.Log($"NPC avoiding obstacle: {hit.collider.gameObject.name}");
                        PickNewDestination();
                    }
                    // If the proposed position is NOT in a forbidden area, move there
                    else if(!IsPointInForbiddenArea(proposedPos)) 
                    {
                        transform.position = proposedPos;
                        networkPosition.Value = proposedPos;
                        
                        // Flip sprite horizontally based on the movement direction if using a SpriteRenderer
                        SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
                        if(spriteR != null && Mathf.Abs(direction.x) > 0.01f)
                        {
                            bool shouldFlipX = (direction.x < 0);
                            spriteR.flipX = shouldFlipX;
                            networkSpriteFlipX.Value = shouldFlipX;
                        }
                        
                        // Make the NPC sprite always face the camera (billboarding)
                        if(spriteR != null)
                        {
                            // Get the main camera
                            Camera mainCamera = Camera.main;
                            if (mainCamera != null)
                            {
                                // Get the character transform (parent of the sprite)
                                Transform characterTransform = spriteR.transform;
                                
                                // Get the camera's Y rotation
                                float cameraYRotation = mainCamera.transform.rotation.eulerAngles.y;
                                
                                // Set the character's rotation to match camera's Y rotation
                                characterTransform.rotation = Quaternion.Euler(0, cameraYRotation, 0);
                            }
                        }
                    } 
                    else 
                    {
                        // We're about to enter a forbidden area, pick a new destination
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
                        groupTimer = Random.Range(minGroupTime, maxGroupTime);
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
                        groupTimer = Random.Range(minGroupTime, maxGroupTime);
                        ComputeFormationTarget();
                    }
                }
            }
            else
            {
                // If already in a group, decrement timer and possibly leave group when time expires
                groupTimer -= Time.deltaTime;
                if(groupTimer <= 0f)
                {
                    inGroup = false;
                    PickNewDestination();
                }
                yield return null;
            }
        }
    }

    private void Update()
    {
        // Only the server updates movement logic
        if (!IsServer) return;

        if (inGroup)
        {
            float step = walkSpeed * Time.deltaTime;
            Vector3 proposedGroupPos = Vector3.MoveTowards(transform.position, formationTargetPos, step);
            float distanceToTarget = Vector3.Distance(transform.position, formationTargetPos);
            if(distanceToTarget > destinationTolerance) {
                networkIsMoving.Value = true;
                
                if(!IsPointInForbiddenArea(proposedGroupPos)) {
                    transform.position = proposedGroupPos;
                    networkPosition.Value = proposedGroupPos;
                }
                // Compute direction towards the formation target
                Vector3 groupDirection = (formationTargetPos - transform.position).normalized;
                // Flip sprite based on x direction if using a SpriteRenderer
                SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
                if(spriteR != null && Mathf.Abs(groupDirection.x) > 0.01f) {
                    spriteR.flipX = (groupDirection.x < 0);
                    networkSpriteFlipX.Value = (groupDirection.x < 0);
                }
            } else {
                transform.position = formationTargetPos;
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
                    
                    // Face toward center
                    Vector3 directionToCenter = (groupCenter - transform.position).normalized;
                    SpriteRenderer spriteR = npcRenderer as SpriteRenderer;
                    if(spriteR != null && Mathf.Abs(directionToCenter.x) > 0.01f) {
                        spriteR.flipX = (directionToCenter.x < 0);
                        networkSpriteFlipX.Value = (directionToCenter.x < 0);
                    }
                }
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
        Gizmos.color = Color.green;
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
} 