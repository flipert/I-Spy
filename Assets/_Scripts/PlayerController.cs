using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float runMultiplier = 2f;
    [Tooltip("Tag used to identify buildings that player should not pass through")]
    public string buildingTag = "Building";

    [Header("Stamina Settings")]
    public float maxStamina = 100f;         // Max stamina
    public float staminaDepletionRate = 15f; // How fast stamina goes down when sprinting
    public float staminaRegenRate = 5f;     // How fast stamina refills when not sprinting
    public float minStaminaToSprint = 20f;  // Minimum stamina needed to START sprinting again
    private RectTransform staminaBarFill;    // Keep it private

    // Network variables to sync across clients
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<bool> networkIsRunning = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> networkIsFacingLeft = new NetworkVariable<bool>(false);

    // New network variables for targeting system
    private NetworkVariable<ulong> currentTargetId = new NetworkVariable<ulong>(ulong.MaxValue); // ulong.MaxValue means "no target"
    // NetworkList must be initialized in the declaration for Netcode
    private NetworkList<ulong> pursuersIds = new NetworkList<ulong>();

    // Reference to the HUD controller
    private PlayerHUDController hudController;

    private float currentStamina;
    private bool canSprint = true;          // Track if player can start sprinting
    private Animator animator;
    private SpriteRenderer characterSprite;
    private bool isLocalPlayer;
    private bool isMoving = false;
    private bool isFacingLeft = false;
    private bool isRunning = false;

    // This could be used to identify which character type this player is
    public int characterIndex { get; private set; } = 0;

    void Start()
    {
        // Check if this is the local player
        isLocalPlayer = IsOwner;
        
        // Initialize stamina
        currentStamina = maxStamina;
        canSprint = true;

        // Get the Animator on the same GameObject
        animator = GetComponent<Animator>();

        // Get the child named "Character" for flipping sprite
        Transform characterTransform = transform.Find("Character");
        if (characterTransform != null)
        {
            characterSprite = characterTransform.GetComponent<SpriteRenderer>();
        }
        else
        {
            Debug.LogError("Child 'Character' not found. Please ensure your player has a child named 'Character' with a SpriteRenderer.");
        }

        // Make sure stamina bar is full at start - Will be updated in OnNetworkSpawn for local player
        // UpdateStaminaBar(); // Moved to OnNetworkSpawn
        
        // Debug log for network ownership
        Debug.Log($"Player initialized. IsOwner: {IsOwner}, NetworkObjectId: {NetworkObjectId}");
    }

    void Update()
    {
        // Only process input for the local player
        if (IsOwner)
        {
            // Get input
            float moveX = Input.GetAxisRaw("Horizontal");
            float moveZ = Input.GetAxisRaw("Vertical");

            // Determine if we're moving
            isMoving = (moveX != 0 || moveZ != 0);

            // Determine if we're running (holding Shift and have enough stamina)
            bool wantsToRun = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            
            // Can only start sprinting if we have enough stamina
            if (wantsToRun && currentStamina < minStaminaToSprint)
            {
                wantsToRun = false;
                canSprint = false;
            }
            
            // If we have enough stamina again, allow sprinting
            if (!canSprint && currentStamina >= minStaminaToSprint)
            {
                canSprint = true;
            }
            
            // Only run if we're moving, want to run, and can sprint
            isRunning = isMoving && wantsToRun && canSprint;

            // Calculate movement speed
            float currentSpeed = moveSpeed;
            if (isRunning)
            {
                currentSpeed *= runMultiplier;
                // Deplete stamina while running
                currentStamina = Mathf.Max(0, currentStamina - (staminaDepletionRate * Time.deltaTime));
                if (currentStamina <= 0)
                {
                    canSprint = false;
                }
            }
            else if (!isMoving || !wantsToRun)
            {
                // Regenerate stamina when not running
                currentStamina = Mathf.Min(maxStamina, currentStamina + (staminaRegenRate * Time.deltaTime));
            }

            // Create movement vector using X and Z axes (keeping Y the same)
            Vector3 inputDirection = new Vector3(moveX, 0, moveZ).normalized;
            Vector3 movement = inputDirection; // Default to input direction if no camera is found
            Vector3 proposedPosition = transform.position;
            
            // Convert input direction to be relative to camera orientation
            Camera mainCamera = Camera.main;
            if (mainCamera != null && inputDirection.magnitude > 0)
            {
                // Get the camera's forward and right vectors, but ignore Y component (keep movement on ground plane)
                Vector3 cameraForward = mainCamera.transform.forward;
                cameraForward.y = 0;
                cameraForward.Normalize();
                
                Vector3 cameraRight = mainCamera.transform.right;
                cameraRight.y = 0;
                cameraRight.Normalize();
                
                // Calculate the movement direction relative to camera
                movement = (cameraForward * inputDirection.z + cameraRight * inputDirection.x).normalized;
                
                // Update character facing direction based on movement
                if (movement.x != 0)
                {
                    isFacingLeft = (movement.x < 0);
                }
            }
            
            // Calculate the proposed new position
            if (movement.magnitude > 0)
            {
                proposedPosition = transform.position + movement * currentSpeed * Time.deltaTime;
            }
            
            // Check for collisions before moving
            if (movement.magnitude > 0)
            {
                // Cast a ray in the movement direction to check for obstacles
                RaycastHit hit;
                float rayDistance = currentSpeed * Time.deltaTime * 1.5f; // Look slightly ahead
                bool hitObstacle = Physics.Raycast(transform.position, movement, out hit, rayDistance);
                
                // Only move if we won't hit a building
                if (!hitObstacle || !hit.collider.CompareTag(buildingTag))
                {
                    // Apply movement
                    transform.position = proposedPosition;
                }
                else
                {
                    // We hit a building, try to slide along it
                    Vector3 reflectionDirection = Vector3.Reflect(movement, hit.normal);
                    reflectionDirection.y = 0; // Keep movement on the ground plane
                    
                    // Try moving along the wall
                    Vector3 slideDirection = Vector3.Cross(hit.normal, Vector3.up).normalized;
                    if (Vector3.Dot(movement, slideDirection) < 0)
                    {
                        slideDirection = -slideDirection;
                    }
                    
                    // Check if we can slide in this direction
                    bool canSlide = !Physics.Raycast(transform.position, slideDirection, rayDistance);
                    if (canSlide)
                    {
                        // Apply sliding movement
                        transform.position += slideDirection * currentSpeed * Time.deltaTime * 0.8f;
                    }
                }
            }

            // Update animator if we have one
            if (animator != null)
            {
                animator.SetBool("Running", isMoving);
            }

            // Make the character sprite always face the camera (billboarding)
            if (characterSprite != null && mainCamera != null)
            {
                // Get the character transform (parent of the sprite)
                Transform characterTransform = characterSprite.transform;
                
                // Get the camera's Y rotation
                float cameraYRotation = mainCamera.transform.rotation.eulerAngles.y;
                
                // Set the character's rotation to match camera's Y rotation
                characterTransform.rotation = Quaternion.Euler(0, cameraYRotation, 0);
                
                // Still use flipX for left/right movement direction
                characterSprite.flipX = isFacingLeft;
                
                // Find and handle the shadow object
                Transform shadowTransform = transform.Find("Shadow");
                if (shadowTransform != null)
                {
                    // Keep the shadow flat on the ground (no rotation)
                    shadowTransform.rotation = Quaternion.Euler(90, 0, 0);
                }
            }

            // Update network variables to sync with other clients
            if (IsServer)
            {
                networkPosition.Value = transform.position;
                networkIsRunning.Value = isMoving;
                networkIsFacingLeft.Value = isFacingLeft;
            }
            else
            {
                UpdatePositionServerRpc(transform.position, isMoving, isFacingLeft);
            }

            // Finally, update the stamina bar fill
            UpdateStaminaBar();
        }
        else
        {
            // For non-local players, update position and animation based on network variables
            transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
            
            // Update animator
            if (animator != null)
            {
                animator.SetBool("Running", networkIsRunning.Value);
            }
            
            // Update sprite direction
            if (characterSprite != null)
            {
                characterSprite.flipX = networkIsFacingLeft.Value;
            }
        }
    }

    [ServerRpc]
    private void UpdatePositionServerRpc(Vector3 position, bool isRunning, bool isFacingLeft)
    {
        networkPosition.Value = position;
        networkIsRunning.Value = isRunning;
        networkIsFacingLeft.Value = isFacingLeft;
    }

    /// <summary>
    /// Updates the local scale of the stamina bar to match currentStamina/maxStamina.
    /// Make sure the pivot X is set to 0 (left side) on the RectTransform so it shrinks from right to left.
    /// </summary>
    private void UpdateStaminaBar()
    {
        // Add a null check here too, just in case it wasn't found during spawn
        if (staminaBarFill == null) return;

        float ratio = currentStamina / maxStamina;
        // Scale only the X dimension
        staminaBarFill.localScale = new Vector3(ratio, 1f, 1f);
    }

    // Public function to update the minimum stamina required to start sprinting.
    public void SetMinStaminaToSprint(float value)
    {
        minStaminaToSprint = value;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // No need to initialize NetworkList here anymore
        // if (pursuersIds == null)
        // {
        //     pursuersIds = new NetworkList<ulong>();
        // }

        // If this is the local player, find the camera and assign ourselves as the target AND find the HUD elements
        if (IsOwner)
        {
            Debug.Log($"PlayerController: Local player spawned at {transform.position}");
            
            // Start a coroutine to find and set up the camera with retries
            StartCoroutine(FindAndSetupCamera());
            
            // Find the stamina bar UI element in the scene using its tag
            GameObject staminaBarObject = GameObject.FindGameObjectWithTag("StaminaBarFill"); // Make sure this tag exists and is assigned in the scene
            if (staminaBarObject != null)
            {
                staminaBarFill = staminaBarObject.GetComponent<RectTransform>();
                if (staminaBarFill != null)
                {
                    Debug.Log("PlayerController: Found and assigned StaminaBarFill RectTransform.");
                    // Initialize the bar state correctly now that we found it
                    UpdateStaminaBar();
                }
                else
                {
                    Debug.LogError("PlayerController: Found GameObject with tag 'StaminaBarFill', but it lacks a RectTransform component.");
                }
            }
            else
            {
                Debug.LogWarning("PlayerController: Could not find GameObject with tag 'StaminaBarFill'. Make sure the UI element exists in the scene and has the correct tag.");
            }

            // Find and setup the HUD controller
            hudController = FindObjectOfType<PlayerHUDController>();
            if (hudController == null)
            {
                Debug.Log("PlayerController: Attempting to create PlayerHUDController since it wasn't found in the scene");
                
                // Try to instantiate PlayerHUDController from prefab
                GameObject hudPrefab = Resources.Load<GameObject>("Prefabs/PlayerHUD");
                if (hudPrefab != null)
                {
                    GameObject hudObject = Instantiate(hudPrefab);
                    hudController = hudObject.GetComponent<PlayerHUDController>();
                    if (hudController != null)
                    {
                        Debug.Log("PlayerController: Successfully created PlayerHUDController from prefab");
                        hudController.Initialize();
                    }
                    else
                    {
                        Debug.LogError("PlayerController: Failed to get PlayerHUDController component from instantiated prefab");
                    }
                }
                else
                {
                    Debug.LogError("PlayerController: Could not find PlayerHUDController prefab in Resources/Prefabs/PlayerHUD");
                }
            }
            else
            {
                hudController.Initialize();
            }
        }

        // If this is the server, register this player with the GameManager
        if (IsServer)
        {
            // Get the character index from NetworkManagerUI if available
            if (NetworkManagerUI.Instance != null)
            {
                characterIndex = NetworkManagerUI.Instance.GetClientCharacterIndex(OwnerClientId);
            }

            // Register with GameManager
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RegisterPlayer(this);
            }
            else
            {
                Debug.Log("PlayerController: Attempting to find or create GameManager since it wasn't found");
                
                // First try to find it - maybe it exists but the reference wasn't set up
                GameManager gameManager = FindObjectOfType<GameManager>();
                if (gameManager != null)
                {
                    gameManager.RegisterPlayer(this);
                    Debug.Log("PlayerController: Found existing GameManager and registered player");
                }
                else
                {
                    // Try to instantiate GameManager from prefab
                    GameObject gmPrefab = Resources.Load<GameObject>("Prefabs/GameManager");
                    if (gmPrefab != null)
                    {
                        GameObject gmObject = Instantiate(gmPrefab);
                        DontDestroyOnLoad(gmObject);
                        gameManager = gmObject.GetComponent<GameManager>();
                        if (gameManager != null)
                        {
                            gameManager.RegisterPlayer(this);
                            Debug.Log("PlayerController: Created GameManager from prefab and registered player");
                        }
                        else
                        {
                            Debug.LogError("PlayerController: Failed to get GameManager component from instantiated prefab");
                        }
                    }
                    else
                    {
                        Debug.LogError("PlayerController: Could not find GameManager prefab in Resources/Prefabs/GameManager");
                    }
                }
            }
        }

        // Subscribe to network variable changes
        currentTargetId.OnValueChanged += OnTargetChanged;
        // Fix: Subscribe to NetworkList events directly
        pursuersIds.OnListChanged += OnPursuersChanged;

        // Initialize network variables
        if (IsServer)
        {
            networkPosition.Value = transform.position;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Add null checks before unsubscribing
        if (currentTargetId != null)
        {
            currentTargetId.OnValueChanged -= OnTargetChanged;
        }
        
        if (pursuersIds != null)
        {
            pursuersIds.OnListChanged -= OnPursuersChanged;
        }

        // Clean up other callbacks or references as needed
    }

    // Called on all clients when the current target changes
    private void OnTargetChanged(ulong previousTarget, ulong newTarget)
    {
        if (!IsOwner) return; // Only update UI for the local player

        Debug.Log($"PlayerController: Target changed from {previousTarget} to {newTarget}");

        if (hudController != null)
        {
            if (newTarget == ulong.MaxValue)
            {
                // No target assigned
                hudController.SetTargetLoadingState(false, false);
            }
            else
            {
                // New target assigned, show loading indicator while finding target details
                hudController.SetTargetLoadingState(true, true);

                // Get the character index of the target player
                // This might need to be implemented differently based on your architecture
                PlayerController targetPlayer = GetPlayerByNetworkId(newTarget);
                if (targetPlayer != null)
                {
                    int targetCharacterIndex = targetPlayer.characterIndex;
                    hudController.SetTargetCharacter(targetCharacterIndex);
                }
                else
                {
                    // If we can't find the player yet, we'll keep showing the loading state
                    // The UI might be updated later when we have more info
                }
            }
        }
    }

    // Called on all clients when the list of pursuers changes
    private void OnPursuersChanged(NetworkListEvent<ulong> changeEvent)
    {
        if (!IsOwner) return; // Only update UI for the local player

        Debug.Log($"PlayerController: Pursuers list changed");

        if (hudController != null)
        {
            // Convert NetworkList to array for the HUD controller
            ulong[] pursuers = new ulong[pursuersIds.Count];
            for (int i = 0; i < pursuersIds.Count; i++)
            {
                pursuers[i] = pursuersIds[i];
            }
            hudController.UpdatePursuers(pursuers);
        }
    }

    // Coroutine to find and set up the camera with retries
    private IEnumerator FindAndSetupCamera()
    {
        int maxRetries = 5;
        int retryCount = 0;
        float retryDelay = 0.5f;
        bool cameraFound = false;
        
        // Wait a moment for the scene to fully load
        yield return new WaitForSeconds(0.2f);
        
        while (!cameraFound && retryCount < maxRetries)
        {
            // First try Camera.main
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                // Try to find ThirdPersonCamera
                ThirdPersonCamera thirdPersonCamera = mainCamera.GetComponent<ThirdPersonCamera>();
                if (thirdPersonCamera != null)
                {
                    Debug.Log("PlayerController: Found ThirdPersonCamera on Camera.main, setting target");
                    thirdPersonCamera.SetTarget(transform);
                    thirdPersonCamera.ResetCameraPosition();
                    cameraFound = true;
                    yield break;
                }
                
                // Try to find CameraFollow
                CameraFollow cameraFollow = mainCamera.GetComponent<CameraFollow>();
                if (cameraFollow != null)
                {
                    Debug.Log("PlayerController: Found CameraFollow on Camera.main, setting target");
                    cameraFollow.SetTarget(transform);
                    cameraFollow.ResetCameraPosition();
                    cameraFound = true;
                    yield break;
                }
            }
            
            // If Camera.main failed, try FindObjectOfType
            ThirdPersonCamera[] allThirdPersonCameras = FindObjectsOfType<ThirdPersonCamera>();
            if (allThirdPersonCameras.Length > 0)
            {
                Debug.Log("PlayerController: Found ThirdPersonCamera using FindObjectsOfType, setting target");
                allThirdPersonCameras[0].SetTarget(transform);
                allThirdPersonCameras[0].ResetCameraPosition();
                cameraFound = true;
                yield break;
            }
            
            CameraFollow[] allCameraFollows = FindObjectsOfType<CameraFollow>();
            if (allCameraFollows.Length > 0)
            {
                Debug.Log("PlayerController: Found CameraFollow using FindObjectsOfType, setting target");
                allCameraFollows[0].SetTarget(transform);
                allCameraFollows[0].ResetCameraPosition();
                cameraFound = true;
                yield break;
            }
            
            // Try cameras with specific tags
            GameObject taggedCamera = GameObject.FindGameObjectWithTag("MainCamera");
            if (taggedCamera != null)
            {
                ThirdPersonCamera taggedThirdPersonCamera = taggedCamera.GetComponent<ThirdPersonCamera>();
                if (taggedThirdPersonCamera != null)
                {
                    Debug.Log("PlayerController: Found ThirdPersonCamera using tag, setting target");
                    taggedThirdPersonCamera.SetTarget(transform);
                    taggedThirdPersonCamera.ResetCameraPosition();
                    cameraFound = true;
                    yield break;
                }
                
                CameraFollow taggedCameraFollow = taggedCamera.GetComponent<CameraFollow>();
                if (taggedCameraFollow != null)
                {
                    Debug.Log("PlayerController: Found CameraFollow using tag, setting target");
                    taggedCameraFollow.SetTarget(transform);
                    taggedCameraFollow.ResetCameraPosition();
                    cameraFound = true;
                    yield break;
                }
            }
            
            // If we get here, no camera was found this attempt
            retryCount++;
            Debug.Log($"PlayerController: Camera not found, retry {retryCount}/{maxRetries}");
            yield return new WaitForSeconds(retryDelay);
        }
        
        if (!cameraFound)
        {
            Debug.LogWarning("PlayerController: Could not find either ThirdPersonCamera or CameraFollow component after multiple attempts");
        }
    }
    
    // Helper method to find a player by their network ID
    private PlayerController GetPlayerByNetworkId(ulong networkId)
    {
        // This is a simple implementation; you may need to adjust based on your game's structure
        PlayerController[] allPlayers = FindObjectsOfType<PlayerController>();
        foreach (PlayerController player in allPlayers)
        {
            if (player.OwnerClientId == networkId)
            {
                return player;
            }
        }
        return null;
    }

    #region ClientRpc Methods (Called by Server)

    // Called on all clients to update a player's target
    [ClientRpc]
    public void SetTargetClientRpc(ulong targetId)
    {
        if (IsServer) 
        {
            // If we're the server, directly update the network variable
            currentTargetId.Value = targetId;
        }
        else
        {
            // This will fire the OnTargetChanged callback
            Debug.Log($"PlayerController: Received SetTargetClientRpc with target ID {targetId}");
            // For non-server clients, we don't need to do anything as they'll receive the networkvar change
        }
    }

    // Called on all clients to update the pursuers list
    [ClientRpc]
    public void UpdatePursuersClientRpc(ulong[] newPursuers)
    {
        // NetworkList is now always initialized in the field declaration
        // if (pursuersIds == null)
        // {
        //    pursuersIds = new NetworkList<ulong>();
        // }

        // Update pursuers list
        pursuersIds.Clear();
        foreach (ulong pursuerId in newPursuers)
        {
            pursuersIds.Add(pursuerId);
        }
        
        Debug.Log($"PlayerController: Received UpdatePursuersClientRpc with {newPursuers.Length} pursuers");
    }

    #endregion

    #region Gameplay Methods

    // Call this method when this player kills another player
    public void KillTarget(PlayerController target)
    {
        if (!IsServer)
        {
            // If not server, send RPC to server
            KillTargetServerRpc(target.OwnerClientId);
            return;
        }

        if (target.OwnerClientId == currentTargetId.Value)
        {
            // Inform GameManager about the kill
            if (GameManager.Instance != null)
            {
                GameManager.Instance.PlayerKilledTarget(OwnerClientId, target.OwnerClientId);
            }
            
            // Additional kill logic can go here (e.g., score, effects, etc.)
        }
        else
        {
            Debug.LogWarning($"PlayerController: Player {OwnerClientId} tried to kill {target.OwnerClientId} who is not their current target!");
        }
    }

    // Server RPC for non-server client to request a kill
    [ServerRpc]
    private void KillTargetServerRpc(ulong targetId)
    {
        // Validate the kill request
        if (targetId == currentTargetId.Value)
        {
            // Find the target player
            PlayerController targetPlayer = GetPlayerByNetworkId(targetId);
            if (targetPlayer != null)
            {
                KillTarget(targetPlayer);
            }
        }
    }

    #endregion
}