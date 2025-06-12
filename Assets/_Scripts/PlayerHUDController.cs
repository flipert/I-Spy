using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHUDController : MonoBehaviour
{
    [Header("Target Display Settings")]
    [SerializeField] private float targetLoadingAnimDuration = 2.0f;
    [SerializeField] private Sprite[] characterProfileSprites; // Array of profile pictures for each character
    [SerializeField] private Sprite genericPursuerSprite; // Generic sprite for pursuers
    
    [Header("Arsenal Display Settings")]
    private ArsenalIconController rangedWeaponIcon;
    
    // UI References - found by tags in Initialize()
    private Image targetProfileImage;
    private GameObject targetLoadingIndicator;
    private GameObject[] pursuerSlots = new GameObject[4]; // Up to 4 pursuers
    
    // State
    private bool initialized = false;
    private bool isLoadingTarget = false;
    private Coroutine loadingCoroutine;

    // Singleton pattern for easy access
    public static PlayerHUDController Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Don't destroy on load if you want the HUD to persist between scenes
            // DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    public void Initialize()
    {
        if (initialized) return;
        
        Debug.Log($"PlayerHUDController: Initializing... Instance ID: {GetInstanceID()}");
        
        // Find the target profile image
        GameObject targetProfileObj = GameObject.FindGameObjectWithTag("TargetProfileImage");
        if (targetProfileObj != null)
        {
            targetProfileImage = targetProfileObj.GetComponent<Image>();
            if (targetProfileImage == null)
            {
                Debug.LogError("PlayerHUDController: TargetProfileImage tag found but object has no Image component!");
            }
            else
            {
                // Start with an empty/invisible state
                targetProfileImage.enabled = false;
            }
        }
        else
        {
            Debug.LogError("PlayerHUDController: Could not find GameObject with tag 'TargetProfileImage'!");
        }
        
        // Find the target loading indicator
        targetLoadingIndicator = GameObject.FindGameObjectWithTag("TargetLoadingIndicator");
        if (targetLoadingIndicator == null)
        {
            Debug.LogError("PlayerHUDController: Could not find GameObject with tag 'TargetLoadingIndicator'!");
        }
        else
        {
            // Start with loading indicator hidden
            targetLoadingIndicator.SetActive(false);
        }
        
        // Find the pursuer slots (up to 4)
        for (int i = 0; i < 4; i++)
        {
            GameObject slot = GameObject.FindGameObjectWithTag($"PursuerSlot{i+1}");
            if (slot != null)
            {
                pursuerSlots[i] = slot;
                
                // Initialize empty
                Image slotImage = slot.GetComponent<Image>();
                if (slotImage != null)
                {
                    slotImage.enabled = false;
                }
            }
            else
            {
                Debug.LogWarning($"PlayerHUDController: Could not find GameObject with tag 'PursuerSlot{i+1}'!");
            }
        }
        
        // Find the arsenal icons
        GameObject rangedIconObj = GameObject.FindGameObjectWithTag("RangedWeaponIcon");
        if (rangedIconObj != null)
        {
            rangedWeaponIcon = rangedIconObj.GetComponent<ArsenalIconController>();
            if (rangedWeaponIcon == null)
            {
                Debug.LogError("PlayerHUDController: RangedWeaponIcon tag found but object has no ArsenalIconController component!");
            }
        }
        else
        {
            Debug.LogWarning("PlayerHUDController: Could not find GameObject with tag 'RangedWeaponIcon'. This is optional.");
        }
        
        Debug.Log("PlayerHUDController: Initialized HUD elements");
        initialized = true;
    }
    
    // Set the state of the target loading display
    public void SetTargetLoadingState(bool showLoading, bool startAnimation)
    {
        if (!initialized) return;
        
        // Update state
        isLoadingTarget = showLoading;
        
        // Handle loading indicator
        if (targetLoadingIndicator != null)
        {
            targetLoadingIndicator.SetActive(showLoading);
        }
        
        // Start loading animation if requested
        if (showLoading && startAnimation)
        {
            if (loadingCoroutine != null)
            {
                StopCoroutine(loadingCoroutine);
            }
            loadingCoroutine = StartCoroutine(AnimateTargetLoading());
        }
        else if (!showLoading && loadingCoroutine != null)
        {
            StopCoroutine(loadingCoroutine);
            loadingCoroutine = null;
        }
        
        // Hide target profile while loading
        if (targetProfileImage != null)
        {
            targetProfileImage.enabled = !showLoading;
        }
    }
    
    // Set the target character profile
    public void SetTargetCharacter(int characterIndex)
    {
        if (!initialized) return;
        
        // Start loading animation
        SetTargetLoadingState(true, true);
    }
    
    // Called by the loading animation when finished
    private void FinishTargetLoading(int characterIndex)
    {
        if (!initialized) return;
        
        // Hide loading indicator
        if (targetLoadingIndicator != null)
        {
            targetLoadingIndicator.SetActive(false);
        }
        
        // Show target profile with correct sprite
        if (targetProfileImage != null)
        {
            if (characterProfileSprites != null && characterIndex >= 0 && characterIndex < characterProfileSprites.Length)
            {
                targetProfileImage.sprite = characterProfileSprites[characterIndex];
                targetProfileImage.enabled = true;
            }
            else
            {
                Debug.LogError($"PlayerHUDController: Invalid character index {characterIndex} or missing profile sprites!");
                targetProfileImage.enabled = false;
            }
        }
        
        // Update state
        isLoadingTarget = false;
        loadingCoroutine = null;
    }
    
    // Animate the target loading display
    private IEnumerator AnimateTargetLoading()
    {
        float startTime = Time.time;
        float progress = 0f;
        int lastCharacterIndex = -1;
        
        // Remember the character index we're loading
        int targetCharacterIndex = 0;
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null && player.IsOwner)
        {
            // Try to determine target's character index
            // Here, we might need custom logic based on your setup
        }
        
        // Simulate loading by cycling through profile images
        while (progress < 1f)
        {
            progress = (Time.time - startTime) / targetLoadingAnimDuration;
            
            // Cycle through character profiles during loading animation
            if (characterProfileSprites != null && characterProfileSprites.Length > 0)
            {
                int currentIndex = Mathf.FloorToInt(progress * 10f) % characterProfileSprites.Length;
                if (currentIndex != lastCharacterIndex)
                {
                    lastCharacterIndex = currentIndex;
                    
                    // Update loading indicator to cycle through profiles
                    if (targetProfileImage != null)
                    {
                        targetProfileImage.sprite = characterProfileSprites[currentIndex];
                        targetProfileImage.enabled = true;
                    }
                }
            }
            
            yield return null;
        }
        
        // Loading complete, show actual target
        FinishTargetLoading(targetCharacterIndex);
    }
    
    // Update the pursuers display
    public void UpdatePursuers(ulong[] pursuers)
    {
        if (!initialized) return;
        
        // Clear all slots first
        for (int i = 0; i < pursuerSlots.Length; i++)
        {
            if (pursuerSlots[i] != null)
            {
                Image slotImage = pursuerSlots[i].GetComponent<Image>();
                if (slotImage != null)
                {
                    slotImage.enabled = false;
                }
            }
        }
        
        // Fill in active pursuers
        int count = Mathf.Min(pursuers.Length, pursuerSlots.Length);
        for (int i = 0; i < count; i++)
        {
            if (pursuerSlots[i] != null)
            {
                Image slotImage = pursuerSlots[i].GetComponent<Image>();
                if (slotImage != null)
                {
                    slotImage.sprite = genericPursuerSprite;
                    slotImage.enabled = true;
                }
            }
        }
        
        Debug.Log($"PlayerHUDController: Updated pursuer display with {count} pursuers");
    }

    // --- Arsenal Methods ---
    
    public void SetRangedWeaponIcon(Sprite icon)
    {
        if (!initialized || rangedWeaponIcon == null) return;

        if (icon != null)
        {
            rangedWeaponIcon.SetIcon(icon);
        }
        else
        {
            Debug.LogWarning("PlayerHUDController: Tried to set ranged weapon icon with a null sprite.");
        }
    }

    public void TriggerRangedCooldown(float duration)
    {
        if (!initialized || rangedWeaponIcon == null)
        {
            Debug.LogWarning($"PlayerHUDController: Cannot trigger cooldown. Initialized: {initialized}, RangedWeaponIcon is null: {rangedWeaponIcon == null}");
            return;
        }
        
        Debug.Log("PlayerHUDController: Triggering cooldown on ArsenalIconController.");
        rangedWeaponIcon.StartCooldown(duration);
    }
} 