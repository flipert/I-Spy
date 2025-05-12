using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;
using System;


/// <summary>
/// Controls a character selection tile in the lobby screen.
/// Manages the states: Available, Selected, and Coming Soon.
/// </summary>
public class ButtonCharacterSelect : MonoBehaviour
{
    [Header("Character Info")]
    [SerializeField] private int characterIndex;
    
    // Coming Soon status - set only by CharacterSelectSetup, not in inspector
    private bool isComingSoon = false;
    
    // Expose IsComingSoon as a public property so it can be controlled externally
    public bool IsComingSoon { get { return isComingSoon; } set { isComingSoon = value; UpdateVisualState(); } }
    
    [Header("State Game Objects")]
    [SerializeField] private GameObject stateSelected;
    [SerializeField] private GameObject stateComingSoon;
    
    [Header("UI References")]
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private GameObject kickButton;
    
    // Track which client ID has selected this character
    private ulong? selectedByClientId = null;
    
    // Property to expose character index
    public int CharacterIndex => characterIndex;
    
    private void Awake()
    {
        // Make sure the button reference is set
        if (button == null)
            button = GetComponent<Button>();
            
        // Set up button click listener
        if (button != null)
            button.onClick.AddListener(OnButtonClicked);
    }
    
    private void Start()
    {
        // Register with the CharacterSelectionManager
        if (CharacterSelectionManager.Instance != null)
        {
            CharacterSelectionManager.Instance.RegisterCharacterButton(this);
        }
        
        // Initialize the UI state
        UpdateVisualState();
    }
    
    private void OnDestroy()
    {
        // Unregister from the CharacterSelectionManager
        if (CharacterSelectionManager.Instance != null)
        {
            CharacterSelectionManager.Instance.UnregisterCharacterButton(this);
        }
    }
    
    public void OnButtonClicked()
    {
        // If coming soon, the button shouldn't be clickable
        if (isComingSoon)
            return;
            
        // If we're not connected to NetworkManager, just update visual state (for testing in editor)
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            SelectLocalCharacter();
            return;
        }
        
        // If already selected by this client, don't do anything
        if (selectedByClientId.HasValue && selectedByClientId.Value == NetworkManager.Singleton.LocalClientId)
            return;
            
        // If selected by another client, don't allow selection
        if (selectedByClientId.HasValue && selectedByClientId.Value != NetworkManager.Singleton.LocalClientId)
            return;
            
