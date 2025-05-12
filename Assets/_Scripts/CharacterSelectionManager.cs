using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
        if (button == null) return;
        
        // Skip registration if the button is on an inactive GameObject
        if (!button.gameObject.activeInHierarchy)
        {
            Debug.Log($"[CharacterSelectionManager] Skipping registration of inactive button {button.name} with index {button.CharacterIndex}");
            return;
        }
        
        if (!characterButtons.Contains(button))
        {
            characterButtons.Add(button);
            
            // Sort the list by character index to ensure auto-assignment works properly
            characterButtons = characterButtons
                .OrderBy(b => b.CharacterIndex)
                .ToList();
            
            Debug.Log($"[CharacterSelectionManager] Registered character button {button.name} with index {button.CharacterIndex}");
        }
    }
    
    // Unregister a ButtonCharacterSelect from the manager
    public void UnregisterCharacterButton(ButtonCharacterSelect button)
    {
        characterButtons.Remove(button);
    }
    
    // Auto-assign a character to a client that just joined
    public void AutoAssignCharacter(ulong clientId)
    {
        // Don't reassign if client already has a character
        if (clientSelections.ContainsKey(clientId))
            return;
            
        // Find the first available character that isn't marked as "coming soon"
        ButtonCharacterSelect availableButton = characterButtons
            .Where(button => !button.IsComingSoon && !button.IsSelected())
            .FirstOrDefault();
            
        if (availableButton != null)
        {
            SelectCharacter(clientId, availableButton);
            Debug.Log($"Auto-assigned character {availableButton.CharacterIndex} to client {clientId}");
        }
        else
        {
            Debug.LogWarning($"No available characters to auto-assign to client {clientId}");
        }
    }
    
    // Called when a client selects a character
    public void SelectCharacter(ulong clientId, ButtonCharacterSelect selectedButton)
    {
        Debug.Log($"[CharacterSelectionManager] Client {clientId} selecting character {selectedButton.CharacterIndex}");
        
        // If this client already has a selection, deselect it
        if (clientSelections.TryGetValue(clientId, out ButtonCharacterSelect previousSelection))
        {
            Debug.Log($"[CharacterSelectionManager] Deselecting previous character {previousSelection.CharacterIndex} for client {clientId}");
            previousSelection.DeselectCharacter();
            clientSelections.Remove(clientId); // Remove before adding new selection
        }
        
        // Check if the button is already selected by another client
        foreach (var pair in clientSelections)
        {
            if (pair.Value == selectedButton)
            {
                Debug.LogWarning($"[CharacterSelectionManager] Character {selectedButton.CharacterIndex} already selected by client {pair.Key}. Ignoring selection request.");
                return; // Cannot select a character already selected by another player
            }
        }
        
        // Update selection tracking
        clientSelections[clientId] = selectedButton;
        
        // Update the button's state
        selectedButton.SelectCharacter(clientId);
        
        // Update NetworkManagerUI if available
        if (NetworkManagerUI.Instance != null)
        {
            NetworkManagerUI.Instance.SelectCharacter(selectedButton.CharacterIndex);
        }
    }
    
    // Called when a client deselects a character
    public void DeselectCharacter(ulong clientId)
    {
        if (clientSelections.TryGetValue(clientId, out ButtonCharacterSelect selection))
        {
            selection.DeselectCharacter();
            clientSelections.Remove(clientId);
        }
    }
    
    // Handle client connections
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[CharacterSelectionManager] Client connected to lobby: {clientId}");
        
        // Auto-assign a character to the new client after a short delay to ensure everything is set up
        StartCoroutine(DelayedAutoAssign(clientId));
    }
    
    // Delay auto-assignment slightly to ensure everything is properly initialized
    private IEnumerator DelayedAutoAssign(ulong clientId)
    {
        // Wait a frame to ensure all character buttons are registered
        yield return null;
        
        Debug.Log($"[CharacterSelectionManager] Auto-assigning character to client {clientId}");
        Debug.Log($"[CharacterSelectionManager] Available character buttons: {characterButtons.Count}");
        
        // If no character buttons are registered yet, try to find them
        if (characterButtons.Count == 0)
        {
            FindAndRegisterAllCharacterButtons();
        }
        
        // Auto-assign a character
        AutoAssignCharacter(clientId);
    }
    
    // Utility to find and register all character buttons in the scene
    private void FindAndRegisterAllCharacterButtons()
    {
        ButtonCharacterSelect[] buttons = FindObjectsOfType<ButtonCharacterSelect>();
        
        if (buttons != null && buttons.Length > 0)
        {
            foreach (ButtonCharacterSelect button in buttons)
            {
                RegisterCharacterButton(button);
            }
            
            Debug.Log($"[CharacterSelectionManager] Found and registered {buttons.Length} character buttons");
        }
        else
        {
            Debug.LogWarning("[CharacterSelectionManager] No character buttons found in the scene!");
        }
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
        return !clientSelections.ContainsValue(button) && !button.IsComingSoon;
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
    
    // Set a player's display name
    public void SetPlayerName(ulong clientId, string playerName)
    {
        // Update the name on their currently selected character, if any
        if (clientSelections.TryGetValue(clientId, out ButtonCharacterSelect selection))
        {
            selection.SetPlayerName(playerName);
        }
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
    
    // This comment intentionally left to mark removal of duplicate SetPlayerName method
}
