using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using System.Collections; // For Random.Range if needed beyond simple time

[RequireComponent(typeof(NavMeshAgent))]
public class NPCController : NetworkBehaviour
{
    [Header("State Timers")]
    [Tooltip("Maximum time (seconds) the NPC will stay in Idle state.")]
    public float maxIdleTime = 5f;
    [Tooltip("Maximum time (seconds) the NPC will stay in Running state (or until destination is reached).")]
    public float maxRunTime = 10f;
    [Tooltip("Radius within which to pick a new random destination.")]
    public float wanderRadius = 20f;

    [Header("Movement Settings")]
    [Tooltip("The speed at which the NPC moves.")]
    public float moveSpeed = 3.5f; // Default speed
    [Tooltip("The angular speed for turning (degrees/second). Lower values mean smoother turns.")]
    public float angularSpeed = 120f; // Default angular speed
    [Tooltip("The acceleration of the NPC. Set very high for effectively instant speed changes.")]
    public float acceleration = 10000f; // Very high acceleration for virtually no ramp-up time

    [Header("Pathfinding Settings")]
    [Tooltip("If true, NavMeshAgent-level obstacle avoidance will be disabled, allowing NPCs to pass through each other.")]
    public bool disableAgentObstacleAvoidance = true;

    [Header("Appearance")]
    [Tooltip("Index for pre-defined pastel color (0-5). -1 for random pastel. Defaults to white if index is out of range unless -1.")]
    public int assignedColorIndex = -1;
    [Tooltip("SpriteRenderer for the character visuals. Assign the 'Character' child's SpriteRenderer here if possible.")]
    public SpriteRenderer characterSpriteRenderer; // Made public

    [Header("Animation")]
    [Tooltip("Animator component from the NPC prefab. Expecting a 'Running' boolean parameter.")]
    public Animator npcAnimator;
    private Rigidbody rb; // Moved Rigidbody declaration here

    [Header("Kill Interaction")]
    [Tooltip("The sprite GameObject to show when player is in range to kill this NPC.")]
    public GameObject killPromptIcon; // Now a GameObject with SpriteRenderer
    private SpriteRenderer killPromptRenderer; // Reference to the prompt's SpriteRenderer

