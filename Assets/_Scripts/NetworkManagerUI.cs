using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using TMPro;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class NetworkManagerUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] public TMP_InputField ipInputField;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject startupPanel;
    [SerializeField] private CanvasGroup startupCanvasGroup;
    [SerializeField] private float fadeOutDuration = 0.5f;
    
    [Header("Network Settings")]
    [SerializeField] private string defaultIP = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;
    
    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private float sceneLoadDelay = 1.0f;
    
    [Header("Character Selection")]
    [SerializeField] private GameObject[] characterPrefabs;
    [SerializeField] private Button[] characterSelectionButtons;
    [SerializeField] private int defaultCharacterIndex = 0;
    
    // Singleton pattern
    public static NetworkManagerUI Instance { get; private set; }
    
    // Track the selected character
    private int selectedCharacterIndex;
    
    // Static getter to allow PlayerSpawner to access the selected character prefab
    public static GameObject SelectedCharacterPrefab { get; private set; }
    
    // Add getter for character prefabs array for debugging and UI purposes
    /// <summary>
    /// Gets the available character prefabs.
    /// </summary>
    public GameObject[] CharacterPrefabs => characterPrefabs;
    
    // Add a flag to track if we should show the lobby
    private bool shouldShowLobby = false;
    
    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // Set the default character
        selectedCharacterIndex = defaultCharacterIndex;
        if (characterPrefabs != null && characterPrefabs.Length > 0 && 
            selectedCharacterIndex >= 0 && selectedCharacterIndex < characterPrefabs.Length)
        {
            SelectedCharacterPrefab = characterPrefabs[selectedCharacterIndex];
        }
        
        // Setup character selection buttons
        SetupCharacterSelectionButtons();
        
        // Check if NetworkManager exists, if not create one
        if (NetworkManager.Singleton == null)
        {
            Debug.Log("Creating a NetworkManager as none was found in the scene.");
            GameObject networkManagerObj = new GameObject("NetworkManager");
            NetworkManager networkManager = networkManagerObj.AddComponent<NetworkManager>();
            
            // Add and configure transport
            var transport = networkManagerObj.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            
            // Set the transport on the NetworkManager
            networkManager.NetworkConfig = new Unity.Netcode.NetworkConfig
            {
                PlayerPrefab = null,
                ConnectionApproval = true,
            };
            
            // Must explicitly set the transport
            networkManager.NetworkConfig.NetworkTransport = transport;
            
            // Configure transport
            transport.ConnectionData.Address = defaultIP;
            transport.ConnectionData.Port = defaultPort;
            
            DontDestroyOnLoad(networkManagerObj);
        }
        
        // Make sure the NetworkManager persists between scenes
        if (NetworkManager.Singleton != null)
        {
            DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
            
            // Make sure NetworkConfig is not null before accessing it
            if (NetworkManager.Singleton.NetworkConfig != null)
            {
                // Configure NetworkManager to prevent auto-spawning players
                NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
                
                // Enable connection approval to control when players can join
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            }
            else
            {
                Debug.LogError("NetworkManager.Singleton.NetworkConfig is null. The NetworkManager may not be properly initialized.");
            }
            
            // These can be outside the NetworkConfig check
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApproveConnection; // Remove any previous callbacks
            NetworkManager.Singleton.ConnectionApprovalCallback += ApproveConnection;
        }
        else
        {
            Debug.LogError("NetworkManager.Singleton is null. Make sure there is a NetworkManager in the scene.");
        }
        
        // Set default IP
        if (ipInputField != null)
            ipInputField.text = defaultIP;
        
        // Update status text
        UpdateStatusText("Disconnected");
        
        // Get CanvasGroup if not assigned
        if (startupCanvasGroup == null && startupPanel != null)
        {
            startupCanvasGroup = startupPanel.GetComponent<CanvasGroup>();
            if (startupCanvasGroup == null)
            {
                startupCanvasGroup = startupPanel.AddComponent<CanvasGroup>();
            }
        }
        
        // Register network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
        else
        {
            Debug.LogError("NetworkManager.Singleton is still null after attempt to create it.");
        }
    }
    
    private void SetupCharacterSelectionButtons()
    {
        if (characterSelectionButtons == null || characterSelectionButtons.Length == 0)
        {
            Debug.LogWarning("No character selection buttons assigned!");
            return;
        }
        
        // Setup each character selection button
        for (int i = 0; i < characterSelectionButtons.Length; i++)
        {
            if (characterSelectionButtons[i] != null)
            {
                int characterIndex = i; // Need to capture the index for the lambda
                characterSelectionButtons[i].onClick.AddListener(() => SelectCharacter(characterIndex));
            }
        }
    }
    
    /// <summary>
    /// Selects a character prefab by index. This can be called by other UI elements like CharacterSelection.
    /// </summary>
    /// <param name="characterIndex">Index of the character in the characterPrefabs array</param>
    public void SelectCharacter(int characterIndex)
    {
        if (characterPrefabs == null || characterPrefabs.Length == 0)
        {
            Debug.LogError("No character prefabs available for selection!");
            return;
        }
        
        // Ensure valid index
        if (characterIndex >= 0 && characterIndex < characterPrefabs.Length)
        {
            selectedCharacterIndex = characterIndex;
            SelectedCharacterPrefab = characterPrefabs[characterIndex];
            Debug.Log($"Selected character: {SelectedCharacterPrefab.name}");
            
            // Highlight the selected button (optional)
            UpdateCharacterSelectionUI();
        }
    }
    
    private void UpdateCharacterSelectionUI()
    {
        // Update visuals for character selection buttons
        for (int i = 0; i < characterSelectionButtons.Length; i++)
        {
            if (characterSelectionButtons[i] != null)
            {
                // Highlight the selected button and unhighlight others
                ColorBlock colors = characterSelectionButtons[i].colors;
                colors.normalColor = (i == selectedCharacterIndex) 
                    ? new Color(0.8f, 0.8f, 1f) // Light blue for selected
                    : Color.white;
                characterSelectionButtons[i].colors = colors;
            }
        }
    }
    
    private void Start()
    {
        // This UI should persist if we're in the main menu
        if (SceneManager.GetActiveScene().name != gameSceneName)
        {
            DontDestroyOnLoad(gameObject);
        }
        
        // Initialize character selection
        SelectCharacter(selectedCharacterIndex);
    }
    
    private void OnDestroy()
    {
        // Unregister network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApproveConnection;
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected with ID: {clientId}, Local client ID: {NetworkManager.Singleton.LocalClientId}");
        
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("This is our local client that connected!");
            UpdateStatusText("Connected as " + (NetworkManager.Singleton.IsHost ? "Host" : "Client"));
            
            // Only show the lobby if the flag is set (meaning we came from the host/client panels)
            if (shouldShowLobby)
            {
                ShowLobby();
            }
        }
        else if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
        {
            Debug.Log($"Remote client {clientId} connected to the server");
            
            // Add the newly connected client to the lobby player list
            string playerName = "Player " + clientId;
            LobbyController.Instance?.AddPlayer(clientId, playerName);
        }
    }
    
    // Add a new method to show the lobby
    private void ShowLobby()
    {
        // Hide the startup UI
        if (startupPanel != null)
        {
            startupPanel.SetActive(false);
        }
        
        // Show the lobby using LobbyController
        LobbyController lobbyController = FindObjectOfType<LobbyController>();
        if (lobbyController != null)
        {
            lobbyController.ShowLobby();
        }
        else
        {
            Debug.LogWarning("LobbyController not found!");
        }
    }
    
    // Make LoadGameSceneAfterDelay public so the LobbyController can call it when the host clicks "Start"
    public IEnumerator LoadGameSceneAfterDelay()
    {
        yield return new WaitForSeconds(sceneLoadDelay);
        
        Debug.Log($"Loading game scene: {gameSceneName}");
        
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            // Register a callback for when the scene is loaded to setup camera
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnGameSceneLoaded;
            
            // If we're the host/server, use NetworkManager to switch scenes
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            // Clients don't need to load the scene - server will handle it
            Debug.Log("Client waiting for server to change scene...");
        }
    }
    
    // This is called when the game scene is fully loaded
    private void OnGameSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        Debug.Log($"Game scene loaded: {sceneName}");
        
        // Unregister the callback to avoid multiple calls
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnGameSceneLoaded;
        
        // Setup camera after a short delay to ensure player is spawned
        StartCoroutine(SetupCameraAfterDelay());
    }
    
    private IEnumerator SetupCameraAfterDelay()
    {
        // Wait longer for the player to be fully spawned (more reliable)
        yield return new WaitForSeconds(1.0f);
        
        // Find the camera and make it follow the player
        CameraFollow cameraFollow = FindObjectOfType<CameraFollow>();
        if (cameraFollow != null)
        {
            Debug.Log("Found CameraFollow, triggering player search");
            cameraFollow.FindLocalPlayerWithRetry();
        }
        else
        {
            Debug.LogWarning("CameraFollow component not found in the scene");
            
            // Try to find it again after a delay (in case it's being instantiated)
            yield return new WaitForSeconds(0.5f);
            cameraFollow = FindObjectOfType<CameraFollow>();
            if (cameraFollow != null)
            {
                Debug.Log("Found CameraFollow on second try, triggering player search");
                cameraFollow.FindLocalPlayerWithRetry();
            }
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            UpdateStatusText("Disconnected");
            
            // Return to the main menu scene if we're not already there
            if (SceneManager.GetActiveScene().name != SceneManager.GetSceneByBuildIndex(0).name)
            {
                SceneManager.LoadScene(0); // Load the first scene (main menu)
            }
            else
            {
                // Show the startup panel again
                if (startupPanel != null)
                {
                    startupPanel.SetActive(true);
                    if (startupCanvasGroup != null)
                    {
                        startupCanvasGroup.alpha = 1f;
                        startupCanvasGroup.interactable = true;
                        startupCanvasGroup.blocksRaycasts = true;
                    }
                }
            }
        }
        else if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
        {
            Debug.Log($"Client {clientId} disconnected from the server");
        }
    }
    
    private void OnServerStarted()
    {
        UpdateStatusText("Server started. Waiting for connections...");
    }
    
    private void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = "Status: " + message;
        }
        Debug.Log("Network Status: " + message);
    }
    
    // Helper method to get local IP address
    public static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }
    
    // Display the local IP in the UI
    public void ShowLocalIP()
    {
        string localIP = GetLocalIPAddress();
        UpdateStatusText("Your IP: " + localIP);
    }
    
    // Connection approval callback - this controls when players can join
    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        Debug.Log($"Connection approval requested for client ID: {request.ClientNetworkId}");
        
        // Always approve the connection
        response.Approved = true;
        response.CreatePlayerObject = false; // Don't create player object automatically
        response.Position = Vector3.zero;
        response.Rotation = Quaternion.identity;
        response.Pending = false;
    }
    
    public void OnHostButtonClicked()
    {
        // Start as host (both server and client)
        if (NetworkManager.Singleton != null)
        {
            // Set the flag to show lobby after connection
            shouldShowLobby = true;
            
            // We'll use SceneManagement to handle player spawning after scene load
            Debug.Log("Starting host - NO player spawning until Game scene loads");
            
            // Check for and configure transport
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("Unity Transport component missing! Adding one...");
                transport = NetworkManager.Singleton.gameObject.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            }
            
            // Configure transport data
            transport.ConnectionData.Address = defaultIP;
            transport.ConnectionData.Port = defaultPort;
            
            // Check if NetworkConfig is not null
            if (NetworkManager.Singleton.NetworkConfig != null)
            {
                // Double-check that player spawning is disabled and connection approval is set up
                NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            }
            else
            {
                Debug.LogError("NetworkManager.Singleton.NetworkConfig is null. Creating a new one.");
                NetworkManager.Singleton.NetworkConfig = new Unity.Netcode.NetworkConfig
                {
                    PlayerPrefab = null,
                    ConnectionApproval = true,
                    NetworkTransport = transport
                };
            }
            
            // Make sure our callback is registered
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApproveConnection;
            NetworkManager.Singleton.ConnectionApprovalCallback += ApproveConnection;
            
            try
            {
                NetworkManager.Singleton.StartHost();
                Debug.Log("Host started successfully");
                
                // Fade out the UI
                StartCoroutine(FadeOutUI());
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to start host: {ex.Message}\n{ex.StackTrace}");
            }
        }
        else
        {
            Debug.LogError("NetworkManager.Singleton is null when trying to start host!");
        }
    }
    
    public void OnClientButtonClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is null when trying to start client!");
            return;
        }
        
        // Set the flag to show lobby after connection
        shouldShowLobby = true;
        
        // Get IP from input field
        string ipAddress = ipInputField != null ? ipInputField.text : defaultIP;
        if (string.IsNullOrEmpty(ipAddress))
        {
            ipAddress = defaultIP;
        }
        
        // Check for and configure transport
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("Unity Transport component missing! Adding one...");
            transport = NetworkManager.Singleton.gameObject.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        }
        
        // Configure transport data
        transport.ConnectionData.Address = ipAddress;
        transport.ConnectionData.Port = defaultPort;
        
        // Check if NetworkConfig is not null
        if (NetworkManager.Singleton.NetworkConfig != null)
        {
            // Make absolutely sure we don't spawn a player in main menu
            NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        }
        else
        {
            Debug.LogError("NetworkManager.Singleton.NetworkConfig is null. Creating a new one.");
            NetworkManager.Singleton.NetworkConfig = new Unity.Netcode.NetworkConfig
            {
                PlayerPrefab = null,
                ConnectionApproval = true,
                NetworkTransport = transport
            };
        }
        
        // Make sure our callback is registered
        NetworkManager.Singleton.ConnectionApprovalCallback -= ApproveConnection;
        NetworkManager.Singleton.ConnectionApprovalCallback += ApproveConnection;
        
        try
        {
            // Start as client
            Debug.Log("Starting client - NO player spawning until Game scene loads");
            NetworkManager.Singleton.StartClient();
            UpdateStatusText("Connecting to " + ipAddress + "...");
            
            // Fade out the UI
            StartCoroutine(FadeOutUI());
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to start client: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    private IEnumerator FadeOutUI()
    {
        if (startupCanvasGroup == null)
            yield break;
            
        float startTime = Time.time;
        float startAlpha = startupCanvasGroup.alpha;
        
        while (Time.time < startTime + fadeOutDuration)
        {
            float t = (Time.time - startTime) / fadeOutDuration;
            startupCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }
        
        startupCanvasGroup.alpha = 0f;
        startupCanvasGroup.interactable = false;
        startupCanvasGroup.blocksRaycasts = false;
        
        // Optionally disable the panel after fading
        if (startupPanel != null)
            startupPanel.SetActive(false);
    }
} 