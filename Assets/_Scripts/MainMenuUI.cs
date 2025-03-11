using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI controller for the main menu with Create and Join match options
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject serverBrowserPanel;
    
    [Header("Main Menu Buttons")]
    [SerializeField] private Button createMatchButton;
    [SerializeField] private Button joinMatchButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    
    [Header("Create Match UI")]
    [SerializeField] private GameObject createMatchPanel;
    [SerializeField] private TMP_InputField serverNameInput;
    [SerializeField] private Button createServerButton;
    [SerializeField] private Button cancelCreateButton;
    
    // Reference to the server browser
    private ServerBrowser serverBrowser;
    
    private void Awake()
    {
        // Just find existing ServerBrowser if it exists, don't create one yet
        serverBrowser = FindObjectOfType<ServerBrowser>();
        if (serverBrowser != null)
        {
            Debug.Log("MainMenuUI: Found existing ServerBrowser");
        }
        
        // Set up button listeners
        if (createMatchButton != null)
            createMatchButton.onClick.AddListener(OnCreateMatchClicked);
            
        if (joinMatchButton != null)
            joinMatchButton.onClick.AddListener(OnJoinMatchClicked);
            
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsClicked);
            
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);
            
        if (serverNameInput != null)
            serverNameInput.text = "My Game Server";
            
        if (createServerButton != null)
            createServerButton.onClick.AddListener(OnCreateServerClicked);
            
        if (cancelCreateButton != null)
            cancelCreateButton.onClick.AddListener(OnCancelCreateClicked);
            
        // Set initial UI state
        ShowMainMenuPanel();
    }
    
    #region Button Event Handlers
    
    // Create Match button clicked
    private void OnCreateMatchClicked()
    {
        ShowCreateMatchPanel();
    }
    
    // Join Match button clicked
    private void OnJoinMatchClicked()
    {
        ShowServerBrowserPanel();
    }
    
    // Settings button clicked
    private void OnSettingsClicked()
    {
        // Show settings panel if needed
        Debug.Log("Settings clicked");
    }
    
    // Quit button clicked
    private void OnQuitClicked()
    {
        // Quit the application
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
    
    // Create Server button clicked
    private void OnCreateServerClicked()
    {
        Debug.Log("MainMenuUI: OnCreateServerClicked()");
        
        // Find the MatchmakingManager directly
        MatchmakingManager matchmakingManager = FindObjectOfType<MatchmakingManager>();
        
        if (matchmakingManager == null)
        {
            Debug.LogError("MainMenuUI: MatchmakingManager not found! Cannot create server.");
            return;
        }
            
        // Get server name from input field
        string serverName = "My Game Server";
        if (serverNameInput != null && !string.IsNullOrEmpty(serverNameInput.text))
        {
            serverName = serverNameInput.text;
        }
        
        Debug.Log($"MainMenuUI: Creating server with name: {serverName}");
        
        // Hide the create match panel
        HideCreateMatchPanel();
        
        // Create the server directly through MatchmakingManager
        matchmakingManager.CreateServer(serverName);
        
        Debug.Log("MainMenuUI: CreateServer method called successfully");
    }
    
    // Cancel Create button clicked
    private void OnCancelCreateClicked()
    {
        HideCreateMatchPanel();
        ShowMainMenuPanel();
    }
    
    #endregion
    
    #region UI Panel Management
    
    // Show the main menu panel
    public void ShowMainMenuPanel()
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
            
        if (serverBrowserPanel != null)
            serverBrowserPanel.SetActive(false);
            
        if (createMatchPanel != null)
            createMatchPanel.SetActive(false);
    }
    
    // Show the server browser panel
    public void ShowServerBrowserPanel()
    {
        // Hide all panels first
        HideAllPanels();
        
        // Find the ServerBrowser if we don't have a reference to it
        if (serverBrowser == null)
        {
            // First check if it's attached to our ServerBrowserPanel
            if (serverBrowserPanel != null)
            {
                serverBrowser = serverBrowserPanel.GetComponent<ServerBrowser>();
                
                // If not, look throughout the scene
                if (serverBrowser == null)
                {
                    serverBrowser = FindObjectOfType<ServerBrowser>();
                    
                    // As a last resort, create a new one
                    if (serverBrowser == null)
                    {
                        Debug.LogWarning("MainMenuUI: ServerBrowser component not found in scene! Creating one now, but this should be set up in the Editor.");
                        EnsureServerBrowserExists();
                    }
                }
            }
            else
            {
                Debug.LogError("MainMenuUI: serverBrowserPanel is null! Cannot show server browser. Please assign this in the Inspector.");
                return;
            }
        }
        
        // Reset the server browser completely to avoid stale data
        if (serverBrowser != null)
        {
            Debug.Log("MainMenuUI: Reset ServerBrowser before showing panel");
            
            // Ensure UI components are properly set up
            serverBrowser.SetupUIComponents();
            
            // Clear any existing data to ensure a fresh start
            serverBrowser.ClearServerData();
            
            // Activate the panel
            if (serverBrowserPanel != null)
            {
                serverBrowserPanel.SetActive(true);
            }
            else
            {
                Debug.LogError("MainMenuUI: serverBrowserPanel is null! Cannot show server browser.");
            }
        }
        else
        {
            Debug.LogError("MainMenuUI: Failed to find or create ServerBrowser component!");
        }
    }
    
    // Show the create match panel
    public void ShowCreateMatchPanel()
    {
        // Hide all panels first
        HideAllPanels();
        
        // Show only the create match panel
        if (createMatchPanel != null)
            createMatchPanel.SetActive(true);
        else
            Debug.LogError("MainMenuUI: createMatchPanel is null! Cannot show create match panel.");
    }
    
    // Hide the create match panel
    public void HideCreateMatchPanel()
    {
        if (createMatchPanel != null)
            createMatchPanel.SetActive(false);
    }
    
    // Hide all panels
    private void HideAllPanels()
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
            
        if (serverBrowserPanel != null)
            serverBrowserPanel.SetActive(false);
            
        if (createMatchPanel != null)
            createMatchPanel.SetActive(false);
    }
    
    // Ensure the ServerBrowser exists and is properly set up
    private void EnsureServerBrowserExists()
    {
        serverBrowser = FindObjectOfType<ServerBrowser>();
        
        if (serverBrowser == null)
        {
            Debug.Log("MainMenuUI: ServerBrowser not found. Creating one...");
            
            // Check if we have a ServerBrowserPanel to attach it to
            if (serverBrowserPanel != null)
            {
                // First, make sure the panel has the required UI elements
                Transform content = serverBrowserPanel.transform.Find("Scroll View/Viewport/Content");
                if (content == null)
                {
                    Debug.LogError("MainMenuUI: ServerBrowserPanel is missing the required 'Content' child. Please set up the UI hierarchy properly.");
                    // Create missing UI elements
                    GameObject scrollView = new GameObject("Scroll View", typeof(ScrollRect));
                    scrollView.transform.SetParent(serverBrowserPanel.transform, false);
                    
                    GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
                    viewport.transform.SetParent(scrollView.transform, false);
                    
                    GameObject contentGO = new GameObject("Content", typeof(RectTransform));
                    contentGO.transform.SetParent(viewport.transform, false);
                    
                    // Configure ScrollRect
                    ScrollRect scrollRect = scrollView.GetComponent<ScrollRect>();
                    scrollRect.viewport = viewport.GetComponent<RectTransform>();
                    scrollRect.content = contentGO.GetComponent<RectTransform>();
                    
                    content = contentGO.transform;
                }
                
                // Add the ServerBrowser component
                serverBrowser = serverBrowserPanel.AddComponent<ServerBrowser>();
                
                // Set up the UI components
                serverBrowser.SetupUIComponents();
                
                Debug.Log("MainMenuUI: Added ServerBrowser component to serverBrowserPanel");
            }
            else
            {
                // Create a new GameObject for the ServerBrowser
                GameObject serverBrowserGO = new GameObject("ServerBrowser");
                serverBrowser = serverBrowserGO.AddComponent<ServerBrowser>();
                Debug.Log("MainMenuUI: Created new ServerBrowser GameObject");
                
                // In this case, we need to create a whole UI hierarchy
                // This would get complex, so recommend setting up in the Editor instead
                Debug.LogWarning("MainMenuUI: Created ServerBrowser without proper UI hierarchy. Please set up the UI in the Editor.");
            }
        }
        else
        {
            Debug.Log("MainMenuUI: Found existing ServerBrowser");
        }
    }
    
    #endregion
}

