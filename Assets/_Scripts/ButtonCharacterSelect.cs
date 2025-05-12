using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;
using System;

/// <summary>
/// Manages all character selections in the lobby scene.
/// This coordinates between individual ButtonCharacterSelect components.
/// </summary>
public class CharacterSelectionManager : MonoBehaviour
{
    private static CharacterSelectionManager _instance;
    public static CharacterSelectionManager Instance { get; private set; }
    
    // List of all ButtonCharacterSelect components in the scene
    private List<ButtonCharacterSelect> characterButtons = new List<ButtonCharacterSelect>();
    
    // Dictionary to track which client has selected which character
    private Dictionary<ulong, ButtonCharacterSelect> clientSelections = new Dictionary<ulong, ButtonCharacterSelect>();
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            // Don't destroy on load as it needs to persist from lobby to game
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        // Listen for network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }
    
    private void OnDestroy()
    {
        // Clean up event subscriptions
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    // Register a ButtonCharacterSelect with the manager
    public void RegisterCharacterButton(ButtonCharacterSelect button)
    {
        if (!characterButtons.Contains(button))
        {
            characterButtons.Add(button);
        }
    }
    
    // Unregister a ButtonCharacterSelect from the manager
    public void UnregisterCharacterButton(ButtonCharacterSelect button)
    {
        characterButtons.Remove(button);
    }
    
    // Called when a client selects a character
    public void SelectCharacter(ulong clientId, ButtonCharacterSelect selectedButton)
    {
        // If this client already has a selection, deselect it
        if (clientSelections.TryGetValue(clientId, out ButtonCharacterSelect previousSelection))
        {
            previousSelection.DeselectCharacter();
        }
        
        // Update selection tracking
        clientSelections[clientId] = selectedButton;
        
        // Update NetworkManagerUI if available
        if (NetworkManagerUI.Instance != null)
        {
            NetworkManagerUI.Instance.SelectCharacter(selectedButton.CharacterIndex);
        }
    }
    
    // Called when a client deselects a character
    public void DeselectCharacter(ulong clientId)
    {
        clientSelections.Remove(clientId);
    }
    
    // Handle client connections
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected to lobby: {clientId}");
        // Could initialize character selection here if needed
    }
    
    // Handle client disconnections
    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client disconnected from lobby: {clientId}");
        
        // If this client had a selection, deselect it
        if (clientSelections.TryGetValue(clientId, out ButtonCharacterSelect selection))
        {
            selection.DeselectCharacter();
            clientSelections.Remove(clientId);
        }
    }
    
    // Get the currently selected character button for a client
    public ButtonCharacterSelect GetSelectedCharacterFor(ulong clientId)
    {
        clientSelections.TryGetValue(clientId, out ButtonCharacterSelect selection);
        return selection;
    }
    
    // Check if a character is available (not selected by any client)
    public bool IsCharacterAvailable(ButtonCharacterSelect button)
    {
        return !clientSelections.ContainsValue(button);
    }
    
    // Get the client who selected a character
    public ulong? GetClientForCharacter(ButtonCharacterSelect button)
    {
        foreach (var kvp in clientSelections)
        {
            if (kvp.Value == button)
            {
                return kvp.Key;
            }
        }
        return null;
    }
    
    // Reset all character selections
    public void ResetAllSelections()
    {
        foreach (var button in characterButtons)
        {
            button.DeselectCharacter();
        }
        clientSelections.Clear();
    }
    
    // Kick a player from the lobby
    public void KickPlayer(ulong clientId)
    {
        // First, deselect their character
        if (clientSelections.TryGetValue(clientId, out ButtonCharacterSelect selection))
        {
            selection.DeselectCharacter();
            clientSelections.Remove(clientId);
        }
        
        // Then, disconnect them if we're the host
        if (NetworkManager.Singleton != null && 
            (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer))
        {
            NetworkManager.Singleton.DisconnectClient(clientId);
        }
    }
}


/// <summary>
/// Controls a character selection tile in the lobby screen.
/// Manages the states: Available, Selected, and Coming Soon.
/// </summary>
public class ButtonCharacterSelect : MonoBehaviour
{
    [Header("Character Info")]
    [SerializeField] private int characterIndex;
    [SerializeField] private bool isComingSoon = false;
    
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
            
        // Use the manager to handle selection
        SelectLocalCharacter();
    }
    
    private void SelectLocalCharacter()
    {
        if (CharacterSelectionManager.Instance != null && NetworkManager.Singleton != null)
        {
            // Let the manager handle the selection
            CharacterSelectionManager.Instance.SelectCharacter(
                NetworkManager.Singleton.LocalClientId, 
                this
            );
        }
        else
        {
            // Fallback for testing in editor
            ulong clientId = NetworkManager.Singleton != null ? 
                NetworkManager.Singleton.LocalClientId : 0;
                
            // Just update this component directly
            selectedByClientId = clientId;
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
    
    private void UpdateVisualState()
    {
        // Coming Soon state takes priority
        if (isComingSoon)
        {
            // Disable button interactivity
            if (button != null)
                button.interactable = false;
                
            // Show Coming Soon state
            if (stateComingSoon != null)
                stateComingSoon.SetActive(true);
                
            // Hide Selected state
            if (stateSelected != null)
                stateSelected.SetActive(false);
                
            return;
        }
        
        // Enable button interactivity for available characters
        if (button != null)
            button.interactable = true;
            
        // Show/hide selected state based on selection status
        if (stateSelected != null)
            stateSelected.SetActive(selectedByClientId.HasValue);
            
        // Hide coming soon state
        if (stateComingSoon != null)
            stateComingSoon.SetActive(false);
            
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
    
    // Property for inspector checkbox
    public bool IsComingSoon
    {
        get { return isComingSoon; }
        set 
        { 
            isComingSoon = value;
            // Update visual state when changed in inspector
            if (isActiveAndEnabled)
                UpdateVisualState();
        }
    }
}
