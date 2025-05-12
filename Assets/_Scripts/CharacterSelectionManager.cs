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
    
    // Flag to track if auto-assignment has been completed
    private bool hostCharacterAssigned = false;
    
    private void OnEnable()
    {
        Debug.Log("[CharacterSelectionManager] OnEnable called, starting automatic character assignment");
        // Reset assignment flag when enabled
        hostCharacterAssigned = false;
        // Start the auto-assignment process
        StartCoroutine(WaitForUIAndAssignCharacter());
    }
    
    /// <summary>
    /// This coroutine waits until the UI is fully initialized before assigning a character to the host
    /// It checks repeatedly until character buttons are registered or a time limit is reached
    /// </summary>
    private IEnumerator WaitForUIAndAssignCharacter()
    {
        // Only proceed if we're the host/server and haven't already assigned a character
        if (NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer) || hostCharacterAssigned)
        {
            Debug.Log("[CharacterSelectionManager] Not the host or already assigned, skipping character assignment");
            yield break;
        }
        
        Debug.Log("[CharacterSelectionManager] Starting UI check for character assignment");
        
        // Give the UI time to initialize first
        yield return new WaitForSeconds(1.0f);
        
        // Keep checking for character buttons until they're registered or we've tried enough times
        int maxAttempts = 10;
        int attemptCount = 0;
        
        while (characterButtons.Count == 0 && attemptCount < maxAttempts)
        {
            Debug.Log($"[CharacterSelectionManager] Attempt {attemptCount+1}/{maxAttempts} to find character buttons");
            FindAndRegisterAllCharacterButtons();
            
            // If we still don't have buttons, wait a bit and try again
            if (characterButtons.Count == 0)
            {
                // Use a longer wait time between attempts
                yield return new WaitForSeconds(0.5f);
                attemptCount++;
            }
        }
        
        // Log how many buttons we found
        Debug.Log($"[CharacterSelectionManager] Found {characterButtons.Count} character buttons after {attemptCount+1} attempts");
        
        // If we couldn't find any character buttons, log an error and exit
        if (characterButtons.Count == 0)
        {
            Debug.LogError("[CharacterSelectionManager] Failed to find any character buttons after multiple attempts!");
            yield break;
        }
        
        // Now that we have buttons, assign character 0 to the host
        AssignHostCharacter();
    }
    
    /// <summary>
    /// Directly assigns character 0 to the host player if available
    /// </summary>
    private void AssignHostCharacter()
    {
        // Don't reassign if already done
        if (hostCharacterAssigned)
        {
            Debug.Log("[CharacterSelectionManager] Host character already assigned, skipping");
            return;
        }
        
        // Get the host's client ID
        ulong hostId = NetworkManager.Singleton.LocalClientId;
        
        Debug.Log($"[CharacterSelectionManager] Assigning character to host (ClientID: {hostId})");
        
        // Try to find character 0 (first priority)
        ButtonCharacterSelect hostCharacter = characterButtons
            .Where(b => b.CharacterIndex == 0 && !b.IsComingSoon)
            .FirstOrDefault();
            
        // If character 0 isn't available, try any available character
        if (hostCharacter == null)
        {
            Debug.Log("[CharacterSelectionManager] Character 0 not available, looking for any available character");
            hostCharacter = characterButtons
                .Where(b => !b.IsComingSoon && !b.IsSelected())
                .OrderBy(b => b.CharacterIndex)
                .FirstOrDefault();
        }
        
        if (hostCharacter != null)
        {
            Debug.Log($"[CharacterSelectionManager] Found character {hostCharacter.CharacterIndex} for host");
            
            // Deselect any currently selected character for this host
            if (clientSelections.TryGetValue(hostId, out var previousSelection))
            {
                previousSelection.DeselectCharacter();
                clientSelections.Remove(hostId);
            }
            
            // Force select the character and update tracking
            hostCharacter.SelectCharacter(hostId);
            clientSelections[hostId] = hostCharacter;
            
            // Set a player name
            hostCharacter.SetPlayerName("Host");
            
            // Force update visual state
            hostCharacter.UpdateVisualState();
            
            // Mark as assigned
            hostCharacterAssigned = true;
            
            Debug.Log($"[CharacterSelectionManager] Successfully assigned character {hostCharacter.CharacterIndex} to host player");
        }
        else
        {
            Debug.LogError("[CharacterSelectionManager] No available characters found for host player!");
        }
    }
    
    private void Start()
    {
        Debug.Log("[CharacterSelectionManager] Start method called");
        
        // Listen for network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            
            // Try again to assign a character if it wasn't done in OnEnable
            if (!hostCharacterAssigned && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer))
            {
                Debug.Log("[CharacterSelectionManager] Host character not assigned yet, starting assignment from Start");
                StartCoroutine(WaitForUIAndAssignCharacter());
            }
        }
    }
    
    // Additional method to manually force character assignment
    // Can be called from a UI button if needed
    public void ForceCharacterAssignment()
    {
        Debug.Log("[CharacterSelectionManager] Manual force of character assignment requested");
        hostCharacterAssigned = false; // Reset flag to allow reassignment
        StartCoroutine(WaitForUIAndAssignCharacter());
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
        Debug.Log($"[CharacterSelectionManager] Auto-assigning character to client {clientId}");
        
        // Don't reassign if client already has a character
        if (clientSelections.ContainsKey(clientId))
        {
            Debug.Log($"[CharacterSelectionManager] Client {clientId} already has character {clientSelections[clientId].CharacterIndex} assigned. Skipping auto-assignment.");
            return;
        }

        // If this is the host, prefer character 0
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId)
        {
            ButtonCharacterSelect characterZero = characterButtons
                .Where(button => button.CharacterIndex == 0 && !button.IsComingSoon && !button.IsSelected())
                .FirstOrDefault();
                
            if (characterZero != null)
            {
                Debug.Log($"[CharacterSelectionManager] Found character 0 for host client {clientId}");
                SelectCharacter(clientId, characterZero);
                return;
            }
        }
            
        // Find the first available character that isn't marked as "coming soon"
        ButtonCharacterSelect availableButton = characterButtons
            .Where(button => !button.IsComingSoon && !button.IsSelected())
            .OrderBy(button => button.CharacterIndex) // Ensure we assign characters in index order
            .FirstOrDefault();
            
        if (availableButton != null)
        {
            Debug.Log($"[CharacterSelectionManager] Found available character {availableButton.CharacterIndex} for client {clientId}");
            SelectCharacter(clientId, availableButton);
            Debug.Log($"[CharacterSelectionManager] Auto-assigned character {availableButton.CharacterIndex} to client {clientId}");
        }
        else
        {
            Debug.LogWarning($"[CharacterSelectionManager] No available characters to auto-assign to client {clientId}");
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
        ulong? selectedByClientId = null;
        foreach (var pair in clientSelections)
        {
            if (pair.Value == selectedButton)
            {
                selectedByClientId = pair.Key;
                Debug.Log($"[CharacterSelectionManager] Character {selectedButton.CharacterIndex} already selected by client {pair.Key}. Will reassign it.");
                break;
            }
        }
        
        // If the character is already selected by another client, deselect it for that client
        if (selectedByClientId.HasValue)
        {
            Debug.Log($"[CharacterSelectionManager] Deselecting character {selectedButton.CharacterIndex} from client {selectedByClientId.Value}");
            // Remove the other client's selection
            clientSelections.Remove(selectedByClientId.Value);
            
            // If we're the host, we should notify the client that they lost their character
            if (NetworkManager.Singleton != null && 
                (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer))
            {
                // You might need to implement a client RPC to notify the client they lost their character
                Debug.Log($"[CharacterSelectionManager] Would notify client {selectedByClientId.Value} that they lost their character");
                // Depending on your network architecture, you might need additional code here
            }
        }
        
        // Update selection tracking
        clientSelections[clientId] = selectedButton;
        
        // Update the button's state
        selectedButton.DeselectCharacter(); // First deselect it (in case it was selected by another client)
        selectedButton.SelectCharacter(clientId); // Then select it for the current client
        
        // Update NetworkManagerUI if available
        if (NetworkManagerUI.Instance != null)
        {
            NetworkManagerUI.Instance.SelectCharacter(selectedButton.CharacterIndex);
            Debug.Log($"[CharacterSelectionManager] Notified NetworkManagerUI about selection of character {selectedButton.CharacterIndex}");
        }
        else
        {
            Debug.LogWarning($"[CharacterSelectionManager] NetworkManagerUI not found. Cannot update character selection UI.");
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
    
    /// <summary>
    /// Deselects all character buttons except for the specified one.
    /// This is used to ensure only one character is selected at a time in non-networked mode.
    /// </summary>
    public void DeselectAllCharactersExcept(ButtonCharacterSelect exceptButton)
    {
        Debug.Log($"[CharacterSelectionManager] Deselecting all characters except {exceptButton.CharacterIndex}");
        
        foreach (var button in characterButtons)
        {
            if (button != exceptButton && button.IsSelected())
            {
                Debug.Log($"[CharacterSelectionManager] Deselecting character {button.CharacterIndex}");
                button.DeselectCharacter();
            }
        }
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
