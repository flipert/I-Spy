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
        
    // Internal State
    private NavMeshAgent agent;
    private float currentStateTimer;
    private bool serverIsRunningState;

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
        // NavMeshAgent is retrieved in OnNetworkSpawn or can be here too if preferred.
        // For now, keeping agent retrieval in OnNetworkSpawn as it was.
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

            DecideNextState();
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
        if (npcAnimator != null) npcAnimator.SetBool("Running", networkIsMoving.Value);
        if (characterSpriteRenderer != null) characterSpriteRenderer.color = networkSpriteColor.Value; // Apply initial color
    }


    void Update()
    {
        if (IsServer)
        {
            currentStateTimer -= Time.deltaTime;
            if (currentStateTimer <= 0)
            {
                DecideNextState();
            }

            networkPosition.Value = transform.position;
            ServerUpdateMovementDirection(); // Server updates movement direction for clients

            if (serverIsRunningState && agent != null && !agent.pathPending)
            {
                if (agent.remainingDistance <= agent.stoppingDistance)
                {
                    if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
                    {
                        // Reached destination early
                        currentStateTimer = 0; // Force state change
                        // Debug.Log($"NPC {gameObject.name} reached destination early, switching state.");
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

    private void DecideNextState()
    {
        if (!IsServer) return;

        if (serverIsRunningState) // Was running, now idle
        {
            TransitionToIdle();
        }
        else // Was idle, now run
        {
            TransitionToRunning();
        }
    }

    private void TransitionToIdle()
    {
        if (!IsServer) return;

        // Debug.Log($"NPC {gameObject.name} transitioning to IDLE.");
        serverIsRunningState = false;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        networkIsMoving.Value = false;
        if (npcAnimator != null) npcAnimator.SetBool("Running", false);
        currentStateTimer = Random.Range(1f, maxIdleTime);
        networkAgentDesiredVelocity.Value = Vector3.zero; // NPC is idle, no desired velocity
        networkAgentDestination.OnValueChanged -= ClientOnAgentDestinationChanged;
        if(networkSpriteColor != null) networkSpriteColor.OnValueChanged -= ClientOnSpriteColorChanged;
    }

    private void TransitionToRunning()
    {
        if (!IsServer) return;

        // Debug.Log($"NPC {gameObject.name} transitioning to RUNNING.");
        serverIsRunningState = true;
        if (agent != null && agent.isOnNavMesh)
        {
            Vector3 randomNavMeshPoint = PickRandomNavMeshLocation(wanderRadius);
            if (randomNavMeshPoint != Vector3.zero) // Vector3.zero indicates failure to find point
            {
                agent.SetDestination(randomNavMeshPoint);
                agent.isStopped = false;
                networkAgentDestination.Value = randomNavMeshPoint;
                networkIsMoving.Value = true;
                if (npcAnimator != null) npcAnimator.SetBool("Running", true);
                // ServerHandleSpriteOrientation(); // Ensure correct orientation immediately upon starting to run
                // ServerUpdateMovementDirection will pick this up in the next server Update
            }
            else
            {
                // Failed to find a point, stay idle for a bit longer
                // Debug.LogWarning($"NPC {gameObject.name} failed to find a NavMesh point. Staying idle.");
                serverIsRunningState = false; // Revert to idle state logic for this cycle
                networkIsMoving.Value = false;
                if (npcAnimator != null) npcAnimator.SetBool("Running", false);
                // currentStateTimer will be positive from previous idle, or we can set a short one
                currentStateTimer = Random.Range(1f, maxIdleTime / 2f); // shorter idle if failed to run
                return; // Skip setting full run timer
            }
        }
        else
        {
             // Agent not ready, go back to idle for safety
            // Debug.LogWarning($"NPC {gameObject.name} NavMeshAgent not ready. Staying idle.");
            TransitionToIdle(); // Try to idle instead
                    return;
        }
        currentStateTimer = Random.Range(1f, maxRunTime);
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

        if (networkIsMoving.Value && agent != null && agent.hasPath && agent.desiredVelocity.sqrMagnitude > 0.01f) 
        {
            networkAgentDesiredVelocity.Value = agent.desiredVelocity.normalized;
        }
        else
        {
            // If not moving, or no path, or desiredVelocity is negligible, 
            // set networked desired velocity to zero so clients know.
            if (networkAgentDesiredVelocity.Value != Vector3.zero)
            {
                networkAgentDesiredVelocity.Value = Vector3.zero;
            }
        }
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
            // No networkSpriteFlipX OnValueChanged to remove as it wasn't added
            // No OnValueChanged for networkAgentDesiredVelocity
        }
        base.OnNetworkDespawn();
    }
} 