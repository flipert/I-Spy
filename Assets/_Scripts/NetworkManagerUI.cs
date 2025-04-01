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
using Unity.Netcode.Transports.UTP;
using System;

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

    [Header("NPC Prefabs")]
    [SerializeField] private GameObject npcPrefab;
    
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

    // Dictionary to keep track of registered prefabs
    private Dictionary<uint, GameObject> registeredPrefabs = new Dictionary<uint, GameObject>();
    
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
        
        // Setup character selection buttons
        SetupCharacterSelectionButtons();
        
        // Select default character
        selectedCharacterIndex = Mathf.Clamp(defaultCharacterIndex, 0, characterPrefabs.Length - 1);
        
        // Make sure there is a NetworkManager
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("NetworkManager not found, creating one");
            GameObject networkManagerObj = new GameObject("NetworkManager");
            NetworkManager networkManager = networkManagerObj.AddComponent<NetworkManager>();
            
            // Add Unity Transport
            networkManagerObj.AddComponent<UnityTransport>();
            
            // Ensure NetworkConfig is created
            networkManager.NetworkConfig = new NetworkConfig();
            
            DontDestroyOnLoad(networkManagerObj);
        }
        
        // Make sure the NetworkManager persists between scenes
        if (NetworkManager.Singleton != null)
        {
            DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
            
            // Make sure NetworkConfig is not null before accessing it
            if (NetworkManager.Singleton.NetworkConfig == null)
            {
                Debug.LogWarning("NetworkConfig is null, creating a new one");
                NetworkManager.Singleton.NetworkConfig = new NetworkConfig();
            }
            
            // Now we can safely configure the NetworkConfig
            NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            
            // Register all necessary prefabs with NetworkManager
            RegisterAllNetworkPrefabs();
            
            // These can be outside the NetworkConfig check
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApproveConnection; // Remove any previous callbacks
            NetworkManager.Singleton.ConnectionApprovalCallback += ApproveConnection;

            // Subscribe to transport failure events
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
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

    // New method to register all network prefabs
    private void RegisterAllNetworkPrefabs()
    {
        // Clear our tracking dictionary
        registeredPrefabs.Clear();

        // Create a new NetworkPrefabs list to ensure compatibility
        NetworkManager.Singleton.NetworkConfig.Prefabs = new NetworkPrefabs();

        // Register all character prefabs
        foreach (GameObject prefab in characterPrefabs)
        {
            if (prefab != null)
            {
                RegisterNetworkPrefab(prefab);
            }
        }

        // Register NPC prefab if available
        if (npcPrefab != null)
        {
            RegisterNetworkPrefab(npcPrefab);
        }

        // Add any other required prefabs here
        Debug.Log($"Registered {registeredPrefabs.Count} network prefabs");
        
        // Log all registered prefabs for debugging
        foreach (var prefab in registeredPrefabs)
        {
            Debug.Log($"Registered prefab: {prefab.Value.name} with hash {prefab.Key}");
        }
    }

    // Helper method to register a single prefab
    private void RegisterNetworkPrefab(GameObject prefab)
    {
        if (prefab == null)
            return;

        NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError($"Prefab {prefab.name} is missing NetworkObject component!");
            return;
        }

        // Calculate a hash from the prefab name for tracking
        uint prefabHash = (uint)prefab.name.GetHashCode();
        
        // Skip if already registered
        if (registeredPrefabs.ContainsKey(prefabHash))
        {
            Debug.LogWarning($"Prefab {prefab.name} is already registered");
            return;
        }

        // Add to our tracking dictionary
        registeredPrefabs[prefabHash] = prefab;
        
        // Add to NetworkConfig
        NetworkManager.Singleton.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });
        
        Debug.Log($"Registered network prefab: {prefab.name} with hash {prefabHash}");
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
            
            // Make sure this prefab is registered with NetworkManager
            if (NetworkManager.Singleton?.NetworkConfig?.Prefabs != null)
            {
                RegisterNetworkPrefab(SelectedCharacterPrefab);
            }
            
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
        
        // Initialize character selection - make sure SelectedCharacterPrefab is set
        if (SelectedCharacterPrefab == null && characterPrefabs != null && characterPrefabs.Length > 0)
        {
            selectedCharacterIndex = Mathf.Clamp(defaultCharacterIndex, 0, characterPrefabs.Length - 1);
            SelectedCharacterPrefab = characterPrefabs[selectedCharacterIndex];
            Debug.Log($"Start: Initialized selected character to {SelectedCharacterPrefab.name}");
        }
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
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
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
        try
        {
            // First try to get the public IP address
            using (var client = new WebClient())
            {
                string publicIP = client.DownloadString("https://api.ipify.org");
                Debug.Log($"Public IP: {publicIP}");
                return publicIP;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Could not get public IP: {ex.Message}. Falling back to local IP.");
            
            // Fallback to local IP
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    Debug.Log($"Local IP: {ip}");
                    return ip.ToString();
                }
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
        Debug.Log($"Connection request received from client {request.ClientNetworkId}");
        response.Approved = true;
    }
    
    public void OnClientButtonClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is null when trying to start client!");
            UpdateStatusText("Error: NetworkManager not found!");
            return;
        }
        
        // Set the flag to show lobby after connection
        shouldShowLobby = true;
        
        // Get IP from input field
        string ipAddress = ipInputField != null ? ipInputField.text.Trim() : defaultIP;
        if (string.IsNullOrEmpty(ipAddress))
        {
            ipAddress = defaultIP;
        }
        
        Debug.Log($"Attempting to connect to IP: {ipAddress}");
        UpdateStatusText($"Attempting to connect to {ipAddress}...");
        
        // Check for and configure transport
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("Unity Transport component missing! Adding one...");
            transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
        }
        
        // Configure transport data
        transport.ConnectionData.Address = ipAddress;
        transport.ConnectionData.Port = defaultPort;
        
        // Make connection more reliable
        transport.MaxConnectAttempts = 3;
        transport.ConnectTimeoutMS = 10000; // 10 seconds
        
        Debug.Log($"Transport configured - Address: {transport.ConnectionData.Address}, Port: {transport.ConnectionData.Port}");
        
        // Ensure NetworkConfig is properly set up
        if (NetworkManager.Singleton.NetworkConfig == null)
        {
            NetworkManager.Singleton.NetworkConfig = new NetworkConfig();
        }
        
        NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        
        // Register all prefabs before starting the client
        RegisterAllNetworkPrefabs();
        
        // Store reference to the active coroutine so we can stop it if needed
        StartCoroutine(ShowConnectingUI());
        
        try
        {
            Debug.Log("Starting client connection attempt...");
            NetworkManager.Singleton.StartClient();
            LogRegisteredPrefabs();
            
            // Start a new coroutine that checks connection status and handles timeout
            StartCoroutine(ConnectionTimeoutCheck());
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start client: {ex.Message}\n{ex.StackTrace}");
            UpdateStatusText($"Connection failed: {ex.Message}");
            StopAllCoroutines();
            RecoverUI("Exception during connection attempt. Please try again.");
        }
    }
    
    private IEnumerator ShowConnectingUI()
    {
        // Show connecting animation/text
        UpdateStatusText("Connecting...");
        
        float blinkInterval = 0.5f;
        int dotCount = 0;
        
        while (true)
        {
            dotCount = (dotCount + 1) % 4;
            string dots = new string('.', dotCount);
            UpdateStatusText($"Connecting{dots}");
            yield return new WaitForSeconds(blinkInterval);
        }
    }
    
    private IEnumerator ConnectionTimeoutCheck()
    {
        float timeoutDuration = 15f; // 15 seconds timeout
        float startTime = Time.time;
        
        // Periodically check connection status
        while (Time.time - startTime < timeoutDuration)
        {
            // If connected successfully, stop checking
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                Debug.Log("Successfully connected to host!");
                StopCoroutine(ShowConnectingUI());
                StartCoroutine(FadeOutUI());
                yield break;
            }
            
            // If disconnected (connection failed after initial attempt), recover UI
            if (NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsConnectedClient)
            {
                Debug.LogWarning("Connection attempt failed - network is listening but not connected");
                StopCoroutine(ShowConnectingUI());
                RecoverUI("Connection attempt failed. Please check the IP address and try again.");
                yield break;
            }
            
            yield return new WaitForSeconds(0.5f);
        }
        
        // If we get here, connection timed out
        Debug.LogWarning("Connection attempt timed out after " + timeoutDuration + " seconds");
        StopCoroutine(ShowConnectingUI());
        
        // Try to clean up the failed connection
        if (NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        RecoverUI("Connection timed out. Please check the IP address and ensure port 7777 is forwarded on the host.");
    }
    
    // Helper method to recover UI after a failed connection attempt
    private void RecoverUI(string errorMessage)
    {
        // Show the error message
        UpdateStatusText(errorMessage);
        
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
    
    public void OnHostButtonClicked()
    {
        if (NetworkManager.Singleton != null)
        {
            shouldShowLobby = true;
            
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("Unity Transport component missing! Adding one...");
                transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
            }
            
            // When hosting, bind to all available network interfaces
            transport.ConnectionData.Address = "0.0.0.0";
            transport.ConnectionData.Port = defaultPort;
            
            // Make connection more reliable
            transport.MaxConnectAttempts = 3;
            transport.ConnectTimeoutMS = 10000; // 10 seconds
            
            Debug.Log($"Host binding to all interfaces (0.0.0.0) on port {defaultPort}");
            
            // Show the IP address that others should use to connect
            string publicIP = GetLocalIPAddress();
            UpdateStatusText($"Starting host... Others can connect to: {publicIP}");
            
            // Ensure NetworkConfig is properly set up
            if (NetworkManager.Singleton.NetworkConfig == null)
            {
                NetworkManager.Singleton.NetworkConfig = new NetworkConfig();
            }
            
            NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            
            // Register all prefabs before starting the host
            RegisterAllNetworkPrefabs();
            
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApproveConnection;
            NetworkManager.Singleton.ConnectionApprovalCallback += ApproveConnection;
            
            try
            {
                NetworkManager.Singleton.StartHost();
                Debug.Log("Host started successfully");
                LogRegisteredPrefabs();
                StartCoroutine(FadeOutUI());
                
                // Display a message about port forwarding
                Debug.Log("IMPORTANT: Make sure port 7777 is forwarded in your router to allow external connections");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to start host: {ex.Message}\n{ex.StackTrace}");
                UpdateStatusText($"Failed to start host: {ex.Message}");
            }
        }
        else
        {
            Debug.LogError("NetworkManager.Singleton is null when trying to start host!");
            UpdateStatusText("Error: NetworkManager not found!");
        }
    }
    
    // Helper method to log all registered prefabs for debugging
    private void LogRegisteredPrefabs()
    {
        if (NetworkManager.Singleton?.NetworkConfig?.Prefabs != null)
        {
            int prefabCount = registeredPrefabs.Count;
            Debug.Log($"Total registered network prefabs: {prefabCount}");
            foreach (var prefab in registeredPrefabs)
            {
                Debug.Log($"Registered prefab: {prefab.Value.name}");
            }
        }
        else
        {
            Debug.LogError("Cannot list registered prefabs - NetworkConfig or Prefabs list is null");
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

    private void OnTransportFailure()
    {
        Debug.LogError("Transport failure occurred. Check your network settings and port forwarding.");
        UpdateStatusText("Connection failed: Transport error. Check your network settings.");
    }
} 