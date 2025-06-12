using UnityEngine;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float runMultiplier = 2f;

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

    // Variables to track camera rotation and maintain movement direction
    private Vector3 lastMovementDirection = Vector3.zero;
    private float lastCameraRotationY = 0f;
    private bool isCameraRotating = false;

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
    private Rigidbody rb; // Add Rigidbody reference

    // This could be used to identify which character type this player is
    public NetworkVariable<int> characterIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private PlayerKill playerKill;

    // Public method to get the character index
    public int GetCharacterIndex()
    {
        return characterIndex.Value;
    }

    void Awake()
    {
        // Get components early
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
        playerKill = GetComponent<PlayerKill>();
        
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
    }

    void Start()
    {
        // Check if this is the local player
        isLocalPlayer = IsOwner;
        
        // Initialize stamina
        currentStamina = maxStamina;
        canSprint = true;

        // Debug log for network ownership
        Debug.Log($"Player initialized. IsOwner: {IsOwner}, NetworkObjectId: {NetworkObjectId}");
    }

    void Update()
    {
        if (!IsOwner) return; // Only the owner player can initiate kills

        // Try finding canvas in Update if Start failed
        // These lines were mistakenly added here and belong in PlayerKill.cs
        // if (uiCanvas == null)
        // {
        //     FindUICanvas();
        // }

        // If kill animation is playing OR if aiming, handle differently
        if (IsOwner)
        {
            if (PlayerKill.IsKillAnimationPlaying)
            {
                // Optionally, ensure player is not moving if caught mid-movement by animation start
                if (rb != null) 
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                if (animator != null) 
                {
                    animator.SetBool("Running", false); // Ensure running animation is off
                }
                return; // Skip normal movement and input processing
            }
            
            // Check if player is aiming
            if (PlayerKill.IsAiming)
            {
                // Root player in place handled in PlayerController
                // The rooting logic is in PlayerController's Update method already
                
                // Handle aiming, shooting, and exiting aim mode is handled by PlayerKill
                // We only need to ensure movement is stopped here.
                
                // Ensure movement is stopped while aiming
                if (rb != null)
                {
                     rb.velocity = Vector3.zero;
                     rb.angularVelocity = Vector3.zero;
                }
                if (animator != null)
                {
                    animator.SetBool("Running", false);
                }
                
                // DON'T return here, allow camera facing logic and networking to run
                // return; // REMOVED
            }
        }

        // Only process input for the local player if not aiming or performing kill
        if (IsOwner && !PlayerKill.IsAiming && !PlayerKill.IsKillAnimationPlaying)
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
            
            // Check if the camera is rotating
            Camera playerCamera = Camera.main;
            if (playerCamera != null)
            {
                float currentCameraRotationY = playerCamera.transform.eulerAngles.y;
                
                // Detect if camera rotation has changed
                if (Mathf.Abs(currentCameraRotationY - lastCameraRotationY) > 0.1f)
                {
                    // Camera is rotating
                    isCameraRotating = true;
                    
                    // If we have a valid last movement direction and we're still moving
                    if (lastMovementDirection.magnitude > 0.1f && inputDirection.magnitude > 0.1f)
                    {
                        // Use the last world-space movement direction instead of calculating a new one
                        Vector3 currentMovement = lastMovementDirection;
                        lastCameraRotationY = currentCameraRotationY;
                        
                        // Apply movement
                        // transform.position += currentMovement * currentSpeed * Time.deltaTime;
                        rb.MovePosition(rb.position + currentMovement * currentSpeed * Time.deltaTime);
                        
                        // Skip the rest of the movement code
                        goto SkipMovement;
                    }
                }
                else
                {
                    // Camera has stopped rotating
                    if (isCameraRotating && inputDirection.magnitude < 0.1f)
                    {
                        // Player has released movement keys after camera rotation
                        isCameraRotating = false;
                    }
                }
                
                // Update last camera rotation
                lastCameraRotationY = currentCameraRotationY;
            }
            
            // Convert input direction to be relative to camera orientation
            Vector3 movement = GetCameraRelativeMovement(inputDirection);
            
            // Store the world-space movement direction for use during camera rotation
            if (movement.magnitude > 0.1f)
            {
                lastMovementDirection = movement;
            }
            
            // Apply movement
            // transform.position += movement * currentSpeed * Time.deltaTime;
            rb.MovePosition(rb.position + movement * currentSpeed * Time.deltaTime);
            
        SkipMovement:

            // Update animator if we have one
            if (animator != null)
            {
                animator.SetBool("Running", isMoving);
            }

            // Always make sprite face the camera (billboard effect)
            if (characterSprite != null)
            {
                // For movement-based flipping (optional - can be removed if you want sprites to always face one direction)
                if (moveX != 0)
                {
                    // Determine if character should face left or right based on camera-relative movement
                    Camera viewCamera = Camera.main;
                    if (viewCamera != null)
                    {
                        // Get the current movement direction (must be initialized before this point)
                        Vector3 currentMovement = lastMovementDirection;
                        if (currentMovement.magnitude < 0.1f)
                        {
                            // If we don't have a valid movement direction, use the input direction
                            currentMovement = GetCameraRelativeMovement(inputDirection);
                        }
                        
                        // Project the movement onto the camera's right vector to determine if moving left or right relative to camera
                        float rightMovement = Vector3.Dot(currentMovement, viewCamera.transform.right);
                        isFacingLeft = (rightMovement < 0);
                        characterSprite.flipX = isFacingLeft;
                    }
                    else
                    {
                        // Fallback to world-space if camera not found
                        isFacingLeft = (moveX < 0);
                        characterSprite.flipX = isFacingLeft;
                    }
                }
                
                // Make the sprite face the camera
                if (characterSprite.transform.parent != null)
                {
                    // Keep the sprite facing the camera while preserving the parent's position
                    characterSprite.transform.rotation = Camera.main != null ? 
                        Quaternion.LookRotation(Camera.main.transform.forward) : Quaternion.identity;
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

            // If no input, explicitly stop the rigidbody's velocity to prevent sliding
            if (!isMoving && rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero; // Also stop any rotation
            }

            // Finally, update the stamina bar fill
            UpdateStaminaBar();
        }
        else
        {
            // For non-local players, update position and animation based on network variables
            // transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
            if (rb != null) // Ensure Rigidbody exists before trying to move it
            {
                rb.MovePosition(Vector3.Lerp(rb.position, networkPosition.Value, Time.deltaTime * 10f));
            } else { // Fallback for safety, though rb should exist
                 transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
            }
            
            // Update animator
            if (animator != null)
            {
                animator.SetBool("Running", networkIsRunning.Value);
            }
            
            // Update sprite direction
            if (characterSprite != null)
            {
                // Set the flip based on network value
                characterSprite.flipX = networkIsFacingLeft.Value;
                
                // Make the sprite face the camera
                if (characterSprite.transform.parent != null)
                {
                    // Keep the sprite facing the camera while preserving the parent's position
                    characterSprite.transform.rotation = Camera.main != null ? 
                        Quaternion.LookRotation(Camera.main.transform.forward) : Quaternion.identity;
                }
            }
        }
    }

    [ServerRpc]
    private void UpdatePositionServerRpc(Vector3 position, bool isRunning, bool isFacingLeft)
    {
        networkPosition.Value = position; // Server still receives and sets the target position
        networkIsRunning.Value = isRunning;
        networkIsFacingLeft.Value = isFacingLeft;
    }
    
    // Convert input direction to be relative to camera orientation
    private Vector3 GetCameraRelativeMovement(Vector3 inputDirection)
    {
        if (inputDirection.magnitude == 0) return Vector3.zero;
        
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return inputDirection; // Fallback to world space if no camera
        
        // Get the camera's forward and right vectors, but ignore Y component to keep movement on XZ plane
        Vector3 cameraForward = mainCamera.transform.forward;
        Vector3 cameraRight = mainCamera.transform.right;
        
        // Project to XZ plane
        cameraForward.y = 0;
        cameraRight.y = 0;
        cameraForward.Normalize();
        cameraRight.Normalize();
        
        // Calculate the movement direction relative to the camera
        Vector3 moveDirection = (cameraForward * inputDirection.z + cameraRight * inputDirection.x).normalized;
        
        return moveDirection;
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

        // Subscribe to network variable changes
        characterIndex.OnValueChanged += OnCharacterIndexChanged;

        // If this is the local player, find the camera and assign ourselves as the target AND find the HUD elements
        if (IsOwner)
        {
            Debug.Log($"PlayerController: Local player spawned at {transform.position}");
            
            // Manually trigger the icon update for the initial value, since OnValueChanged only triggers on change.
            OnCharacterIndexChanged(0, characterIndex.Value);
            
            // Find the main camera and set its target to this player
            CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
            if (cameraFollow != null)
            {
                Debug.Log("PlayerController: Found CameraFollow, setting target");
                cameraFollow.SetTarget(transform);
                cameraFollow.ResetCameraPosition();
            }
            else
            {
                Debug.LogWarning("PlayerController: Could not find CameraFollow component on main camera");
            }

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
                try {
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
                            Debug.LogWarning("PlayerController: Failed to get PlayerHUDController component from instantiated prefab");
                        }
                    }
                }
                catch (System.Exception) {
                    // Silently continue if Resources folder doesn't exist
                    Debug.LogWarning("PlayerController: Unable to load PlayerHUDController prefab. Game will continue without HUD.");
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
                characterIndex.Value = NetworkManagerUI.Instance.GetClientCharacterIndex(OwnerClientId);
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
                    try {
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
                        }
                    }
                    catch (System.Exception) {
                        // Create a basic GameManager if we can't load the prefab
                        Debug.LogWarning("PlayerController: Unable to load GameManager prefab. Creating a basic GameManager.");
                        GameObject gmObject = new GameObject("GameManager");
                        DontDestroyOnLoad(gmObject);
                        gameManager = gmObject.AddComponent<GameManager>();
                        if (gameManager != null)
                        {
                            gameManager.RegisterPlayer(this);
                        }
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
        
        if (characterIndex != null)
        {
            characterIndex.OnValueChanged -= OnCharacterIndexChanged;
        }
        
        if (pursuersIds != null)
        {
            pursuersIds.OnListChanged -= OnPursuersChanged;
        }

        // Clean up other callbacks or references as needed
    }

    private void OnCharacterIndexChanged(int previousValue, int newValue)
    {
        // Only the owner should update their HUD.
        if (!IsOwner) return;

        if (playerKill == null)
        {
            Debug.LogError("PlayerController could not find the PlayerKill component to update the weapon icon!");
            return;
        }
    
        Debug.Log($"OnCharacterIndexChanged: new index is {newValue}. Updating weapon icon.");
        playerKill.UpdateWeaponIcon(newValue);
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
                    int targetCharacterIndex = targetPlayer.characterIndex.Value;
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

    // Public method to set the player's facing direction (used for aiming)
    public void SetFacingLeft(bool facingLeft)
    {
        Debug.Log($"PlayerController: SetFacingLeft called with: {facingLeft}");
        if (isFacingLeft != facingLeft)
        {
            isFacingLeft = facingLeft;
            
            // Also update sprite flip immediately for local player
            if (IsOwner && characterSprite != null)
            {
                characterSprite.flipX = isFacingLeft;
            }

            // Sync the facing direction to other clients
            if (IsOwner)
            {
                UpdatePositionServerRpc(transform.position, isMoving, isFacingLeft); // Reuse existing RPC
            }
        }
    }

    #endregion
}