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
            
            if (refreshButton == null)
            {
                // Try to find by name in children
                Button[] buttons = GetComponentsInChildren<Button>(true);
                foreach (Button button in buttons)
                {
                    if (button.name.Contains("Refresh"))
                    {
                        refreshButton = button;
                        Debug.Log("ServerBrowser: Found refreshButton: " + button.name);
                        break;
                    }
                }
            }
        }
        
        if (backButton == null)
        {
            Debug.LogWarning("ServerBrowser: backButton is not assigned! Finding it...");
            backButton = transform.Find("BackButton")?.GetComponent<Button>();
            
            if (backButton == null)
            {
                // Try to find by name in children
                Button[] buttons = GetComponentsInChildren<Button>(true);
                foreach (Button button in buttons)
                {
                    if (button.name.Contains("Back"))
                    {
                        backButton = button;
                        Debug.Log("ServerBrowser: Found backButton: " + button.name);
                        break;
                    }
                }
            }
        }
        
        // Setup button listeners if they exist
        if (refreshButton != null)
        {
            refreshButton.onClick.AddListener(RefreshServerList);
        }
        
        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
        
        FindMainMenuPanel();
        RemoveTemplateEntries();
        
        if (serverListContent != null)
        {
            Debug.Log("ServerBrowser: Initialized with serverListContent found");
        }
        else
        {
            Debug.Log("ServerBrowser: Initialized with serverListContent missing");
        }
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
        
        // Add a small delay before refreshing the server list
        // This avoids issues with test entries showing up temporarily
        StartCoroutine(DelayedRefresh());
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
        // Unsubscribe from events when disabled
        if (matchmakingManager != null)
        {
            matchmakingManager.OnServerListUpdated -= OnServerListUpdated;
            matchmakingManager.OnCreateServerSuccess -= OnCreateServerSuccess;
            matchmakingManager.OnCreateServerFailed -= OnCreateServerFailed;
            matchmakingManager.OnJoinServerSuccess -= OnJoinServerSuccess;
            matchmakingManager.OnJoinServerFailed -= OnJoinServerFailed;
            
            // Stop any ongoing server discovery when panel is closed
            matchmakingManager.StopServerDiscovery();
        }
        
        // Clean up the UI
        ClearServerList();
        ClearStatusMessage();
    }
    
    // Refresh the server list
    public void RefreshServerList()
    {
        if (matchmakingManager != null)
        {
            // First clear any existing entries
            ClearServerList();
            
            // Show loading status
            ShowStatusMessage("Refreshing server list...");
            
            // Request the MatchmakingManager to clear its cached data
            matchmakingManager.ClearAllServerData();
            
            // Start server discovery
            matchmakingManager.StartServerDiscovery();
        }
        else
        {
            Debug.LogError("ServerBrowser: MatchmakingManager reference is missing!");
            ShowStatusMessage("Error: MatchmakingManager not found!");
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
        // Add robust null checks and logging
        Debug.Log($"ServerBrowser: OnServerListUpdated called with {(servers != null ? servers.Count : 0)} servers");
        
        // Try to ensure references are valid before proceeding
        EnsureReferencesExist();
        
        if (serverListContent == null)
        {
            Debug.LogError("ServerBrowser: serverListContent is still null in OnServerListUpdated! Cannot display servers.");
            return;
        }
        
        try
        {
            ClearServerList();
        }
        catch (System.NullReferenceException e)
        {
            Debug.LogError($"ServerBrowser: Error clearing server list: {e.Message}");
        }
        
        if (servers == null || servers.Count == 0)
        {
            ShowStatusMessage("No servers found. Make sure a server is running and try again.");
            return;
        }
        
        // Hide status text if we have servers
        ClearStatusMessage();
        
        for (int i = 0; i < servers.Count; i++)
        {
            if (servers[i] == null)
            {
                Debug.LogWarning($"ServerBrowser: Server at index {i} is null, skipping");
                continue;
            }
            
            try
            {
                CreateServerEntry(servers[i], i);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ServerBrowser: Error creating server entry: {e.Message}");
            }
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
        
        Debug.Log($"ServerBrowser: Creating server entry for {server.serverName} ({server.ipAddress}:{server.port})");
        
        // Instantiate the server entry prefab
        GameObject entry = Instantiate(serverEntryPrefab, serverListContent);
        entry.name = $"ServerEntry_{index}_{server.serverName}";
        
        // Find components in the prefab
        TextMeshProUGUI serverNameText = entry.transform.Find("TXTServerName")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI playerCountText = entry.transform.Find("TXTPlayerCount")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI pingText = entry.transform.Find("TXTPing")?.GetComponent<TextMeshProUGUI>();
        Button joinButton = entry.transform.Find("BTNJoin")?.GetComponent<Button>();
        
        // Set server information
        if (serverNameText != null)
        {
            serverNameText.text = server.serverName;
        }
        
        if (playerCountText != null)
        {
            playerCountText.text = $"{server.currentPlayers}/{server.maxPlayers}";
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
            joinButton.onClick.AddListener(() => OnJoinButtonClicked(serverIdx));
        }
        
        // Add to our list for tracking
        serverEntries.Add(entry);
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
    
    /// <summary>
    /// Completely resets the server browser state, clearing all entries and cached data
    /// </summary>
    public void ClearServerData()
    {
        Debug.Log("ServerBrowser: Clearing all server data");
        
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
            ShowStatusMessage("Ready to search for servers...");
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
}
