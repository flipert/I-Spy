using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using Unity.Netcode;
using UnityEngine.Events;

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
    private bool userInitiatedClose = false;
    
    // Add a private flag to track if a refresh is needed when enabled
    private bool needsRefreshOnNextEnable = false;
    
    private void Awake()
    {
        EnsureMatchmakingManagerExists();
        
        // Ensure essential UI components are found
        if (serverListContent == null)
        {
            Debug.LogError("ServerBrowser: serverListContent is not assigned! Finding it in children...");
            
            // First, print the hierarchy for debugging
            PrintUIHierarchy(transform, 0);
            
            // Look for Scroll View/Viewport/Content structure (standard Unity UI layout)
            Transform scrollView = transform.Find("Scroll View");
            if (scrollView != null)
            {
                Debug.Log("ServerBrowser: Found Scroll View");
                Transform viewport = scrollView.Find("Viewport");
                if (viewport != null)
                {
                    Debug.Log("ServerBrowser: Found Viewport");
                    Transform content = viewport.Find("Content");
                    if (content != null)
                    {
                        Debug.Log("ServerBrowser: Found Content at path: Scroll View/Viewport/Content");
                        serverListContent = content;
                    }
                    else
                    {
                        Debug.LogError("ServerBrowser: Content transform not found under Viewport!");
                    }
                }
                else
                {
                    Debug.LogError("ServerBrowser: Viewport transform not found under Scroll View!");
                }
            }
            else
            {
                Debug.LogError("ServerBrowser: Scroll View transform not found!");
                
                // Try to find it in children using tag or name
                serverListContent = transform.Find("ServerListContent") as Transform;
                
                // If still null, search deeper in hierarchy
                if (serverListContent == null)
                {
                    Transform[] allChildren = GetComponentsInChildren<Transform>(true);
                    foreach (Transform child in allChildren)
                    {
                        if (child.name.Contains("Content") && child.parent != null && child.parent.name.Contains("Viewport"))
                        {
                            serverListContent = child;
                            Debug.Log("ServerBrowser: Found serverListContent by name pattern: " + child.name);
                            break;
                        }
                    }
                }
            }
            
            if (serverListContent == null)
            {
                Debug.LogError("ServerBrowser: Could not find serverListContent! Server entries will not be displayed.");
                Debug.LogError("ServerBrowser: Please ensure your UI hierarchy includes: Scroll View/Viewport/Content");
            }
            else
            {
                Debug.Log("ServerBrowser: Successfully assigned serverListContent: " + serverListContent.name);
            }
        }
        
        // Ensure buttons are assigned
        if (refreshButton == null)
        {
            Debug.LogWarning("ServerBrowser: refreshButton is not assigned! Finding it...");
            refreshButton = transform.Find("RefreshButton")?.GetComponent<Button>();
        }
        
        if (backButton == null)
        {
            Debug.LogWarning("ServerBrowser: backButton is not assigned! Finding it...");
            backButton = transform.Find("BackButton")?.GetComponent<Button>();
        }
        
        // Setup button listeners
        if (refreshButton != null)
        {
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(RefreshServerList);
        }
        else
        {
            Debug.LogError("ServerBrowser: refreshButton not found!");
        }
        
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
        else
        {
            Debug.LogError("ServerBrowser: backButton not found!");
        }
        
        // Try to find mainMenuPanel
        FindMainMenuPanel();
        
        // Initialize server entries list
        serverEntries = new List<GameObject>();
        
        // Remove any template entries
        RemoveTemplateEntries();
        
        // Reset status message
        if (statusText != null)
        {
            statusText.gameObject.SetActive(false);
        }
        
        // Auto-refresh when enabled
        StartCoroutine(DelayedRefresh());
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
        // Try to find it by static instance first
        if (MatchmakingManager.Instance != null)
        {
            matchmakingManager = MatchmakingManager.Instance;
            Debug.Log("ServerBrowser: Found MatchmakingManager via singleton Instance");
            return;
        }
        
        // As a fallback, try to find it in the scene
        matchmakingManager = FindObjectOfType<MatchmakingManager>();
        
        if (matchmakingManager == null)
        {
            Debug.LogError("ServerBrowser: MatchmakingManager not found in scene! Server discovery won't work.");
            Debug.LogError("ServerBrowser: Please make sure a MatchmakingManager exists in your scene.");
            
            // Don't create one - it should exist as a singleton
            // Show a notification to help with debugging
            ShowStatusMessage("Error: Network Manager missing!");
        }
        else
        {
            Debug.Log("ServerBrowser: Found existing MatchmakingManager in scene");
        }
    }
    
    /// <summary>
    /// Safely starts a coroutine only if the GameObject is active
    /// </summary>
    private Coroutine SafeStartCoroutine(IEnumerator routine, string routineName = "")
    {
        if (gameObject == null || !gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"ServerBrowser: Cannot start coroutine '{routineName}' - GameObject is inactive or null");
            return null;
        }
        
        try
        {
            return StartCoroutine(routine);
        }
        catch (Exception ex)
        {
            Debug.LogError($"ServerBrowser: Error starting coroutine '{routineName}': {ex.Message}");
            return null;
        }
    }
    
    private void OnEnable()
    {
        // Make sure we have a reference to MatchmakingManager
        EnsureMatchmakingManagerExists();
        
        // Subscribe to events from MatchmakingManager
        if (matchmakingManager != null)
        {
            // First unsubscribe to avoid duplicate subscriptions
            matchmakingManager.OnServerListUpdated -= OnServerListUpdated;
            matchmakingManager.OnCreateServerSuccess -= OnCreateServerSuccess;
            matchmakingManager.OnCreateServerFailed -= OnCreateServerFailed;
            matchmakingManager.OnJoinServerSuccess -= OnJoinServerSuccess;
            matchmakingManager.OnJoinServerFailed -= OnJoinServerFailed;
            
            // Then subscribe
            matchmakingManager.OnServerListUpdated += OnServerListUpdated;
            matchmakingManager.OnCreateServerSuccess += OnCreateServerSuccess;
            matchmakingManager.OnCreateServerFailed += OnCreateServerFailed;
            matchmakingManager.OnJoinServerSuccess += OnJoinServerSuccess;
            matchmakingManager.OnJoinServerFailed += OnJoinServerFailed;
            
            Debug.Log("ServerBrowser: Successfully subscribed to MatchmakingManager events");
        }
        else
        {
            Debug.LogError("ServerBrowser: Failed to find MatchmakingManager! Server discovery will not work.");
        }
        
        // Always clear the server list when this panel is enabled
        ClearServerList();
        
        // Check if we were asked to refresh while inactive
        if (needsRefreshOnNextEnable)
        {
            Debug.Log("ServerBrowser: Performing delayed refresh requested while inactive");
            needsRefreshOnNextEnable = false;
            // Add a small delay before refreshing the server list
            SafeStartCoroutine(DelayedRefresh(), "DelayedRefresh");
        }
        else
        {
            // Add a small delay before refreshing the server list
            // This avoids issues with test entries showing up temporarily
            SafeStartCoroutine(DelayedRefresh(), "DelayedRefresh");
        }
    }
    
    private IEnumerator DelayedRefresh()
    {
        // Wait a brief moment to ensure everything is initialized
        yield return new WaitForSeconds(0.5f);
        
        // Ensure all references exist
        EnsureReferencesExist();
        
        // Check serverListContent after trying to find it
        if (serverListContent == null)
        {
            Debug.LogError("ServerBrowser: serverListContent is still null after attempts to find it. Refresh will not display servers.");
        }
        
        // Check MatchmakingManager
        if (matchmakingManager == null)
        {
            Debug.LogError("ServerBrowser: MatchmakingManager is null before refresh! Attempting to find it...");
            EnsureMatchmakingManagerExists();
            
            if (matchmakingManager == null)
            {
                Debug.LogError("ServerBrowser: Still could not find MatchmakingManager! Refresh will not work.");
                yield break;
            }
        }
        
        // Additional delay to ensure UDP discovery has time to start
        yield return new WaitForSeconds(0.5f);
        
        // Now refresh
        RefreshServerList();
    }
    
    private void OnDisable()
    {
        // If this wasn't user-initiated, we might be in a scene transition or premature disable
        // In that case, we should try to preserve our server list/discovery status
        if (!userInitiatedClose && matchmakingManager != null)
        {
            // Log the fact that this might be unintentional
            Debug.LogWarning("ServerBrowser: OnDisable called without user initiating close. " +
                            "This might cause the server list to disappear prematurely.");
            
            // Don't stop discovery if being disabled externally (like scene transitions)
            // Only unsubscribe from events to prevent memory leaks
            matchmakingManager.OnServerListUpdated -= OnServerListUpdated;
            matchmakingManager.OnCreateServerSuccess -= OnCreateServerSuccess;
            matchmakingManager.OnCreateServerFailed -= OnCreateServerFailed;
            matchmakingManager.OnJoinServerSuccess -= OnJoinServerSuccess;
            matchmakingManager.OnJoinServerFailed -= OnJoinServerFailed;
            
            // Don't call: matchmakingManager.StopServerDiscovery();
        }
        else
        {
            // This is a normal user-initiated close, handle normally
            // Unsubscribe from events when disabled
            if (matchmakingManager != null)
            {
                matchmakingManager.OnServerListUpdated -= OnServerListUpdated;
                matchmakingManager.OnCreateServerSuccess -= OnCreateServerSuccess;
                matchmakingManager.OnCreateServerFailed -= OnCreateServerFailed;
                matchmakingManager.OnJoinServerSuccess -= OnJoinServerSuccess;
                matchmakingManager.OnJoinServerFailed -= OnJoinServerFailed;
                
                // Stop any ongoing server discovery when panel is closed by user
                matchmakingManager.StopServerDiscovery();
            }
        }
        
        // Clean up the UI
        ClearServerList();
        ClearStatusMessage();
    }
    
    // Refresh the server list
    public void RefreshServerList()
    {
        // First ensure all references exist
        if (!EnsureReferencesExist())
        {
            Debug.LogError("ServerBrowser: Cannot refresh - references not complete");
            ShowStatusMessage("Error: Server browser not properly set up!");
            return;
        }
        
        // Make sure MatchmakingManager exists
        if (matchmakingManager == null)
        {
            EnsureMatchmakingManagerExists();
        }
        
        if (matchmakingManager != null)
        {
            Debug.Log("ServerBrowser: Refreshing server list...");
            
            // First clear any existing entries
            ClearServerList();
            
            // Show loading status
            ShowStatusMessage("Refreshing server list...");
            
            try
            {
                // Request the MatchmakingManager to clear its cached data
                matchmakingManager.ClearAllServerData();
                
                // Start server discovery with a clean slate
                matchmakingManager.StopServerDiscovery();
                
                // Wait a moment before starting discovery again
                SafeStartCoroutine(DelayedStartDiscovery(), "DelayedStartDiscovery");
            }
            catch (Exception ex)
            {
                Debug.LogError($"ServerBrowser: Error refreshing server list: {ex.Message}");
                ShowStatusMessage("Error refreshing servers. Try again.");
            }
        }
        else
        {
            Debug.LogError("ServerBrowser: MatchmakingManager reference is missing after attempting to find it!");
            ShowStatusMessage("Error: Cannot find network manager!");
        }
    }
    
    // Start discovery after a short delay to allow previous discovery to clean up
    private IEnumerator DelayedStartDiscovery()
    {
        yield return new WaitForSeconds(0.5f);
        
        if (matchmakingManager != null)
        {
            Debug.Log("ServerBrowser: Starting server discovery...");
            matchmakingManager.StartServerDiscovery();
        }
    }
    
    // Back button handler
    private void OnBackButtonClicked()
    {
        // Mark that the user is intentionally closing the browser
        userInitiatedClose = true;
        
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
        
        // Reset the flag after navigation
        userInitiatedClose = false;
    }
    
    // Handle server list update event
    private void OnServerListUpdated(List<MatchmakingManager.ServerInfo> servers)
    {
        // First check if we're still active - if not, just cache the data but don't update UI
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogWarning("ServerBrowser: OnServerListUpdated called while GameObject inactive. Skipping UI update.");
            // Cache the server list for when we become active again
            if (servers != null && servers.Count > 0)
            {
                Debug.Log($"ServerBrowser: Caching {servers.Count} servers for later display");
                needsRefreshOnNextEnable = true;
            }
            return;
        }
        
        // Add robust null checks and detailed logging
        if (servers != null)
        {
            Debug.Log($"********** SERVER LIST UPDATED: {servers.Count} SERVERS FOUND **********");
            
            if (servers.Count > 0)
            {
                // Log details of all servers to help with debugging
                for (int i = 0; i < servers.Count; i++)
                {
                    var server = servers[i];
                    Debug.Log($"********** SERVER #{i} DETAILS **********\n" +
                              $"Name: {server.serverName}\n" +
                              $"IP: {server.ipAddress}\n" +
                              $"Port: {server.port}\n" +
                              $"Players: {server.currentPlayers}/{server.maxPlayers}\n" +
                              $"In Game: {server.inGame}");
                }
            }
        }
        else
        {
            Debug.LogWarning("ServerBrowser: OnServerListUpdated called with null servers list");
            servers = new List<MatchmakingManager.ServerInfo>();
        }
        
        // Try to ensure references are valid before proceeding
        if (!EnsureReferencesExist())
        {
            Debug.LogError("ServerBrowser: References missing, cannot update server list UI");
            ShowStatusMessage("Error: UI components not found");
            return;
        }
        
        if (serverListContent == null)
        {
            Debug.LogError("ServerBrowser: serverListContent is still null in OnServerListUpdated! Cannot display servers.");
            return;
        }
        
        try
        {
            // Clear existing entries
            ClearServerList();
            
            // Print hierarchy for debugging if having issues
            if (servers.Count > 0 && serverListContent.childCount == 0)
            {
                Debug.Log("ServerBrowser: Printing UI hierarchy to debug server entry creation:");
                PrintUIHierarchy(transform, 0);
            }
            
            if (servers.Count == 0)
            {
                Debug.Log("********** NO SERVERS FOUND **********");
                ShowStatusMessage("No servers found. Make sure a server is running and try again.");
                return;
            }
            
            // Hide status text if we have servers
            ClearStatusMessage();
            
            Debug.Log($"********** ATTEMPTING TO CREATE {servers.Count} SERVER ENTRIES **********");
            
            // Create server entries
            for (int i = 0; i < servers.Count; i++)
            {
                if (servers[i] == null)
                {
                    Debug.LogWarning($"ServerBrowser: Server at index {i} is null, skipping");
                    continue;
                }
                
                try
                {
                    Debug.Log($"********** CREATING SERVER ENTRY #{i}: {servers[i].serverName} **********");
                    CreateServerEntry(servers[i], i);
                    
                    // Verify entry was created
                    if (i < serverEntries.Count && serverEntries[i] != null)
                    {
                        Debug.Log($"********** SERVER ENTRY #{i} SUCCESSFULLY CREATED **********");
                    }
                    else
                    {
                        Debug.LogError($"ServerBrowser: Failed to create/track entry for server: {servers[i].serverName}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"ServerBrowser: Error creating server entry at index {i}: {e.Message}\n{e.StackTrace}");
                }
            }
            
            // Final verification
            Debug.Log($"********** FINAL SERVER ENTRY COUNT: {serverEntries.Count} **********");
            Debug.Log($"********** SERVER LIST CONTENT CHILD COUNT: {serverListContent.childCount} **********");
            
            // Check and fix all server entry buttons
            EnsureServerEntryButtonsWork();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ServerBrowser: Unhandled error in OnServerListUpdated: {e.Message}\n{e.StackTrace}");
            ShowStatusMessage("Error updating server list");
        }
    }
    
    // Clear the server list UI
    private void ClearServerList()
    {
        if (serverListContent == null)
        {
            Debug.LogWarning("ServerBrowser: serverListContent is null, cannot clear children");
            return;
        }
        
        int childCount = serverListContent.childCount;
        Debug.Log($"ServerBrowser: Clearing server list with {childCount} children");
        
        // Destroy all existing server entries
        foreach (GameObject entry in serverEntries)
        {
            if (entry != null)
            {
                Destroy(entry);
            }
        }
        
        // Also destroy any other children that might be in the content
        for (int i = serverListContent.childCount - 1; i >= 0; i--)
        {
            Destroy(serverListContent.GetChild(i).gameObject);
        }
        
        serverEntries.Clear();
    }
    
    // Create a server entry in the UI
    private void CreateServerEntry(MatchmakingManager.ServerInfo server, int index)
    {
        // Validate the server info before creating an entry
        if (server == null || string.IsNullOrEmpty(server.serverName) || string.IsNullOrEmpty(server.ipAddress))
        {
            Debug.LogWarning($"ServerBrowser: Attempted to create entry for invalid server at index {index}");
            return;
        }
        
        if (serverEntryPrefab == null || serverListContent == null)
        {
            Debug.LogError("ServerBrowser: Missing serverEntryPrefab or serverListContent references");
            return;
        }
        
        Debug.Log($"********** CREATING UI ENTRY FOR SERVER: {server.serverName} ({server.ipAddress}:{server.port}) **********");
        
        // Instantiate the server entry prefab
        GameObject entry = Instantiate(serverEntryPrefab, serverListContent);
        entry.name = $"ServerEntry_{index}_{server.serverName}";
        
        // Check that the entry was properly instantiated
        if (entry == null)
        {
            Debug.LogError("ServerBrowser: Failed to instantiate server entry prefab!");
            return;
        }
        
        Debug.Log($"********** SERVER ENTRY INSTANTIATED: {entry.name} **********");
        
        // Find components in the prefab
        TextMeshProUGUI serverNameText = entry.transform.Find("TXTServerName")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI playerCountText = entry.transform.Find("TXTPlayerCount")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI pingText = entry.transform.Find("TXTPing")?.GetComponent<TextMeshProUGUI>();
        
        // Log component references for debugging
        Debug.Log($"ServerBrowser: Server entry components - " +
                  $"Name: {(serverNameText != null ? "Found" : "Missing")}, " +
                  $"PlayerCount: {(playerCountText != null ? "Found" : "Missing")}, " +
                  $"Ping: {(pingText != null ? "Found" : "Missing")}");
        
        // If specific components aren't found, try to locate them by different names
        if (serverNameText == null)
        {
            // Try to find any text component with "name" in it
            foreach (TextMeshProUGUI text in entry.GetComponentsInChildren<TextMeshProUGUI>())
            {
                if (text.name.ToLower().Contains("name") || text.name.ToLower().Contains("server"))
                {
                    serverNameText = text;
                    Debug.Log($"ServerBrowser: Found alternative server name text: {text.name}");
                    break;
                }
            }
        }
        
        // Look for join button - either a specific button or the entry itself
        Button joinButton = entry.transform.Find("BTNJoin")?.GetComponent<Button>();
        
        // If no specific join button found, check if the entry itself is a button
        if (joinButton == null)
        {
            joinButton = entry.GetComponent<Button>();
            Debug.Log("ServerBrowser: Using the entire entry as the join button");
        }
        
        // Set server information
        if (serverNameText != null)
        {
            serverNameText.text = server.serverName;
            Debug.Log($"ServerBrowser: Set server name text to: {server.serverName}");
        }
        else
        {
            // If no specific server name text component, try to find any TextMeshProUGUI
            TextMeshProUGUI[] texts = entry.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0)
            {
                texts[0].text = server.serverName;
                Debug.Log($"ServerBrowser: Set first available text component to: {server.serverName}");
            }
            else
            {
                Debug.LogWarning("ServerBrowser: No text components found to display server name");
            }
        }
        
        if (playerCountText != null)
        {
            playerCountText.text = $"{server.currentPlayers}/{server.maxPlayers}";
            Debug.Log($"ServerBrowser: Set player count text to: {server.currentPlayers}/{server.maxPlayers}");
        }
        
        if (pingText != null)
        {
            // Set ping to a placeholder or actual value if available
            pingText.text = "---";
        }
        
        // Setup join button
        if (joinButton != null)
        {
            int serverIdx = index; // Capture index for the lambda
            
            // Remove any existing listeners to avoid duplicates
            joinButton.onClick.RemoveAllListeners();
            
            // Add new listener
            joinButton.onClick.AddListener(() => {
                Debug.Log($"ServerBrowser: Join button clicked for server {server.serverName} at index {serverIdx}");
                OnJoinButtonClicked(serverIdx);
            });
            
            Debug.Log($"ServerBrowser: Successfully set up join button for server {server.serverName}");
        }
        else
        {
            Debug.LogError("ServerBrowser: No Button component found on server entry! User won't be able to join this server.");
        }
        
        // Make sure the entry is active and visible
        entry.SetActive(true);
        
        // Add to our list for tracking
        serverEntries.Add(entry);
        
        Debug.Log($"********** SERVER ENTRY #{index} ADDED TO LIST, TOTAL ENTRIES: {serverEntries.Count} **********");
    }
    
    // Join button click handler
    private void OnJoinButtonClicked(int serverIndex)
    {
        Debug.Log($"ServerBrowser: OnJoinButtonClicked called for server index {serverIndex}");
        
        if (matchmakingManager == null)
        {
            EnsureMatchmakingManagerExists();
            if (matchmakingManager == null)
            {
                Debug.LogError("ServerBrowser: MatchmakingManager not found! Cannot join server.");
                ShowStatusMessage("Error: Network manager not found!");
                return;
            }
        }
        
        // Get the list of available servers from the MatchmakingManager to verify index
        List<MatchmakingManager.ServerInfo> servers = matchmakingManager.GetAvailableServers();
        
        if (servers == null || servers.Count == 0)
        {
            Debug.LogError("ServerBrowser: Available servers list is empty or null");
            ShowStatusMessage("Error: No servers available to join");
            return;
        }
        
        if (serverIndex < 0 || serverIndex >= servers.Count)
        {
            Debug.LogError($"ServerBrowser: Invalid server index {serverIndex}. Available servers: {servers.Count}");
            ShowStatusMessage("Error: Invalid server selection");
            return;
        }
        
        // Show status
        ShowStatusMessage("Joining server...");
        
        Debug.Log($"ServerBrowser: Attempting to join server at index {serverIndex}: {servers[serverIndex].serverName} at {servers[serverIndex].ipAddress}:{servers[serverIndex].port}");
        
        try
        {
            // Set the user-initiated flag so that OnDisable won't prematurely stop discovery
            // if this GameObject gets disabled during scene transitions
            userInitiatedClose = true;
            
            // Join the server
            matchmakingManager.JoinServer(serverIndex);
        }
        catch (Exception ex)
        {
            // Reset the flag if there was an error
            userInitiatedClose = false;
            
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
        // Keep the userInitiatedClose flag set to true since we're intentionally
        // changing scenes or disabling this GameObject during connection
    }
    
    private void OnJoinServerFailed(string errorMessage)
    {
        // Reset the userInitiatedClose flag since we're staying in the browser
        userInitiatedClose = false;
        ShowStatusMessage($"Failed to join server: {errorMessage}");
    }
    
    // Show a status message for a duration
    private void ShowStatusMessage(string message)
    {
        if (statusText == null)
            return;
            
        // Make sure the GameObject is active before starting a coroutine
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"ServerBrowser: Cannot show status message '{message}' - GameObject is inactive");
            return;
        }
            
        // Cancel any existing status message coroutine
        if (statusMessageCoroutine != null)
            StopCoroutine(statusMessageCoroutine);
            
        // Set the message
        statusText.text = message;
        
        // Start the coroutine to clear the message after a delay
        statusMessageCoroutine = SafeStartCoroutine(ClearStatusMessageAfterDelay(), "ClearStatusMessageAfterDelay");
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
    
    /// <summary>
    /// Completely resets the server browser state, clearing all entries and cached data
    /// </summary>
    public void ClearServerData()
    {
        Debug.Log("ServerBrowser: Clearing all server data");
        
        // Check if we're active - if not, set a flag to refresh when we become active
        if (!gameObject.activeInHierarchy)
        {
            Debug.Log("ServerBrowser: GameObject is inactive during ClearServerData call. Will refresh when enabled.");
            needsRefreshOnNextEnable = true;
            return;
        }
        
        // Stop discovery first
        if (matchmakingManager != null)
        {
            matchmakingManager.StopServerDiscovery();
            matchmakingManager.ClearAllServerData();
        }
        
        // Clear UI
        if (serverEntries != null)
        {
            ClearServerList();
        }
        
        // Clear status
        if (statusText != null)
        {
            // Try showing a status message, but only if we're active
            if (gameObject.activeInHierarchy)
            {
                ShowStatusMessage("Ready to search for servers...");
            }
        }
        
        // Remove any template entries
        RemoveTemplateEntries();
    }
    
    // Ensure there are no template entries left in the hierarchy
    private void RemoveTemplateEntries()
    {
        if (serverListContent == null)
        {
            Debug.LogWarning("ServerBrowser: Cannot remove template entries, serverListContent is null");
            return;
        }
        
        Debug.Log($"ServerBrowser: Checking for template entries, found {serverListContent.childCount} children");
        
        // Remove any child game objects that might be template entries
        if (serverListContent.childCount > 0)
        {
            for (int i = serverListContent.childCount - 1; i >= 0; i--)
            {
                Transform child = serverListContent.GetChild(i);
                Destroy(child.gameObject);
            }
        }
    }
    
    // Method to ensure all references exist before using them
    private bool EnsureReferencesExist()
    {
        // Try to set up UI components if they're missing
        if (serverListContent == null)
        {
            Debug.LogError("ServerBrowser: Could not find serverListContent reference!");
            SetupUIComponents();
            
            // Check if the setup was successful
            if (serverListContent == null)
            {
                Debug.LogError("ServerBrowser: Failed to find serverListContent after setup attempt.");
                return false;
            }
        }
        
        return true;
    }
    
    // Add this method to directly assign the serverListContent
    public void SetupUIComponents()
    {
        if (serverListContent == null)
        {
            Debug.Log("ServerBrowser: Attempting to find Content in hierarchy...");
            
            // Try to find the Content directly
            Transform scrollView = transform.Find("Scroll View");
            if (scrollView != null)
            {
                Transform viewport = scrollView.Find("Viewport");
                if (viewport != null)
                {
                    Transform content = viewport.Find("Content");
                    if (content != null)
                    {
                        serverListContent = content;
                        Debug.Log("ServerBrowser: Successfully found and assigned Content transform!");
                    }
                }
            }
            
            // If still not found, try to find by name
            if (serverListContent == null)
            {
                // Search all children for "Content"
                Transform[] allChildren = GetComponentsInChildren<Transform>(true);
                foreach (Transform child in allChildren)
                {
                    if (child.name == "Content")
                    {
                        serverListContent = child;
                        Debug.Log("ServerBrowser: Found Content by name: " + child.name);
                        break;
                    }
                }
            }
        }
        
        // Check if the reference was successfully set
        if (serverListContent != null)
        {
            Debug.Log("ServerBrowser: serverListContent is now assigned to: " + serverListContent.name);
        }
        else
        {
            Debug.LogError("ServerBrowser: Failed to find Content transform!");
        }
    }
    
    // Helper method to print the UI hierarchy for debugging
    private void PrintUIHierarchy(Transform transform, int depth)
    {
        string indent = new string(' ', depth * 4);
        Debug.Log($"{indent}• {transform.name}");
        
        foreach (Transform child in transform)
        {
            PrintUIHierarchy(child, depth + 1);
        }
    }
    
    // Check and fix all server entry buttons to ensure they work
    private void EnsureServerEntryButtonsWork()
    {
        Debug.Log($"ServerBrowser: Checking {serverEntries.Count} server entry buttons");
        
        for (int i = 0; i < serverEntries.Count; i++)
        {
            GameObject entry = serverEntries[i];
            if (entry == null) continue;
            
            // Try to get the button component
            Button button = entry.GetComponent<Button>();
            if (button == null)
            {
                // Try to find a button in the children
                button = entry.GetComponentInChildren<Button>(true);
                if (button == null)
                {
                    Debug.LogError($"ServerBrowser: Server entry {i} has no Button component! Adding one.");
                    button = entry.AddComponent<Button>();
                    // Set up a color transition
                    ColorBlock colors = button.colors;
                    colors.normalColor = Color.white;
                    colors.highlightedColor = new Color(0.9f, 0.9f, 1f);
                    colors.pressedColor = new Color(0.8f, 0.8f, 0.9f);
                    button.colors = colors;
                }
            }
            
            // Check if button has listeners
            int listenerCount = 0;
            
            // We can't directly check the listeners count, so we'll add a temporary one and remove it
            UnityAction tempAction = () => { listenerCount++; };
            button.onClick.AddListener(tempAction);
            button.onClick.RemoveListener(tempAction);
            
            if (listenerCount == 0)
            {
                Debug.LogWarning($"ServerBrowser: Button for server entry {i} has no click listeners. Adding one.");
                int serverIdx = i; // Capture index for the lambda
                button.onClick.AddListener(() => {
                    Debug.Log($"ServerBrowser: Clicked on server entry {serverIdx}");
                    OnJoinButtonClicked(serverIdx);
                });
            }
        }
    }
}