        // Use the manager to handle selection - this will deselect any previously selected character
        SelectLocalCharacter();
    }
    
    private void SelectLocalCharacter()
    {
        // For non-networked testing
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            // Just toggle selection if not networked
            bool isCurrentlySelected = selectedByClientId.HasValue;
            selectedByClientId = isCurrentlySelected ? null : (ulong?)999; // Use dummy ID for testing
            UpdateVisualState();
            return;
        }
        
        // Inform the CharacterSelectionManager about this selection
        if (CharacterSelectionManager.Instance != null)
        {
            // This will handle deselecting previous character and update the selection
            CharacterSelectionManager.Instance.SelectCharacter(NetworkManager.Singleton.LocalClientId, this);
            Debug.Log($"[ButtonCharacterSelect] Selected character {characterIndex} for client {NetworkManager.Singleton.LocalClientId}");
        }
        else
        {
            // Fallback if no manager (shouldn't happen in production)
            Debug.LogWarning("[ButtonCharacterSelect] No CharacterSelectionManager found! Cannot properly coordinate character selection.");
            selectedByClientId = NetworkManager.Singleton.LocalClientId;
            UpdateVisualState();
            
            // Inform NetworkManagerUI about the selection if available
            if (NetworkManagerUI.Instance != null)
            {
                NetworkManagerUI.Instance.SelectCharacter(characterIndex);
            }
        }
    }
    
    // Called by the CharacterSelectionManager or external code to select this character for a specific client
    public void SelectCharacter(ulong clientId)
    {
        // Skip if already selected by this client
        if (selectedByClientId.HasValue && selectedByClientId.Value == clientId)
            return;
            
        // Skip if coming soon
        if (isComingSoon)
            return;
            
        // Update selection state
        selectedByClientId = clientId;
        
        // Update visual state
        UpdateVisualState();
    }
    
    // Called by the CharacterSelectionManager to deselect this character
    public void DeselectCharacter()
    {
        // Clear selection state
        selectedByClientId = null;
        
        // Update visual state
        UpdateVisualState();
    }
    
    public void SetPlayerName(string playerName)
    {
        if (playerNameText != null)
            playerNameText.text = playerName;
    }
    
    [Header("Transition Settings")]
    [SerializeField] private float fadeInDuration = 0.25f;
    [SerializeField] private float fadeOutDuration = 0.15f;
    
    // Active fade coroutine reference for cancellation if needed
    private Coroutine activeFadeCoroutine = null;
    
    private void UpdateVisualState()
    {
        // Check if this GameObject is active before attempting to start coroutines
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning($"[ButtonCharacterSelect] Attempted to update visual state on inactive GameObject {gameObject.name}");
            return;
        }
        
        // Coming Soon state takes priority
        if (isComingSoon)
        {
            // Disable button interactivity
            if (button != null)
                button.interactable = false;
                
            // Show Coming Soon state with fade
            if (stateComingSoon != null)
            {
                // Cancel any active fades
                if (activeFadeCoroutine != null)
                    StopCoroutine(activeFadeCoroutine);
                    
                // If the selected state is showing, fade it out first
                if (stateSelected != null && stateSelected.activeSelf)
                {
                    UIFadeUtility.FadeOut(this, stateSelected, fadeOutDuration, () => {
                        // After selected state is faded out, fade in coming soon state
                        if (!stateComingSoon.activeSelf)
                            activeFadeCoroutine = UIFadeUtility.FadeIn(this, stateComingSoon, fadeInDuration);
                    });
                }
                else
                {
                    // Just fade in coming soon state
                    if (!stateComingSoon.activeSelf)
                        activeFadeCoroutine = UIFadeUtility.FadeIn(this, stateComingSoon, fadeInDuration);
                }
            }
                
            return;
        }
        
        // Enable button interactivity for available characters
        if (button != null)
            button.interactable = true;
            
        // Show/hide selected state based on selection status
        if (stateSelected != null)
        {
            bool shouldBeSelected = selectedByClientId.HasValue;
            bool isCurrentlySelected = stateSelected.activeSelf;
            
            // Only update if there's a change needed
            if (shouldBeSelected != isCurrentlySelected)
            {
                // Cancel any active fades
                if (activeFadeCoroutine != null)
                    StopCoroutine(activeFadeCoroutine);
                    
                if (shouldBeSelected)
                {
                    // Fade in selected state
                    activeFadeCoroutine = UIFadeUtility.FadeIn(this, stateSelected, fadeInDuration);
                    
                    // If coming soon state is active, fade it out
                    if (stateComingSoon != null && stateComingSoon.activeSelf)
                        UIFadeUtility.FadeOut(this, stateComingSoon, fadeOutDuration);
                }
                else
                {
                    // Fade out selected state
                    activeFadeCoroutine = UIFadeUtility.FadeOut(this, stateSelected, fadeOutDuration);
                }
            }
        }
            
        // Hide coming soon state if not already hidden
        if (stateComingSoon != null && stateComingSoon.activeSelf && !isComingSoon)
        {
            UIFadeUtility.FadeOut(this, stateComingSoon, fadeOutDuration);
        }
            
        // Show/hide kick button based on if we're the host and this is selected by another client
        UpdateKickButtonVisibility();
    }
    
    private void UpdateKickButtonVisibility()
    {
        if (kickButton != null)
        {
            bool isHost = NetworkManager.Singleton != null && 
                         (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer);
                         
            bool isSelectedByOtherClient = selectedByClientId.HasValue && 
                                         NetworkManager.Singleton != null && 
                                         selectedByClientId.Value != NetworkManager.Singleton.LocalClientId;
                                         
            kickButton.SetActive(isHost && isSelectedByOtherClient);
        }
    }
    
    public void OnKickButtonClicked()
    {
        // Only the host can kick players
        if (NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer))
            return;
            
        if (!selectedByClientId.HasValue)
            return;
            
        // Get the client ID to kick
        ulong clientIdToKick = selectedByClientId.Value;
        
        // Cannot kick self (host)
        if (clientIdToKick == NetworkManager.Singleton.LocalClientId)
            return;
            
        // Use the CharacterSelectionManager to handle kicking if available
        if (CharacterSelectionManager.Instance != null)
        {
            CharacterSelectionManager.Instance.KickPlayer(clientIdToKick);
        }
        else
        {
            // Fallback if manager isn't available
            DeselectCharacter();
            NetworkManager.Singleton.DisconnectClient(clientIdToKick);
        }
    }
    
    // Method to be called when player info is updated
    public void UpdatePlayerInfo(string playerName)
    {
        SetPlayerName(playerName);
        UpdateVisualState();
    }
    
    // Returns whether this character is selected by any player
    public bool IsSelected()
    {
        return selectedByClientId.HasValue;
    }
    
    // Returns which client has selected this character
    public ulong? GetSelectedByClientId()
    {
        return selectedByClientId;
    }
    
    // Note: We've removed the duplicate IsSelected method that was causing the CS0111 error
}
