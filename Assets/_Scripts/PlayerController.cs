using UnityEngine;

public class PlayerController : MonoBehaviour
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

    private float currentStamina;
    private bool canSprint = true;          // Track if player can start sprinting
    private Animator animator;
    private SpriteRenderer characterSprite;

    void Start()
    {
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
    }

    void Update()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");

        // For top-down movement on XZ plane
        Vector3 movement = new Vector3(moveX, 0f, moveZ).normalized;

        // Check if we've regenerated enough stamina to sprint again
        if (!canSprint && currentStamina >= minStaminaToSprint)
        {
            canSprint = true;
        }

        // Decide if sprinting: 
        // 1. Player is holding shift
        // 2. Player is moving
        // 3. Either we're already sprinting (stamina > 0) OR we have enough stamina to start sprinting
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) && 
                          movement.magnitude > 0.01f && 
                          (currentStamina > 0 && canSprint);

        // Adjust speed
        float currentSpeed = moveSpeed;
        if (isSprinting)
        {
            currentSpeed *= runMultiplier;
            // Deplete stamina
            currentStamina -= staminaDepletionRate * Time.deltaTime;
            if (currentStamina <= 0f)
            {
                currentStamina = 0f;
                canSprint = false; // Disable sprinting until we reach the threshold again
            }
        }
        else
        {
            // Replenish stamina
            currentStamina += staminaRegenRate * Time.deltaTime;
            if (currentStamina > maxStamina)
                currentStamina = maxStamina;
        }

        // Move the player
        transform.position += movement * currentSpeed * Time.deltaTime;

        // Update animator
        if (animator != null)
        {
            bool isMoving = movement.magnitude > 0.01f;
            animator.SetBool("Running", isMoving);

            // Slightly faster animator speed when sprinting
            animator.speed = (isMoving && isSprinting) ? 1.5f : 1f;
        }

        // Flip sprite based on horizontal input
        if (characterSprite != null && moveX != 0)
        {
            characterSprite.flipX = (moveX < 0);
        }

        // Finally, update the stamina bar fill
        UpdateStaminaBar();
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
}