    // Network Variables
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(Vector3.zero, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> networkAgentDestination = new NetworkVariable<Vector3>(Vector3.zero, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkIsMoving = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> networkAgentDesiredVelocity = new NetworkVariable<Vector3>(Vector3.zero, // For client-side flip logic
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Color> networkSpriteColor = new NetworkVariable<Color>(Color.white, // For sprite tinting
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkIsAlive = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Local state to ensure death animation stays properly
    private bool isDead = false;

    // Internal State
    private NavMeshAgent agent;

    // Define pastel colors
    private static readonly Color[] pastelColors = new Color[]
    {
        new Color(0.98f, 0.68f, 0.68f, 1f), // Pastel 0 (Salmon Pink)
        new Color(0.68f, 0.98f, 0.68f, 1f), // Pastel 1 (Mint Green)
        new Color(0.68f, 0.68f, 0.98f, 1f), // Pastel 2 (Lavender Blue)
        new Color(0.98f, 0.98f, 0.68f, 1f), // Pastel 3 (Pale Yellow)
        new Color(0.98f, 0.8f, 0.6f, 1f),   // Pastel 4 (Peach)
        new Color(0.8f, 0.68f, 0.98f, 1f)   // Pastel 5 (Lilac)
    };

    // Add Awake to set Rigidbody to kinematic if present
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true; 
        }
        agent = GetComponent<NavMeshAgent>(); // Initialize agent here too for early access if needed

        if (killPromptIcon != null)
        {
            killPromptRenderer = killPromptIcon.GetComponent<SpriteRenderer>();
            if (killPromptRenderer == null)
            {
                Debug.LogWarning("Kill prompt icon doesn't have a SpriteRenderer component!");
            }
            killPromptIcon.SetActive(false); // Ensure prompt is hidden initially
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.updateRotation = false; // Prevent NavMeshAgent from rotating the parent GameObject
            if (IsServer) // Server controls agent properties
            {
                agent.speed = moveSpeed;
                agent.angularSpeed = angularSpeed;
                agent.acceleration = acceleration;
                if (disableAgentObstacleAvoidance)
                {
                    agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                }
                else
                {
                    // Optionally, set a default avoidance type if you re-enable it
                    agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance; 
                }
            }
        }

        if (npcAnimator == null) npcAnimator = GetComponentInChildren<Animator>();
        
        // Find the Character child and its SpriteRenderer
        if (characterSpriteRenderer == null) // If not assigned in Inspector, try to find it
        {
            // Attempt to find it based on the hierarchy: Root -> TheManInTheCoatNPC -> Character
            // Assuming the intermediate child has the same name as the root prefab, which might be the case from screenshots
            Transform intermediateChild = transform.Find(gameObject.name); // e.g., "TheManInTheCoatNPC"
            if (intermediateChild == null && transform.childCount > 0) {
                // Fallback if the intermediate child is not named the same as the root, but is the first child (common setup)
                // Or if the animator is on this child.
                if (npcAnimator != null && npcAnimator.transform != transform) {
                     intermediateChild = npcAnimator.transform;
                } else {
                    // A general guess if there's an intermediate parent for visuals
                    // This part is speculative without exact naming conventions.
                    // For now, let's assume the animator's parent or a direct child if animator is on root.
                }
            }
            
            // If an intermediate child is found (could be the animator's gameobject)
            Transform parentToSearchCharacterIn = transform; // Default to root
            if (intermediateChild != null && intermediateChild != transform) {
                 parentToSearchCharacterIn = intermediateChild;
            }


            Transform characterChildTransform = parentToSearchCharacterIn.Find("Character");
            if (characterChildTransform != null)
            {
                characterSpriteRenderer = characterChildTransform.GetComponent<SpriteRenderer>();
            }
        }

        if (agent == null)
            Debug.LogError($"NPCController on {gameObject.name} is missing a NavMeshAgent component!", this);
        if (npcAnimator == null)
            Debug.LogWarning($"NPCController on {gameObject.name} is missing an Animator component or it's not assigned to the root.", this);
        if (characterSpriteRenderer == null)
            Debug.LogWarning($"NPCController on {gameObject.name} could not find a SpriteRenderer on a child named 'Character'.", this);

        if (!IsServer)
        {
            networkPosition.OnValueChanged += ClientOnPositionChanged;
            networkIsMoving.OnValueChanged += ClientOnIsMovingChanged;
            networkAgentDestination.OnValueChanged += ClientOnAgentDestinationChanged;
            networkAgentDesiredVelocity.OnValueChanged += ClientOnAgentDesiredVelocityChanged;
            networkSpriteColor.OnValueChanged += ClientOnSpriteColorChanged;
            networkIsAlive.OnValueChanged += ClientOnIsAliveChanged; // Client subscribes to liveness changes

            ApplyCurrentNetworkState();
        }
        else // Server initializes state
        {
            agent.Warp(transform.position); // Ensure agent is at the spawned position
            // Apply speed settings on server-controlled agent
            if (agent != null)
            {
                agent.speed = moveSpeed;
                agent.angularSpeed = angularSpeed;
                agent.acceleration = acceleration;
                if (disableAgentObstacleAvoidance && IsServer) // Ensure this is also applied here for server
                {
                    agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                }
                else if (IsServer)
                {
                    agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                }
            }
            // Apply color on server side if needed for any direct server rendering/logic (usually not for sprites)
            // Determine and set initial color
            Color initialColor = Color.white;
            if (assignedColorIndex == -1) // Random pastel
            {
                initialColor = pastelColors[Random.Range(0, pastelColors.Length)];
            }
            else if (assignedColorIndex >= 0 && assignedColorIndex < pastelColors.Length) // Assigned pastel
            {
                initialColor = pastelColors[assignedColorIndex];
            }
            // If index is out of range (and not -1), it remains Color.white as per network var default or can be set explicitly
            
            networkSpriteColor.Value = initialColor;
            if(characterSpriteRenderer != null) characterSpriteRenderer.color = initialColor; // Also apply on server for host

            Server_ForceIdle(); // Start idle until NPCBehaviorManager issues a command.
        }
    }

    private void ClientOnPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        // Clients simply observe the position, agent movement is handled by server.
        // Smooth movement is handled in Update.
        if (agent != null && !agent.isOnNavMesh) // If somehow off NavMesh, try to warp
        {
            agent.Warp(newValue);
        }
    }

    private void ClientOnAgentDestinationChanged(Vector3 previousValue, Vector3 newValue)
    {
        // For clients, if we want them to *also* path for smoother visuals (optional advanced)
        // For now, clients mostly rely on networkPosition for movement lerping.
        // However, setting the agent's destination can be useful for visual debugging or if agent.remainingDistance is used by client.
        if (agent != null && agent.isOnNavMesh) // Removed agent.destination != newValue check, let client also set/update
        {
            // agent.SetDestination(newValue); // Potentially enable if client-side pathing is desired for prediction
            // Client agent should also respect speed settings if it's doing any pathing or for visual consistency
            // However, primary control is server-side. This is more for visual consistency if client-side prediction pathing is used.
            // agent.speed = moveSpeed;
            // agent.angularSpeed = angularSpeed;
            // agent.acceleration = acceleration;
        }
    }

    private void ClientOnIsMovingChanged(bool previousValue, bool newValue)
    {
        if (npcAnimator != null) npcAnimator.SetBool("Running", newValue);
    }

    private void ClientOnAgentDesiredVelocityChanged(Vector3 previous, Vector3 current)
    {
        // Placeholder: Client logic for when desired velocity changes, if any beyond Update's use
    }

    private void ClientOnSpriteColorChanged(Color previousValue, Color newValue)
    {
        if (characterSpriteRenderer != null)
        {
            characterSpriteRenderer.color = newValue;
        }
    }

    [ClientRpc]
    private void ProcessDeathClientRpc()
    {
        Debug.Log($"NPC {gameObject.name} received ProcessDeathClientRpc. Current networkIsAlive: {networkIsAlive.Value}, local isDead: {isDead}");
        
        // If the network says we are dead, and we haven't started the local death process yet.
        if (!networkIsAlive.Value && !isDead) 
        {
            Debug.Log($"NPC {gameObject.name} - ProcessDeathClientRpc: Network confirms dead and local death not yet processed. Setting isDead = true and calling HandleDeathVisuals.");
            isDead = true; // Set local flag to prevent re-entry from other paths
            HandleDeathVisuals();
        }
        else if (networkIsAlive.Value && isDead)
        {
             Debug.Log($"NPC {gameObject.name} - ProcessDeathClientRpc: Network says alive, but was locally dead. (Respawn scenario?)");
             isDead = false; // Reset if it was resurrected
        }
        else if (!networkIsAlive.Value && isDead)
        {
            Debug.Log($"NPC {gameObject.name} - ProcessDeathClientRpc: Network confirms dead, and local death already processed. Doing nothing extra.");
        }
    }

    // ClientOnIsAliveChanged will still log, but ProcessDeathClientRpc is now the primary visual trigger on client.
    private void ClientOnIsAliveChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"NPC {gameObject.name} - ClientOnIsAliveChanged: networkIsAlive changed from {previousValue} to {newValue}. Local isDead: {isDead}");
        // This callback primarily updates the local understanding of liveness for other systems.
        // Visuals are now more directly handled by ProcessDeathClientRpc to avoid race conditions.
        if (!newValue && !isDead) {
            // It's possible this gets called slightly after ProcessDeathClientRpc in some frames.
            // isDead = true; // isDead should be set by ProcessDeathClientRpc now
            // HandleDeathVisuals(); // Visuals handled by ProcessDeathClientRpc
            Debug.Log($"NPC {gameObject.name} - ClientOnIsAliveChanged: Detected death. isDead should be set by ProcessDeathClientRpc.");
        }
        else if (newValue) {
            isDead = false; // If it becomes alive again (e.g. respawn)
            Debug.Log($"NPC {gameObject.name} - ClientOnIsAliveChanged: Detected ALIVE state. isDead set to false.");
            // Add any logic needed for respawn visuals if applicable here, e.g., re-enable components.
        }
    }

    private void ApplyCurrentNetworkState()
    {
        if (rb != null && networkPosition.Value != Vector3.zero) // Check if Rigidbody exists and pos is valid
        {
            rb.MovePosition(networkPosition.Value); // Snap to initial position
        }
        else if (networkPosition.Value != Vector3.zero)
        {
            transform.position = networkPosition.Value; // Snap to initial position
        }

        // Apply speed settings to client's agent if it exists, mainly for visual consistency or if client-side pathing is enabled.
        // The actual movement authority and simulation is on the server.
        if (agent != null && agent.isOnNavMesh)
        {
            // agent.speed = moveSpeed; // Not strictly necessary for pure position syncing.
            // agent.angularSpeed = angularSpeed; // Ditto.
            // agent.acceleration = acceleration; // Ditto.
            if (networkAgentDestination.Value != Vector3.zero)
            {
                // agent.SetDestination(networkAgentDestination.Value); // Optional: client sets initial destination
            }
        }
        // Don't reset animation state if the character is dead
        if (!isDead && npcAnimator != null) npcAnimator.SetBool("Running", networkIsMoving.Value);
        if (characterSpriteRenderer != null) characterSpriteRenderer.color = networkSpriteColor.Value; // Apply initial color
    }

    // Add this helper method (can be identical to PlayerKill's or adapted)
    private string GetAnimatorStateName(Animator anim)
    {
        if (anim == null || !anim.isInitialized || anim.runtimeAnimatorController == null) return "Animator_Not_Ready";
        if (anim.IsInTransition(0))
        {
            AnimatorStateInfo nextState = anim.GetNextAnimatorStateInfo(0);
            return $"Transitioning_To_State"; // Simplified, hash: {nextState.fullPathHash}
        }
        else
        {
            AnimatorClipInfo[] clipInfo = anim.GetCurrentAnimatorClipInfo(0);
            if (clipInfo.Length > 0 && clipInfo[0].clip != null)
            {
                return clipInfo[0].clip.name;
            }
            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName("TheManInTheCoatKill")) return "TheManInTheCoatKill";
            if (stateInfo.IsName("TheManInTheCoatDeath")) return "TheManInTheCoatDeath";
            if (stateInfo.IsName("MainInCoatIdle")) return "MainInCoatIdle";
            if (stateInfo.IsName("ManInCoatRun")) return "ManInCoatRun";
            return $"UnknownState_Hash_{stateInfo.shortNameHash}";
        }
    }

    private IEnumerator CheckAnimationStateAfterTrigger(Animator anim, string expectedStateNameFragment, string triggerName)
    {
        if (anim == null) yield break;
        yield return null; 
        string newState = GetAnimatorStateName(anim);
        Debug.Log($"NPC Animator state 1 frame after '{triggerName}' trigger: {newState}. Expected to contain: '{expectedStateNameFragment}'");
        if (!newState.Contains(expectedStateNameFragment))
        {
            Debug.LogWarning($"NPC Animator did NOT immediately transition to a state like '{expectedStateNameFragment}'. Current: {newState}");
        }
        
        yield return new WaitForSeconds(0.5f); 
        string delayedState = GetAnimatorStateName(anim);
        Debug.Log($"NPC Animator state 0.5s after '{triggerName}' trigger: {delayedState}. Expected: '{expectedStateNameFragment}'");
        if (!delayedState.Contains(expectedStateNameFragment))
        {
            Debug.LogWarning($"NPC Animator STILL not in a state like '{expectedStateNameFragment}' after 0.5s. Current: {delayedState}");
        }
    }

    void Update()
    {
        if (!networkIsAlive.Value || isDead) // If not alive or already handling death, do nothing
        {
            // Optionally, ensure agent is stopped and animations are off if not handled by KillNPCClientRpc
            if (agent != null && agent.enabled && !agent.isStopped)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            
            // We don't want to mess with animations here, as they're handled by HandleDeathVisuals
            return;
        }

        // Make kill prompt face the camera if it's active
        if (killPromptIcon != null && killPromptIcon.activeSelf && killPromptRenderer != null && Camera.main != null)
        {
            killPromptIcon.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward, Vector3.up);
        }

        if (IsServer)
        {
            networkPosition.Value = transform.position;
            ServerUpdateMovementDirection(); // Server updates movement direction for clients

            // This check is for autonomously stopping if it reaches a target.
            // NPCBehaviorManager will generally control how long it "tries" to run towards a target.
            if (networkIsMoving.Value && agent != null && !agent.pathPending)
            {
                if (agent.remainingDistance <= agent.stoppingDistance)
                {
                    if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
                    {
                        // Reached destination. NPCBehaviorManager might issue a new command or this NPC will wait.
                        // For now, we can make it stop moving and let NPCBehaviorManager decide the next high-level action.
                        // Server_ForceIdle(); // Optionally, tell it to become idle. Or let BehaviorManager handle the next transition.
                        // For now, let NPCBehaviorManager's timer decide when to switch.
                        // This simply means it physically can't move further along this path.
                        // Setting networkIsMoving to false here might be too abrupt if BehaviorManager expects it to be "wandering" for a set time.
                        // Let's let ServerUpdateMovementDirection handle networkIsMoving based on actual agent velocity.
                    }
                }
            }
        }
        else // Client-side smoothing and animation
        {
            if (agent != null && agent.isOnNavMesh) // Use agent's current position if available
            {
                 transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
            }
            else // Fallback if no agent or off mesh
            {
                transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
            }
            // Animation is updated via OnValueChanged callback
        }

        // This part should run for any client (including host as client) that renders the NPC
        if (characterSpriteRenderer != null && Camera.main != null)
        {
            // Billboarding: Make sprite face the camera plane, keeping world up as sprite's up
            characterSpriteRenderer.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward, Vector3.up);
            // Apply synced flip
            // characterSpriteRenderer.flipX = networkSpriteFlipX.Value;

            // Client-side sprite flip based on networked desired velocity and camera orientation
            Vector3 currentWorldDesiredVelocity = networkAgentDesiredVelocity.Value;
            if (currentWorldDesiredVelocity.sqrMagnitude > 0.001f) // Check if there's any movement intention
            {
                Camera cam = Camera.main;
                // Transform the world-space desired velocity into the camera's local space
                Vector3 cameraSpaceVelocity = cam.transform.InverseTransformDirection(currentWorldDesiredVelocity);

                // Use a threshold for flipping to prevent jitter when movement is near-vertical on screen
                float clientFlipThreshold = 0.1f; 

                if (cameraSpaceVelocity.x > clientFlipThreshold) // Moving right relative to camera
                {
                    characterSpriteRenderer.flipX = false;
                }
                else if (cameraSpaceVelocity.x < -clientFlipThreshold) // Moving left relative to camera
                {
                    characterSpriteRenderer.flipX = true;
                }
                // If cameraSpaceVelocity.x is within the threshold, current flipX is maintained to avoid jitter.
            }
            // If not moving (currentWorldDesiredVelocity is zero), the sprite's flipX remains as it was.
        }
    }

    private Vector3 PickRandomNavMeshLocation(float radius)
    {
        Vector3 randomDirection = Random.insideUnitSphere * radius;
        randomDirection += transform.position; // Relative to current position
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, radius, NavMesh.AllAreas))
        {
            return hit.position;
        }
        // Debug.LogWarning($"NPC {gameObject.name} failed to sample position on NavMesh within radius {radius} around {transform.position}.");
        return Vector3.zero; // Indicate failure
    }

    private void ServerUpdateMovementDirection() // New method name and logic
    {
        if (!IsServer) return; 

        if (agent != null && agent.hasPath && agent.desiredVelocity.sqrMagnitude > 0.01f && !agent.isStopped) 
        {
            networkAgentDesiredVelocity.Value = agent.desiredVelocity.normalized;
            if (!networkIsMoving.Value) networkIsMoving.Value = true; // Ensure this is set if moving
        }
        else
        {
            // If not moving, or no path, or desiredVelocity is negligible, 
            // set networked desired velocity to zero so clients know.
            if (networkAgentDesiredVelocity.Value != Vector3.zero)
            {
                networkAgentDesiredVelocity.Value = Vector3.zero;
            }
            if (networkIsMoving.Value) networkIsMoving.Value = false; // Ensure this is unset if not moving
        }
    }

    // --- Public methods for NPCBehaviorManager to call (SERVER-SIDE) ---
    public void Server_ForceIdle()
    {
        if (!IsServer || agent == null || !agent.isOnNavMesh) return;

        agent.isStopped = true;
        agent.ResetPath(); // Clear any existing path
        networkIsMoving.Value = false;
        // networkAgentDesiredVelocity is handled by ServerUpdateMovementDirection which will see isStopped
        if (npcAnimator != null) npcAnimator.SetBool("Running", false);
        // Debug.Log($"NPC {gameObject.name} was forced to IDLE by BehaviorManager.");
    }

    public void Server_StartWandering()
    {
        if (!IsServer || agent == null || !agent.isOnNavMesh) return;

        Vector3 randomNavMeshPoint = PickRandomNavMeshLocation(wanderRadius);
        if (randomNavMeshPoint != Vector3.zero)
        {
            agent.SetDestination(randomNavMeshPoint);
            agent.isStopped = false;
            networkAgentDestination.Value = randomNavMeshPoint;
            networkIsMoving.Value = true; // Will be confirmed by ServerUpdateMovementDirection based on velocity
            if (npcAnimator != null) npcAnimator.SetBool("Running", true);
            // Debug.Log($"NPC {gameObject.name} was commanded to WANDER by BehaviorManager to {randomNavMeshPoint}.");
        }
        else
        {
            // Failed to find a wander point, BehaviorManager might try again or switch state.
            // For now, NPCController just reports failure by not moving.
            // Consider forcing idle if wander fails and NPCBehaviorManager doesn't immediately give a new task.
            Server_ForceIdle(); // Fallback to idle if cannot wander
            // Debug.LogWarning($"NPC {gameObject.name} (WANDER command) failed to find NavMesh point. Forcing Idle.");
        }
    }

    public void Server_MoveToTarget(Vector3 targetPosition)
    {
        if (!IsServer || agent == null || !agent.isOnNavMesh) return;

        if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, 1.0f, NavMesh.AllAreas)) // Check if target is on/near navmesh
        {
            agent.SetDestination(hit.position);
            agent.isStopped = false;
            networkAgentDestination.Value = hit.position;
            networkIsMoving.Value = true; // Will be confirmed by ServerUpdateMovementDirection
            if (npcAnimator != null) npcAnimator.SetBool("Running", true);
            // Debug.Log($"NPC {gameObject.name} was commanded to MOVE by BehaviorManager to {hit.position}.");
        }
        else
        {
            // Target is not valid or not reachable
            Server_ForceIdle(); // Fallback to idle
            // Debug.LogWarning($"NPC {gameObject.name} (MOVE command) target {targetPosition} is not on NavMesh. Forcing Idle.");
        }
    }
    
    // Placeholder for group following behavior
    // public void Server_FollowTarget(Transform targetToFollow, Vector3 offset)
    // {
    // if (!IsServer || agent == null || !agent.isOnNavMesh || targetToFollow == null) return;
    // This would require continuous updates in NPCController's Update or a coroutine on the server
    // For simplicity with BehaviorManager, it might be better if BehaviorManager periodically calls Server_MoveToTarget
    // with updated positions from the followed target.
    // Or, this method could start a coroutine here.
    // agent.isStopped = false;
    // networkIsMoving.Value = true;
    // if (npcAnimator != null) npcAnimator.SetBool("Running", true);
    // Add logic to continuously update destination: agent.SetDestination(targetToFollow.position + offset);
    // Debug.Log($"NPC {gameObject.name} was commanded to FOLLOW {targetToFollow.name} by BehaviorManager.");
    // }

    public bool IsPathfindingComplete()
    {
        if (!IsServer || agent == null) return true; // If no agent, or not server, assume complete or not applicable.
        if (agent.pathPending) return false; // Still calculating path
        if (agent.remainingDistance > agent.stoppingDistance) return false; // Still has distance to cover
        if (agent.hasPath && agent.velocity.sqrMagnitude > 0.01f) return false; // Still moving
        return true; // Path is complete or NPC is stopped
    }

    public void ShowKillPrompt(bool show)
    {
        if (killPromptIcon != null && networkIsAlive.Value) // Only show if alive
        {
            killPromptIcon.SetActive(show);
            
            // Make sure the sprite faces the camera if shown
            if (show && killPromptRenderer != null && Camera.main != null)
            {
                // Make the prompt face the camera
                killPromptIcon.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward, Vector3.up);
            }
        }
        else if (killPromptIcon != null && !show) // Always allow hiding
        {
            killPromptIcon.SetActive(false);
        }
    }

    private void HandleDeathVisuals()
    {
        if (npcAnimator == null) 
        {
            Debug.LogError($"NPC {gameObject.name}: npcAnimator is NULL in HandleDeathVisuals. Cannot play death animation. Check NPC prefab Animator setup and OnNetworkSpawn.");
            if (IsServer) StartCoroutine(DespawnAfterDelay(2f));
            ShowKillPrompt(false);
            if (agent != null && agent.enabled)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero; 
                agent.enabled = false; // <--- AGGRESSIVELY DISABLE AGENT ON CLIENT
                Debug.Log($"NPC {gameObject.name} (HandleDeathVisuals/NoAnimator): Agent stopped/disabled as fallback.");
            }
            return;
        }

        Debug.Log($"NPC {gameObject.name} triggering Die animation. Animator exists: {npcAnimator != null}. Is dead: {isDead}");
        
        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero; 
            agent.enabled = false; // <--- AGGRESSIVELY DISABLE AGENT ON CLIENT
            Debug.Log($"NPC {gameObject.name} (HandleDeathVisuals): NavMeshAgent STOPPED and DISABLED.");
        }

        bool hasDieTrigger = false; 
        foreach (AnimatorControllerParameter param in npcAnimator.parameters) {
            if (param.name == "Die" && param.type == AnimatorControllerParameterType.Trigger) {
                hasDieTrigger = true; break;
            }
        }
        Debug.Log($"NPC {gameObject.name} Animator state BEFORE 'Die' trigger: {GetAnimatorStateName(npcAnimator)}. Has Die trigger: {hasDieTrigger}");

        npcAnimator.SetBool("Running", false); 
        npcAnimator.SetTrigger("Die"); 
        StartCoroutine(CheckAnimationStateAfterTrigger(npcAnimator, "TheManInTheCoatDeath", "Die"));
        
        ShowKillPrompt(false); 

        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            agent.ResetPath(); 
        }
        
        // Get death animation length for despawn timer
        float deathAnimationDuration = 0f;
        if (npcAnimator != null) {
            RuntimeAnimatorController ac = npcAnimator.runtimeAnimatorController;
            foreach (AnimationClip clip in ac.animationClips)
            {
                // Look for any clip whose name includes the word "Death" (case-insensitive)
                if (clip.name.IndexOf("death", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    deathAnimationDuration = clip.length;
                    Debug.Log($"NPC {gameObject.name}: Found death animation '{clip.name}' with duration: {deathAnimationDuration} for despawn timer.");
                    break;
                }
            }
        }
        if (deathAnimationDuration == 0f) {
            Debug.LogWarning("NPC {gameObject.name}: Could not find death animation clip length for despawn, defaulting to 2s.");
            deathAnimationDuration = 2f; // Fallback duration
        }

        // Despawn after animation finishes
        if (IsServer) // Only server should despawn NetworkObjects
        {
            StartCoroutine(DespawnAfterDelay(deathAnimationDuration));
        }
        else if (!IsServer && npcAnimator == null) // If client and no animator, might need to self-destruct if not networked properly
        {
            // This case should ideally not happen if networkIsAlive syncs correctly
            Debug.LogWarning($"NPC {gameObject.name} on client has no animator and will self-destruct after default delay as failsafe.");
             // Destroy(gameObject, deathAnimationDuration); // Avoid this if possible, server should control lifetime
        }
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        Debug.Log($"NPC {gameObject.name} (Server): Will despawn after {delay} seconds.");
        yield return new WaitForSeconds(delay);
        if (NetworkObject != null && NetworkObject.IsSpawned) // Check if still valid to despawn
        {
            Debug.Log($"NPC {gameObject.name} (Server): Despawning now.");
            NetworkObject.Despawn(true); // True to destroy the object across the network
        }
        else
        {
            Debug.LogWarning($"NPC {gameObject.name} (Server): Despawn requested, but NetworkObject is null or not spawned.");
        }
    }

    [ServerRpc(RequireOwnership = false)] 
    public void KillNPCServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!networkIsAlive.Value) return; 

        Debug.Log($"NPC {gameObject.name} received KillNPCServerRpc from client {rpcParams.Receive.SenderClientId}. Setting networkIsAlive to false.");
        
        if (agent != null && agent.enabled)
        {
            agent.isStopped = true; 
            agent.ResetPath();      
            agent.velocity = Vector3.zero; 
            agent.enabled = false; // <--- AGGRESSIVELY DISABLE AGENT ON SERVER
            Debug.Log($"NPC {gameObject.name} NavMeshAgent STOPPED and DISABLED by server.");
        }

        networkIsAlive.Value = false; 
        
        ProcessDeathClientRpc(); 
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            networkPosition.OnValueChanged -= ClientOnPositionChanged;
            networkIsMoving.OnValueChanged -= ClientOnIsMovingChanged;
            networkAgentDestination.OnValueChanged -= ClientOnAgentDestinationChanged;
            networkAgentDesiredVelocity.OnValueChanged -= ClientOnAgentDesiredVelocityChanged;
            networkSpriteColor.OnValueChanged -= ClientOnSpriteColorChanged;
            networkIsAlive.OnValueChanged -= ClientOnIsAliveChanged; // Unsubscribe
            // No networkSpriteFlipX OnValueChanged to remove as it wasn't added
            // No OnValueChanged for networkAgentDesiredVelocity
        }
        base.OnNetworkDespawn();
    }
} 