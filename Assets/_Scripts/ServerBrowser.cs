using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using Unity.Netcode;

/// <summary>
/// UI controller for the server browser screen
/// </summary>
public class ServerBrowser : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject serverListPanel;
    [SerializeField] private GameObject serverEntryPrefab;
    [SerializeField] private Transform serverListContent;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TextMeshProUGUI statusText;
    
    [Header("Navigation")]
    [SerializeField] private GameObject mainMenuPanel; // Reference to the main menu panel
    
    [Header("Settings")]
    [SerializeField] private float statusMessageDuration = 3f;
    
    // Reference to the matchmaking manager
    private MatchmakingManager matchmakingManager;
    private List<GameObject> serverEntries = new List<GameObject>();
    private Coroutine statusMessageCoroutine;
    
    private void Awake()
    {
        // Ensure MatchmakingManager exists
        EnsureMatchmakingManagerExists();
        
        // Set up button listeners
        if (refreshButton != null)
            refreshButton.onClick.AddListener(RefreshServerList);
            
        if (backButton != null)
            backButton.onClick.AddListener(OnBackButtonClicked);
        
        // Clear any status text
        if (statusText != null)
            statusText.text = "";
            
        // Try to find main menu panel if not set
        if (mainMenuPanel == null)
        {
            FindMainMenuPanel();
        }
            
        Debug.Log($"ServerBrowser Awake completed. Has MatchmakingManager: {matchmakingManager != null}");
    }
    
    // Helper method to find the main menu
    private void FindMainMenuPanel()
    {
        MainMenuUI mainMenuUI = FindObjectOfType<MainMenuUI>();
        if (mainMenuUI != null)
        {
            // Try to find a panel named "MainMenuPanel" on the MainMenuUI GameObject
            Transform mainMenuTransform = mainMenuUI.transform.Find("MainMenuPanel");
            if (mainMenuTransform != null)
            {
                mainMenuPanel = mainMenuTransform.gameObject;
                Debug.Log("ServerBrowser: Found MainMenuPanel by name");
            }
            else
            {
                // Just use the MainMenuUI GameObject itself
                mainMenuPanel = mainMenuUI.gameObject;
                Debug.Log("ServerBrowser: Using MainMenuUI GameObject as main menu panel");
            }
        }
        else
        {
            Debug.LogWarning("ServerBrowser: MainMenuUI not found in scene");
        }
    }
    
    // Ensure the MatchmakingManager exists
    private void EnsureMatchmakingManagerExists()
    {
        matchmakingManager = FindObjectOfType<MatchmakingManager>();
        
        if (matchmakingManager == null)
        {
            Debug.LogWarning("ServerBrowser: MatchmakingManager not found! Creating one...");
            
            // Create the MatchmakingManager
            GameObject matchmakingManagerGO = new GameObject("MatchmakingManager");
            matchmakingManager = matchmakingManagerGO.AddComponent<MatchmakingManager>();
            
            Debug.Log("ServerBrowser: Created MatchmakingManager");
        }
        else
        {
            Debug.Log("ServerBrowser: Found existing MatchmakingManager");
        }
    }
    
    private void OnEnable()
    {
        // Subscribe to events from MatchmakingManager
        if (matchmakingManager != null)
        {
            matchmakingManager.OnServerListUpdated += OnServerListUpdated;
            matchmakingManager.OnCreateServerSuccess += OnCreateServerSuccess;
            matchmakingManager.OnCreateServerFailed += OnCreateServerFailed;
            matchmakingManager.OnJoinServerSuccess += OnJoinServerSuccess;
            matchmakingManager.OnJoinServerFailed += OnJoinServerFailed;
        }
        
        // Always clear the server list when this panel is enabled
        ClearServerList();
        ShowStatusMessage("Server list cleared. Click Refresh to search for servers.");
        
        // Start refreshing the server list
        RefreshServerList();
    }
    
    private void OnDisable()
    {
        // Unsubscribe from events
        if (matchmakingManager != null)
        {
            matchmakingManager.OnServerListUpdated -= OnServerListUpdated;
            matchmakingManager.OnCreateServerSuccess -= OnCreateServerSuccess;
            matchmakingManager.OnCreateServerFailed -= OnCreateServerFailed;
            matchmakingManager.OnJoinServerSuccess -= OnJoinServerSuccess;
            matchmakingManager.OnJoinServerFailed -= OnJoinServerFailed;
        }
        
        // Stop server discovery
        if (matchmakingManager != null)
        {
            matchmakingManager.StopServerDiscovery();
        }
    }
    
    // Refresh the server list
    public void RefreshServerList()
    {
        if (matchmakingManager != null)
        {
            // Show loading status
            ShowStatusMessage("Refreshing server list...");
            
            // Start server discovery
            matchmakingManager.StartServerDiscovery();
        }
    }
    
    // Back button handler
    private void OnBackButtonClicked()
    {
        // Hide the server browser
        gameObject.SetActive(false);
        
        // Activate the main menu
        if (mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(true);
        }
        else
        {
            // Try to find MainMenuUI and show it
            MainMenuUI mainMenuUI = FindObjectOfType<MainMenuUI>();
            if (mainMenuUI != null)
            {
                mainMenuUI.gameObject.SetActive(true);
                mainMenuUI.ShowMainMenuPanel();
                Debug.Log("ServerBrowser: Found and activated MainMenuUI");
            }
            else
            {
                Debug.LogWarning("ServerBrowser: Back button pressed but mainMenuPanel is not assigned and MainMenuUI not found!");
            }
        }
    }
    
    // Handle server list update event
    private void OnServerListUpdated(List<MatchmakingManager.ServerInfo> servers)
    {
        // Clear current server entries
        ClearServerList();
        
        // Filter out any potentially invalid servers
        List<MatchmakingManager.ServerInfo> validServers = new List<MatchmakingManager.ServerInfo>();
        if (servers != null)
        {
            foreach (var server in servers)
            {
                if (server != null && !string.IsNullOrEmpty(server.ipAddress))
                {
                    validServers.Add(server);
                }
                else
                {
                    Debug.LogWarning("ServerBrowser: Filtered out invalid server entry");
                }
            }
        }
        
        // No valid servers found
        if (validServers.Count == 0)
        {
            ShowStatusMessage("No servers found. Try refreshing.");
            return;
        }
        
        Debug.Log($"ServerBrowser: Creating UI for {validServers.Count} servers");
        
        // Create UI entries for each valid server
        for (int i = 0; i < validServers.Count; i++)
        {
            var server = validServers[i];
            CreateServerEntry(server, i);
        }
        
        // Clear status
        ClearStatusMessage();
    }
    
    // Clear the server list UI
    private void ClearServerList()
    {
        foreach (var entry in serverEntries)
        {
            Destroy(entry);
        }
        
        serverEntries.Clear();
    }
    
    // Create a server entry in the UI
    private void CreateServerEntry(MatchmakingManager.ServerInfo server, int index)
    {
        if (serverEntryPrefab == null || serverListContent == null)
            return;
            
        // Validate server info
        if (server == null || string.IsNullOrEmpty(server.ipAddress))
        {
            Debug.LogWarning("ServerBrowser: Tried to create entry with invalid server info");
            return;
        }
            
        // Instantiate the server entry prefab
        GameObject entry = Instantiate(serverEntryPrefab, serverListContent);
        serverEntries.Add(entry);
        
        // Setup the entry UI elements
        TextMeshProUGUI serverNameText = entry.transform.Find("ServerNameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI playerCountText = entry.transform.Find("PlayerCountText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI statusText = entry.transform.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        Button joinButton = entry.transform.Find("JoinButton")?.GetComponent<Button>();
        
        // Set server name (use a default if empty)
        if (serverNameText != null)
        {
            // Force clear any existing text first
            serverNameText.text = "";
            
            // Then set the server name
            if (!string.IsNullOrEmpty(server.serverName))
            {
                serverNameText.text = server.serverName;
                Debug.Log($"ServerBrowser: Set server name text to: '{server.serverName}'");
            }
            else
            {
                // Provide a better default name based on IP
                serverNameText.text = $"Server at {server.ipAddress}";
                Debug.Log($"ServerBrowser: Used fallback name: 'Server at {server.ipAddress}'");
            }
        }
            
        // Set player count
        if (playerCountText != null)
            playerCountText.text = $"{server.currentPlayers}/{server.maxPlayers}";
            
        // Set status
        if (statusText != null)
        {
            if (server.inGame)
            {
                statusText.text = "In Progress";
                statusText.color = Color.red;
            }
            else
            {
                statusText.text = "In Lobby";
                statusText.color = Color.green;
            }
        }
        
        // Setup join button
        if (joinButton != null)
        {
            // Store the server index for the button click handler
            int serverIndex = index;
            
            // Remove any existing listeners and add a new one
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => OnJoinButtonClicked(serverIndex));
            
            // Disable join button if server is full or in game
            joinButton.interactable = !server.inGame && server.currentPlayers < server.maxPlayers;
        }
    }
    
    // Join button click handler
    private void OnJoinButtonClicked(int serverIndex)
    {
        if (matchmakingManager == null)
        {
            EnsureMatchmakingManagerExists();
            if (matchmakingManager == null)
            {
                ShowStatusMessage("MatchmakingManager not found! Cannot join server.");
                return;
            }
        }
            
        // Show status
        ShowStatusMessage("Joining server...");
        
        Debug.Log($"ServerBrowser: Attempting to join server at index {serverIndex}");
        
        try
        {
            // Join the server
            matchmakingManager.JoinServer(serverIndex);
        }
        catch (Exception ex)
        {
            Debug.LogError($"ServerBrowser: Error joining server: {ex.Message}");
            ShowStatusMessage($"Error joining server: {ex.Message}");
        }
    }
    
    // Event handlers
    private void OnCreateServerSuccess()
    {
        ShowStatusMessage("Server created successfully!");
    }
    
    private void OnCreateServerFailed(string errorMessage)
    {
        ShowStatusMessage($"Failed to create server: {errorMessage}");
    }
    
    private void OnJoinServerSuccess()
    {
        ShowStatusMessage("Joined server successfully!");
    }
    
    private void OnJoinServerFailed(string errorMessage)
    {
        ShowStatusMessage($"Failed to join server: {errorMessage}");
    }
    
    // Show a status message for a duration
    private void ShowStatusMessage(string message)
    {
        if (statusText == null)
            return;
            
        // Cancel any existing status message coroutine
        if (statusMessageCoroutine != null)
            StopCoroutine(statusMessageCoroutine);
            
        // Set the message
        statusText.text = message;
        
        // Start the coroutine to clear the message after a delay
        statusMessageCoroutine = StartCoroutine(ClearStatusMessageAfterDelay());
    }
    
    // Clear the status message after a delay
    private IEnumerator ClearStatusMessageAfterDelay()
    {
        yield return new WaitForSeconds(statusMessageDuration);
        ClearStatusMessage();
    }
    
    // Clear the status message immediately
    private void ClearStatusMessage()
    {
        if (statusText != null)
            statusText.text = "";
            
        statusMessageCoroutine = null;
    }
    
    // Clear all server data (useful for debugging)
    public void ClearServerData()
    {
        if (matchmakingManager != null)
        {
            matchmakingManager.ClearAllServerData();
            ClearServerList();
            ShowStatusMessage("All server data cleared.");
        }
    }
}
