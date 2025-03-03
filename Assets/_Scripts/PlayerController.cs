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
    public RectTransform staminaBarFill;    // Assign your StaminaBar (the white fill) here

    // Network variables to sync across clients
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<bool> networkIsRunning = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> networkIsFacingLeft = new NetworkVariable<bool>(false);

    private float currentStamina;
    private bool canSprint = true;          // Track if player can start sprinting
    private Animator animator;
    private SpriteRenderer characterSprite;
    private bool isLocalPlayer;
    private bool isMoving = false;
    private bool isFacingLeft = false;
    private bool isRunning = false;

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

        // Make sure stamina bar is full at start
        UpdateStaminaBar();
        
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
            Vector3 movement = new Vector3(moveX, 0, moveZ).normalized;
            
            // Apply movement
            transform.position += movement * currentSpeed * Time.deltaTime;

            // Update animator if we have one
            if (animator != null)
            {
                animator.SetBool("Running", isMoving);
            }

            // Update sprite direction if we have a sprite renderer
            if (characterSprite != null && moveX != 0)
            {
                isFacingLeft = (moveX < 0);
                characterSprite.flipX = isFacingLeft;
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
        
        // If this is the local player, find the camera and assign ourselves as the target
        if (IsOwner)
        {
            Debug.Log($"PlayerController: Local player spawned at {transform.position}");
            
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
        }
        
        // Initialize network variables
        if (IsServer)
        {
            networkPosition.Value = transform.position;
        }
    }
}