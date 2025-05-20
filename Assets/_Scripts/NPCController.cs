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

    [Header("Animation")]
    [Tooltip("Animator component from the NPC prefab. Expecting a 'Running' boolean parameter.")]
    public Animator npcAnimator;
    private SpriteRenderer characterSpriteRenderer; // Specifically for the character sprite
    private Rigidbody rb; // Moved Rigidbody declaration here

    // Network Variables
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(Vector3.zero, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> networkAgentDestination = new NetworkVariable<Vector3>(Vector3.zero, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkIsMoving = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkSpriteFlipX = new NetworkVariable<bool>(false, // For sprite orientation
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        
    // Internal State
    private NavMeshAgent agent;
    private float currentStateTimer;
    private bool serverIsRunningState;

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
            }
        }

        if (npcAnimator == null) npcAnimator = GetComponentInChildren<Animator>();
        
        // Find the Character child and its SpriteRenderer
        Transform characterTransform = transform.Find("Character");
        if (characterTransform != null)
        {
            characterSpriteRenderer = characterTransform.GetComponent<SpriteRenderer>();
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
            // No specific OnValueChanged for networkSpriteFlipX, clients read it in Update

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
            }
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
            ServerHandleSpriteOrientation(); // Server decides flip

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
            characterSpriteRenderer.flipX = networkSpriteFlipX.Value;
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
                ServerHandleSpriteOrientation(); // Ensure correct orientation immediately upon starting to run
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

    private void ServerHandleSpriteOrientation()
    {
        if (!IsServer) return; 

        // Ensure agent is valid, has a path, and is actually trying to move.
        // networkIsMoving.Value is checked to ensure we only try to flip if the NPC is in a "moving" state.
        if (networkIsMoving.Value && agent != null && agent.hasPath && agent.desiredVelocity.sqrMagnitude > 0.01f) 
        {
            // Use desiredVelocity for a more stable direction based on the path.
            Vector3 desiredMoveDirection = agent.desiredVelocity.normalized;
            float xMovement = desiredMoveDirection.x;
            
            // Threshold for how much x-component is needed to be considered "moving left/right".
            // If x-component of desired movement is less than this, flip state is preserved.
            // This prevents jitter if moving mostly along Z-axis (relative to world) or if x-movement is very slight.
            float flipThreshold = 0.15f; 

            if (xMovement > flipThreshold) // Desired movement is noticeably to the "world right"
            {
                // Only update if the state needs to change
                if (networkSpriteFlipX.Value) networkSpriteFlipX.Value = false;
            }
            else if (xMovement < -flipThreshold) // Desired movement is noticeably to the "world left"
            {
                // Only update if the state needs to change
                if (!networkSpriteFlipX.Value) networkSpriteFlipX.Value = true;
            }
            // If xMovement is between -flipThreshold and flipThreshold, the current flip state is maintained.
        }
        // If not actively moving as per networkIsMoving, or no path, or desiredVelocity is negligible, 
        // the flip state (networkSpriteFlipX.Value) is preserved from its last state.
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            networkPosition.OnValueChanged -= ClientOnPositionChanged;
            networkIsMoving.OnValueChanged -= ClientOnIsMovingChanged;
            networkAgentDestination.OnValueChanged -= ClientOnAgentDestinationChanged;
            // No networkSpriteFlipX OnValueChanged to remove as it wasn't added
        }
        base.OnNetworkDespawn();
    }
} 