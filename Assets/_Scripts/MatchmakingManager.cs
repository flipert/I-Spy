using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

/// <summary>
/// Manages matchmaking, server discovery and connection for dedicated servers
/// </summary>
public class MatchmakingManager : MonoBehaviour
{
    [Header("Server Settings")]
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private int maxPlayersPerServer = 6;
    [SerializeField] private float serverRefreshRate = 5f;
    
    [Header("Network Discovery")]
    [SerializeField] private int discoveryPort = 47777;
    [SerializeField] private float broadcastInterval = 1f;
    [SerializeField] private float discoveryTimeout = 3f;
    
    [Header("Debug Settings")]
    [SerializeField] private bool showTestServersInEditor = false;
    [SerializeField] private bool debugMode = false;
    
    [Header("Character Selection")]
    [SerializeField] private GameObject[] characterPrefabs;
    [SerializeField] private int defaultCharacterIndex = 0;
    
    [Header("Unity Gaming Services")]
    [SerializeField] private bool useUnityServices = true;
    [SerializeField] private string lobbyName = "My Game Lobby";
    [SerializeField] private int relayRegionIndex = 0; // 0 = automatically select best region

    private string[] availableRegions = new string[] { "auto", "us-east", "us-west", "eu-west", "ap-south" };

    // Singleton instance
    public static MatchmakingManager Instance { get; private set; }
    
    // Server information structure
    [System.Serializable]
    public class ServerInfo
    {
        public string serverName;
        public string ipAddress;
        public ushort port;
        public int currentPlayers;
        public int maxPlayers;
        public bool inGame; // If true, the match has already started
        
        // New property for lobby ID
        public string lobbyId;
        
        // Default constructor needed for deserialization
        public ServerInfo() { }
        
        public ServerInfo(string name, string ip, ushort p, int current, int max, bool started)
        {
            serverName = name;
            ipAddress = ip;
            port = p;
            currentPlayers = current;
            maxPlayers = max;
            inGame = started;
        }
        
        // Override Equals for proper comparison
        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;
                
            ServerInfo other = (ServerInfo)obj;
            // Consider servers equal if they have the same IP and port
            return ipAddress == other.ipAddress && port == other.port;
        }
        
        // Override GetHashCode to match Equals
        public override int GetHashCode()
        {
            return (ipAddress + port.ToString()).GetHashCode();
        }
    }
    
    // List of discovered servers
    private List<ServerInfo> availableServers = new List<ServerInfo>();
    
    // Events
    public event Action<List<ServerInfo>> OnServerListUpdated;
    public event Action OnCreateServerSuccess;
    public event Action<string> OnCreateServerFailed;
    public event Action OnJoinServerSuccess;
    public event Action<string> OnJoinServerFailed;
    
    // Current server info
    private ServerInfo currentServerInfo;
    public ServerInfo CurrentServerInfo => currentServerInfo;
    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    public bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    public bool IsClient => NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
    
    private Coroutine serverDiscoveryCoroutine;
    private bool isRefreshingServers = false;
    
    // Discovery-related variables
    private UdpClient broadcastClient;
    private UdpClient discoveryClient;
    private CancellationTokenSource cancellationTokenSource;
    private bool isDiscoveryRunning = false;
    private Dictionary<string, float> discoveredServers = new Dictionary<string, float>();
    
    // Static property to replace NetworkManagerUI.SelectedCharacterPrefab
    public static GameObject SelectedCharacterPrefab { get; private set; }
    
    // Cloud matchmaking properties
    private Lobby currentLobby;
    private string relayJoinCode;
    private string playerId;
    private bool isServicesInitialized = false;
    private Dictionary<string, Lobby> availableLobbies = new Dictionary<string, Lobby>();
    private Coroutine lobbyHeartbeatCoroutine;
    private Coroutine lobbyUpdateCoroutine;
    
    // Add a timestamp to track when we last queried lobbies
    private float lastLobbyQueryTime = 0f;
    // Minimum time between lobby queries in seconds to avoid rate limiting
    [SerializeField] private float minLobbyQueryInterval = 15f; // Unity's rate limits are quite strict
    
    // Add flag to track if we're refreshing with Unity services
    private bool isRefreshingWithUnityServices = false;
    
    private async void Awake()
    {
        // Singleton setup with better logging
        if (Instance != null && Instance != this)
        {
            Debug.Log($"MatchmakingManager: Another instance already exists. Destroying duplicate at {gameObject.name}");
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Make sure NetworkManager exists and persists
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("MatchmakingManager: NetworkManager.Singleton is null. Creating one...");
            
            // Check if there's a NetworkManager in the scene but not initialized yet
            NetworkManager networkManager = FindObjectOfType<NetworkManager>();
            
            if (networkManager == null)
            {
                // Create a new NetworkManager
                GameObject networkManagerGO = new GameObject("NetworkManager");
                networkManager = networkManagerGO.AddComponent<NetworkManager>();
                
                // Add Unity Transport
                UnityTransport transport = networkManager.gameObject.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                
                // Initialize basic NetworkConfig
                if (networkManager.NetworkConfig == null)
                {
                    networkManager.NetworkConfig = new NetworkConfig();
                }
                
                networkManager.NetworkConfig.NetworkTransport = transport;
            }
            
            // Make sure it persists
            DontDestroyOnLoad(networkManager.gameObject);
        }
        else
        {
            Debug.Log("MatchmakingManager: Using existing NetworkManager.Singleton");
            
            // Make sure it has a properly configured transport
            if (NetworkManager.Singleton.NetworkConfig == null)
            {
                NetworkManager.Singleton.NetworkConfig = new NetworkConfig();
            }
            
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
            }
            
            // Set the transport
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            
            DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
        }
        
        // Initialize default character in Awake
        SetDefaultCharacter();
        
        // Initialize Unity Gaming Services if enabled
        if (useUnityServices)
        {
            Debug.Log("MatchmakingManager: Initializing Unity Gaming Services");
            try
            {
                await InitializeUnityServicesAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error initializing Unity Services: {ex.Message}");
                useUnityServices = false; // Fall back to local discovery
            }
        }
    }
    
    // Unity Gaming Services Initialization
    private async Task InitializeUnityServicesAsync()
    {
        try
        {
            // Initialize Unity Services
            await UnityServices.InitializeAsync();
            
            // Sign in anonymously
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            
            // Get player ID
            playerId = AuthenticationService.Instance.PlayerId;
            
            Debug.Log($"Player signed in with ID: {playerId}");
            isServicesInitialized = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize Unity Services: {ex.Message}");
            throw;
        }
    }
    
    private void Start()
    {
        // Clear old server data when starting the game
        #if UNITY_EDITOR
        // In editor mode, always clear old server data for testing
        ClearAllServerData();
        Debug.Log("MatchmakingManager: Cleared all server data at startup (editor mode)");
        #endif
        
        // Set up event listeners
        if (NetworkManager.Singleton != null)
        {
            // Initialize NetworkConfig if it doesn't exist
            if (NetworkManager.Singleton.NetworkConfig == null)
            {
                NetworkManager.Singleton.NetworkConfig = new NetworkConfig();
                Debug.Log("MatchmakingManager: Initialized missing NetworkConfig");
            }
            
            // Make sure we have a transport
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
                Debug.Log("MatchmakingManager: Added missing UnityTransport component");
            }
            
            // Set the transport in the NetworkConfig
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport == null)
            {
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
                Debug.Log("MatchmakingManager: Set transport in NetworkConfig");
            }
            
            // Ensure scene management is enabled
            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = true;
            
            // Make sure ForceSamePrefabs is set to false to allow for different character prefabs
            NetworkManager.Singleton.NetworkConfig.ForceSamePrefabs = false;
            
            // Check if we have the necessary callbacks registered
            if (NetworkManager.Singleton.ConnectionApprovalCallback == null)
            {
                NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;
                Debug.Log("MatchmakingManager: Set ConnectionApprovalCallback");
            }
            
            // Set up our network callbacks
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            
            // Set up scene management events
            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
                NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
                Debug.Log("MatchmakingManager: Scene manager callbacks registered");
            }
            else
            {
                Debug.LogWarning("MatchmakingManager: NetworkManager.SceneManager is null, scene events won't be processed");
            }
            
            Debug.Log("MatchmakingManager: Network callbacks registered successfully");
        }
        else
        {
            Debug.LogError("MatchmakingManager: NetworkManager.Singleton is null in Start!");
        }
        
        // Start server discovery 
        StartServerDiscovery();
    }
    
    private void OnDestroy()
    {
        Debug.Log("MatchmakingManager: OnDestroy called");
        
        // First unsubscribe from all events to prevent callbacks during shutdown
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            
            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
        }
        
        // If we're the host, clean up server registrations
        if (IsServer || IsHost)
        {
            Debug.Log("MatchmakingManager: Cleaning up server registrations on shutdown");
            CleanupOldServerRegistrations();
            
            // Stop broadcasting
            StopServerBroadcast();
        }
        
        // Check the state of the GameObject before proceeding with potentially problematic operations
        bool isActive = gameObject.activeInHierarchy;
        Debug.Log($"MatchmakingManager: GameObject is {(isActive ? "active" : "inactive")} during OnDestroy");
        
        // Safely stop server discovery (our improved method handles inactive GameObjects)
        StopServerDiscovery();
        
        // Perform final cleanup of network resources directly to avoid coroutine issues
        try
        {
            // Close any remaining network connections
            if (broadcastClient != null)
            {
                broadcastClient.Close();
                broadcastClient = null;
            }
            
            // Clean up discovery resources directly
            CleanupDiscoveryResources();
            
            // Make absolutely sure the token is cancelled
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MatchmakingManager: Non-critical error during final cleanup: {ex.Message}");
        }
        
        // Clean up Unity Gaming Services resources
        LeaveServer();
        
        Debug.Log("MatchmakingManager: OnDestroy completed successfully");
    }
    
    #region Server Creation and Management
    
    /// <summary>
    /// Create a new dedicated server with the given name
    /// </summary>
    public async void CreateServer(string serverName)
    {
        Debug.Log($"Creating server: {serverName}");
        
        // Clean up any existing server first
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.Log("Shutting down existing network session first");
            NetworkManager.Singleton.Shutdown();
            StartCoroutine(CreateServerAfterShutdown(serverName, useUnityServices && isServicesInitialized));
            return;
        }
        
        if (useUnityServices && isServicesInitialized)
        {
            await CreateServerWithUnityServices(serverName);
        }
        else
        {
            // Fallback to original implementation for local networks
            CreateServerInternal(serverName);
        }
    }
    
    private IEnumerator CreateServerAfterShutdown(string serverName, bool useUnityServicesForThis)
    {
        // Wait for the previous instance to fully shut down
        yield return new WaitForSeconds(1.0f);
        
        if (useUnityServicesForThis)
        {
            // Start the async method but don't await it in the coroutine
            var _ = CreateServerWithUnityServices(serverName);
        }
        else
        {
            CreateServerInternal(serverName);
        }
    }
    
    private async Task CreateServerWithUnityServices(string serverName)
    {
        Debug.Log("Creating server using Unity Gaming Services");
        
        // Verify that NetworkManager exists and isn't already running
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is null! Cannot create server.");
            OnCreateServerFailed?.Invoke("NetworkManager not found");
            return;
        }
        
        // If NetworkManager is already listening, shut it down and wait for it to fully shut down
        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("NetworkManager is still listening! Shutting down and waiting...");
            
            // Unsubscribe and resubscribe to events to ensure clean state
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            
            // Shut down the NetworkManager
            NetworkManager.Singleton.Shutdown();
            
            // Wait for NetworkManager to completely shut down with a generous timeout
            int attempts = 0;
            int maxAttempts = 10;
            while (NetworkManager.Singleton.IsListening && attempts < maxAttempts)
            {
                Debug.Log($"Waiting for NetworkManager to shut down... Attempt {attempts + 1}/{maxAttempts}");
                await Task.Delay(500); // Longer delay to ensure shutdown completes
                attempts++;
            }
            
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogError("NetworkManager failed to shut down after multiple attempts!");
                OnCreateServerFailed?.Invoke("NetworkManager shutdown failed");
                return;
            }
            
            // Resubscribe to events
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            
            Debug.Log("NetworkManager successfully shut down, proceeding with server creation");
        }
        
        bool success = await CreateRelayServerAsync(serverName);
        
        if (success)
        {
            // Verify again that NetworkManager isn't running before starting host
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogError("NetworkManager is still listening even after shutdown! Cannot start host.");
                OnCreateServerFailed?.Invoke("NetworkManager shutdown failed");
                return;
            }
            
            // Create the server info before starting host
            currentServerInfo = new ServerInfo(
                serverName, 
                "UGS Relay", 
                7777, 
                1, 
                maxPlayersPerServer, 
                false
            );
            
            // Start the server with Relay
            bool serverStarted = NetworkManager.Singleton.StartHost();
            
            if (serverStarted)
            {
                Debug.Log("Relay host started successfully");
                OnCreateServerSuccess?.Invoke();
            }
            else
            {
                Debug.LogError("Failed to start host");
                currentServerInfo = null; // Clear the server info since it wasn't started
                OnCreateServerFailed?.Invoke("Failed to start host");
            }
        }
        else
        {
            Debug.LogError("Failed to create relay server");
            OnCreateServerFailed?.Invoke("Failed to create relay server");
        }
    }
    
    private void CreateServerInternal(string serverName)
    {
        try
        {
            // Check if NetworkManager is already running - if so, shut it down first
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("NetworkManager is already running, shutting down first...");
                NetworkManager.Singleton.Shutdown();
                // Small delay to ensure shutdown completes
                System.Threading.Thread.Sleep(100);
            }
            
            // Get or add the NetworkTransport component
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("UnityTransport component not found on NetworkManager!");
                OnCreateServerFailed?.Invoke("Missing required network components");
                return;
            }
            
            // Reset transport to ensure clean state
            transport.Shutdown();
            
            // Set up local transport if not using relay
            if (string.IsNullOrEmpty(relayJoinCode))
            {
                Debug.Log("Setting up direct connection transport (non-relay)");
                // Set up transport for direct connection
                transport.ConnectionData.Address = "0.0.0.0"; // Listen on all interfaces
                transport.ConnectionData.Port = (ushort)discoveryPort;
                
                Debug.Log($"Transport configured for direct connections on port {discoveryPort}");
            }
            else
            {
                Debug.Log("Using previously configured relay transport");
            }
            
            // Verify and ensure NetworkConfig is properly set
            if (NetworkManager.Singleton.NetworkConfig == null)
            {
                Debug.LogError("NetworkConfig is null! Creating a new one.");
                // You might need to create a new NetworkConfig here, but this would be unusual
                // as NetworkManager should always have a NetworkConfig
                NetworkManager.Singleton.NetworkConfig = new NetworkConfig();
            }
            
            // Ensure transport is assigned to NetworkConfig
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport == null || 
                NetworkManager.Singleton.NetworkConfig.NetworkTransport != transport)
            {
                Debug.LogWarning("Setting NetworkTransport in NetworkConfig");
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            }
            
            // Configure NetworkConfig
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            
            // Register connection approval handler
            NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;
            
            // Register for network events
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            
            // Create a server info object
            currentServerInfo = new ServerInfo(
                serverName,
                useUnityServices ? "Relay" : GetLocalIPv4(),
                (ushort)discoveryPort,
                1, // Start with 1 player (the host)
                maxPlayersPerServer,
                false // Not in game yet
            );
            
            // If using Unity Services, the lobbyId will be set later when the lobby is created
            
            Debug.Log($"Starting server: {serverName} - Max Players: {maxPlayersPerServer}");
            
            // Start the server (or host, which is both server and client)
            bool startSuccess = NetworkManager.Singleton.StartHost();
            
            if (startSuccess)
            {
                Debug.Log($"Server started successfully: {serverName}");
                
                // Start broadcasting server info if not using Unity Services
                if (!useUnityServices)
                {
                    StartServerBroadcast();
                }
                
                // Notify listeners of success
                OnCreateServerSuccess?.Invoke();
            }
            else
            {
                Debug.LogError("Failed to start NetworkManager host mode!");
                
                // Provide more detailed diagnostic info
                Debug.LogError($"NetworkManager state: IsHost={NetworkManager.Singleton.IsHost}, IsServer={NetworkManager.Singleton.IsServer}, IsClient={NetworkManager.Singleton.IsClient}, IsListening={NetworkManager.Singleton.IsListening}");
                
                // Try to get transport config info
                try {
                    Debug.LogError($"Transport config: Address={transport.ConnectionData.Address}, Port={transport.ConnectionData.Port}");
                }
                catch (Exception ex) {
                    Debug.LogError($"Could not access transport config: {ex.Message}");
                }
                
                // Notify listeners of failure
                OnCreateServerFailed?.Invoke("Failed to start server");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating server: {ex.Message}");
            Debug.LogException(ex);
            OnCreateServerFailed?.Invoke($"Error: {ex.Message}");
        }
    }
    
    // Helper method to get the actual local IP address
    private string GetLocalIPv4()
    {
        // Get the local IP address of the first active network interface
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        
        // Fallback to loopback if no IP found
        return "127.0.0.1";
    }
    
    /// <summary>
    /// Register this server with the matchmaking system
    /// </summary>
    private void RegisterServer()
    {
        try
        {
            // Check if currentServerInfo is valid
            if (currentServerInfo == null)
            {
                Debug.LogError("********** ERROR: currentServerInfo is null when registering server **********");
                return;
            }

            Debug.Log($"********** REGISTERING SERVER FOR DISCOVERY **********\n" +
                      $"Server Name: {currentServerInfo.serverName}\n" +
                      $"IP: {currentServerInfo.ipAddress}\n" +
                      $"Port: {currentServerInfo.port}");
            
            // Add our server to the list of available servers
            if (!availableServers.Contains(currentServerInfo))
            {
                availableServers.Add(currentServerInfo);
                Debug.Log("********** SERVER ADDED TO AVAILABLE SERVERS LIST **********");
            }
            
            // Store server info in PlayerPrefs for local discovery
            PlayerPrefs.SetString("LocalServerName", currentServerInfo.serverName);
            PlayerPrefs.Save();
            
            PlayerPrefs.SetString("LocalServerIP", currentServerInfo.ipAddress);
            PlayerPrefs.Save();
            
            PlayerPrefs.SetInt("LocalServerPort", currentServerInfo.port);
            PlayerPrefs.Save();
            
            PlayerPrefs.SetInt("LocalServerPlayers", currentServerInfo.currentPlayers);
            PlayerPrefs.Save();
            
            PlayerPrefs.SetInt("LocalServerMaxPlayers", currentServerInfo.maxPlayers);
            PlayerPrefs.Save();
            
            PlayerPrefs.SetInt("LocalServerInGame", currentServerInfo.inGame ? 1 : 0);
            PlayerPrefs.Save();
            
            PlayerPrefs.SetString("LocalServerTimestamp", DateTime.Now.ToString());
            PlayerPrefs.Save();
            
            // For cross-process discovery, write to a file in a known location
            string filePath = Path.Combine(Application.persistentDataPath, "serverInfo.json");
            string jsonData = JsonUtility.ToJson(currentServerInfo);
            File.WriteAllText(filePath, jsonData);
            
            Debug.Log($"********** SERVER INFO WRITTEN TO FILE: {filePath} **********");
            
            // Start broadcasting server presence on the network (only for local discovery)
            if (currentServerInfo.ipAddress != "UGS Relay")
            {
                StartServerBroadcast();
            }
            
            // Debug output the PlayerPrefs values after setting them
            Debug.Log($"********** PLAYERPREFS VERIFICATION **********\n" +
                      $"Name: {PlayerPrefs.GetString("LocalServerName")}\n" +
                      $"IP: {PlayerPrefs.GetString("LocalServerIP")}\n" +
                      $"Port: {PlayerPrefs.GetInt("LocalServerPort")}");
            
            // Notify success (avoid double notification)
            if (OnCreateServerSuccess != null && !IsHost)
            {
                OnCreateServerSuccess?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"********** ERROR REGISTERING SERVER: {ex.Message} **********");
            Debug.LogException(ex); // Log the full exception for better debugging
            OnCreateServerFailed?.Invoke("Error registering server: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Start broadcasting this server's presence on the local network
    /// </summary>
    private void StartServerBroadcast()
    {
        Debug.Log("********** STARTING SERVER BROADCAST **********");
        
        if (broadcastClient != null)
        {
            Debug.Log("********** STOPPING PREVIOUS BROADCAST BEFORE STARTING NEW ONE **********");
            StopServerBroadcast();
        }
        
        try
        {
            // Create a UDP client for broadcasting
            broadcastClient = new UdpClient();
            
            // Configure socket options
            broadcastClient.EnableBroadcast = true;
            broadcastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            
            // Try to improve connectivity on some systems
            try
            {
                broadcastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            }
            catch (SocketException ex)
            {
                Debug.LogWarning($"MatchmakingManager: Non-critical error setting broadcast socket option: {ex.Message}");
            }
            
            // Start a coroutine to broadcast regularly
            if (gameObject.activeInHierarchy)
            {
                Coroutine bc = SafeStartCoroutine(BroadcastServerInfoCoroutine(), "BroadcastServerInfoCoroutine");
                if (bc != null)
                {
                    Debug.Log("********** SERVER BROADCAST COROUTINE STARTED SUCCESSFULLY **********");
                }
                else
                {
                    Debug.LogError("********** SERVER BROADCAST COROUTINE FAILED TO START **********");
                }
            }
            else
            {
                Debug.LogError("********** CANNOT START BROADCAST - GAMEOBJECT IS INACTIVE **********");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"********** ERROR STARTING SERVER BROADCAST: {ex.Message} **********");
        }
    }
    
    /// <summary>
    /// Stop broadcasting this server's presence
    /// </summary>
    private void StopServerBroadcast()
    {
        if (broadcastClient != null)
        {
            broadcastClient.Close();
            broadcastClient = null;
            StopCoroutine(BroadcastServerInfoCoroutine());
            Debug.Log("MatchmakingManager: Stopped server broadcast");
        }
    }
    
    /// <summary>
    /// Coroutine to broadcast server info periodically
    /// </summary>
    private IEnumerator BroadcastServerInfoCoroutine()
    {
        Debug.Log("********** BROADCAST COROUTINE STARTED **********");
        int broadcastCount = 0;
        
        while (true)
        {
            try
            {
                broadcastCount++;
                
                // Prepare server info to broadcast
                string jsonData = JsonUtility.ToJson(currentServerInfo);
                byte[] data = Encoding.UTF8.GetBytes(jsonData);
                
                // Try different broadcast methods
                TryBroadcastToNetwork(data);
                
                Debug.Log($"********** BROADCAST #{broadcastCount} SENT **********\n" +
                          $"Server: {currentServerInfo.serverName}\n" +
                          $"IP: {currentServerInfo.ipAddress}\n" +
                          $"Port: {currentServerInfo.port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"********** ERROR BROADCASTING SERVER INFO: {ex.Message} **********");
            }
            
            // Wait before broadcasting again
            yield return new WaitForSeconds(broadcastInterval);
        }
    }
    
    /// <summary>
    /// Try different methods to broadcast to the network
    /// </summary>
    private void TryBroadcastToNetwork(byte[] data)
    {
        Debug.Log("********** ATTEMPTING UDP BROADCAST **********");
        
        // This will track if any broadcast method succeeded
        bool anyBroadcastSucceeded = false;
        
        // METHOD 1: Direct broadcast to localhost (always works for testing on the same machine)
        try 
        {
            if (broadcastClient != null)
            {
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), discoveryPort);
                int bytesSent = broadcastClient.Send(data, data.Length, localEndPoint);
                Debug.Log($"********** LOCALHOST BROADCAST SENT: {bytesSent} BYTES TO 127.0.0.1:{discoveryPort} **********");
                anyBroadcastSucceeded = true;
            }
        }
        catch (Exception ex)
        {
            // Just log at warning level since this is just one method
            Debug.LogWarning($"Localhost broadcast failed: {ex.Message}");
        }
        
        // METHOD 2: Try direct broadcast to local IP (works when client and server are on same machine)
        try
        {
            if (broadcastClient != null)
            {
                string localIP = GetLocalIPv4();
                if (!string.IsNullOrEmpty(localIP))
                {
                    IPEndPoint directEndPoint = new IPEndPoint(IPAddress.Parse(localIP), discoveryPort);
                    int bytesSent = broadcastClient.Send(data, data.Length, directEndPoint);
                    Debug.Log($"********** DIRECT BROADCAST SENT: {bytesSent} BYTES TO {localIP}:{discoveryPort} **********");
                    anyBroadcastSucceeded = true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Direct IP broadcast failed: {ex.Message}");
        }
        
        // METHOD 3: Traditional broadcast addresses (works on many networks but may fail on some)
        try
        {
            if (broadcastClient != null)
            {
                // Try to broadcast to all-network broadcast address
                try 
                {
                    IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
                    int bytesSent = broadcastClient.Send(data, data.Length, broadcastEndPoint);
                    Debug.Log($"********** ALL-NETWORK BROADCAST SENT: {bytesSent} BYTES TO 255.255.255.255:{discoveryPort} **********");
                    anyBroadcastSucceeded = true;
                }
                catch (Exception ex)
                {
                    // This often fails with "No route to host" on modern networks, so just log as info
                    Debug.Log($"All-network broadcast failed (common on modern networks): {ex.Message}");
                }
                
                // Try subnet-directed broadcast
                try
                {
                    string localIP = GetLocalIPv4();
                    if (!string.IsNullOrEmpty(localIP) && localIP != "127.0.0.1")
                    {
                        string[] parts = localIP.Split('.');
                        if (parts.Length == 4)
                        {
                            // Create subnet broadcast address (e.g., 192.168.1.255)
                            string broadcastIP = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
                            IPEndPoint subnetEndpoint = new IPEndPoint(IPAddress.Parse(broadcastIP), discoveryPort);
                            int bytesSent = broadcastClient.Send(data, data.Length, subnetEndpoint);
                            Debug.Log($"********** SUBNET BROADCAST SENT: {bytesSent} BYTES TO {broadcastIP}:{discoveryPort} **********");
                            anyBroadcastSucceeded = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Subnet broadcast failed: {ex.Message}");
                }
            }
            else
            {
                Debug.LogError("********** BROADCAST CLIENT IS NULL! CANNOT SEND BROADCAST **********");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Traditional broadcast methods failed: {ex.Message}");
        }
        
        // METHOD 4: Targeted broadcasts to common local addresses
        // This is more reliable on modern networks
        try 
        {
            // Send to common local IPs directly
            string localIP = GetLocalIPv4();
            if (!string.IsNullOrEmpty(localIP) && localIP != "127.0.0.1" && broadcastClient != null)
            {
                string[] parts = localIP.Split('.');
                if (parts.Length == 4)
                {
                    // Send to a few common addresses in the subnet
                    string[] commonHosts = { "1", "100", "101", "102", "150", "200", "254" };
                    
                    foreach (string host in commonHosts)
                    {
                        try 
                        {
                            string targetIP = $"{parts[0]}.{parts[1]}.{parts[2]}.{host}";
                            IPEndPoint targetEndpoint = new IPEndPoint(IPAddress.Parse(targetIP), discoveryPort);
                            int bytesSent = broadcastClient.Send(data, data.Length, targetEndpoint);
                            Debug.Log($"********** TARGET BROADCAST SENT: {bytesSent} BYTES TO {targetIP}:{discoveryPort} **********");
                            anyBroadcastSucceeded = true;
                        }
                        catch (Exception ex)
                        {
                            // No need to log every failure in targeted broadcasting
                            // This is expected for IPs that don't exist
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Targeted broadcast failed: {ex.Message}");
        }
        
        if (anyBroadcastSucceeded)
        {
            Debug.Log("********** AT LEAST ONE BROADCAST METHOD SUCCEEDED **********");
        }
        else
        {
            Debug.LogError("********** ALL BROADCAST METHODS FAILED! **********");
        }
        
        // Log networking information - this helps with troubleshooting
        string externalIP = GetExternalIP();
        Debug.Log("==== SERVER NETWORK INFO ====");
        Debug.Log($"Local IP: {GetLocalIPv4()}");
        Debug.Log($"Possible Public IP: {(string.IsNullOrEmpty(externalIP) ? "Unknown" : externalIP)}");
        Debug.Log($"Discovery Port: {discoveryPort}, Game Port: {currentServerInfo.port}");
    }
    
    // Try to get the external IP using a simple HTTP request
    private string GetExternalIP()
    {
        try
        {
            using (WebClient client = new WebClient())
            {
                // Using a simple, reliable IP echo service
                string externalIP = client.DownloadString("https://api.ipify.org");
                return externalIP.Trim();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to get external IP: {ex.Message}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Updates the player count on the server
    /// </summary>
    public void UpdateServerPlayerCount(int count)
    {
        if (currentServerInfo != null)
        {
            currentServerInfo.currentPlayers = count;
            
            // In a real implementation, update the server info on the matchmaking service
        }
    }
    
    /// <summary>
    /// Mark the server as in-game (match started)
    /// </summary>
    public void SetServerInGame(bool inGame)
    {
        if (currentServerInfo != null)
        {
            currentServerInfo.inGame = inGame;
            
            // In a real implementation, update the server info on the matchmaking service
        }
    }
    
    /// <summary>
    /// Cleans up any old server registration files
    /// </summary>
    private void CleanupOldServerRegistrations()
    {
        try
        {
            // Clear PlayerPrefs entries
            if (PlayerPrefs.HasKey("LocalServerName"))
            {
                PlayerPrefs.DeleteKey("LocalServerName");
                PlayerPrefs.DeleteKey("LocalServerIP");
                PlayerPrefs.DeleteKey("LocalServerPort");
                PlayerPrefs.DeleteKey("LocalServerPlayers");
                PlayerPrefs.DeleteKey("LocalServerMaxPlayers");
                PlayerPrefs.DeleteKey("LocalServerInGame");
                PlayerPrefs.DeleteKey("LocalServerTimestamp");
                PlayerPrefs.Save();
                Debug.Log("MatchmakingManager: Cleared old PlayerPrefs server registrations");
            }
            
            // Delete old server info file if it exists
            string filePath = Path.Combine(Application.persistentDataPath, "serverInfo.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Debug.Log($"MatchmakingManager: Deleted old server info file at {filePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MatchmakingManager: Error cleaning up old server registrations: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Server Discovery and Joining
    
    /// <summary>
    /// Start the server discovery process
    /// </summary>
    public void StartServerDiscovery()
    {
        Debug.Log("********** STARTING SERVER DISCOVERY **********");
        
        if (isRefreshingServers)
        {
            Debug.Log("MatchmakingManager: Already discovering servers, restarting discovery");
            StopServerDiscovery();
        }
        
        // Reset server list
        discoveredServers.Clear();
        
        // Start discovery in the background
        isRefreshingServers = true;
        
        // Debug current status before starting discovery
        Debug.Log($"********** SERVER DISCOVERY STATUS **********\n" +
                  $"Is Client: {IsClient}\n" +
                  $"Is Server: {IsServer}\n" +
                  $"Is Discovering: {isRefreshingServers}\n" +
                  $"Discover Thread Active: {(serverDiscoveryCoroutine != null)}\n" +
                  $"Servers Count: {discoveredServers.Count}");
        
        // Start discovery in a separate thread to avoid blocking the main thread
        serverDiscoveryCoroutine = SafeStartCoroutine(RefreshServersCoroutine(), "RefreshServersCoroutine");
        
        Debug.Log("********** SERVER DISCOVERY THREAD STARTED **********");
    }
    
    /// <summary>
    /// Display network information for debugging
    /// </summary>
    private void DisplayNetworkInfo()
    {
        try
        {
            string localIP = GetLocalIPv4();
            string subnetMask = "255.255.255.0"; // Default assumption
            
            // Try to determine subnet mask
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in interfaces)
            {
                if (adapter.OperationalStatus == OperationalStatus.Up)
                {
                    IPInterfaceProperties ipProps = adapter.GetIPProperties();
                    foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && 
                            addr.Address.ToString() == localIP)
                        {
                            subnetMask = addr.IPv4Mask.ToString();
                            break;
                        }
                    }
                }
            }
            
            // Get gateway information
            string gateway = GetDefaultGatewayIP();
            
            Debug.Log("===== CLIENT NETWORK INFO =====");
            Debug.Log($"Local IP: {localIP}");
            Debug.Log($"Subnet Mask: {subnetMask}");
            Debug.Log($"Gateway: {gateway}");
            Debug.Log($"Listening on port: {discoveryPort}");
            Debug.Log("===============================");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MatchmakingManager: Could not display all network info: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Start listening for UDP broadcast messages from servers
    /// </summary>
    private void StartUdpDiscovery()
    {
        Debug.Log("********** STARTING UDP DISCOVERY **********");
        
        // Stop any existing discovery
        StopUdpDiscovery();
        
        try
        {
            // Create cancellation token for async operations
            cancellationTokenSource = new CancellationTokenSource();
            
            // Track if we successfully initialized a discovery client
            bool clientInitialized = false;
            
            // METHOD 1: Try standard binding method first
            try
            {
                discoveryClient = new UdpClient();
                
                // Set socket options for maximum compatibility
                discoveryClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                discoveryClient.EnableBroadcast = true;
                discoveryClient.Client.ReceiveBufferSize = 65536;
                
                // Bind to the discovery port
                discoveryClient.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
                
                Debug.Log($"********** UDP CLIENT SUCCESSFULLY BOUND TO PORT {discoveryPort} (METHOD 1) **********");
                clientInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"********** PRIMARY BINDING FAILED: {ex.Message}. TRYING FALLBACK METHOD... **********");
                
                // Clean up the failed client
                if (discoveryClient != null)
                {
                    discoveryClient.Close();
                    discoveryClient = null;
                }
                
                // METHOD 2: Fallback to direct constructor binding
                try
                {
                    discoveryClient = new UdpClient(discoveryPort);
                    discoveryClient.EnableBroadcast = true;
                    Debug.Log($"********** UDP CLIENT SUCCESSFULLY BOUND TO PORT {discoveryPort} (METHOD 2) **********");
                    clientInitialized = true;
                }
                catch (Exception ex2)
                {
                    Debug.LogError($"********** FALLBACK BINDING ALSO FAILED: {ex2.Message} **********");
                    
                    // METHOD 3: Last resort - try with a different port
                    try
                    {
                        // Try a different port as last resort (for testing only)
                        int fallbackPort = discoveryPort + 1;
                        discoveryClient = new UdpClient(fallbackPort);
                        discoveryClient.EnableBroadcast = true;
                        Debug.LogWarning($"********** USING FALLBACK PORT {fallbackPort} - THIS IS FOR TESTING ONLY **********");
                        Debug.LogWarning($"********** SERVER AND CLIENT MUST BOTH USE PORT {fallbackPort} TO WORK **********");
                        clientInitialized = true;
                    }
                    catch (Exception ex3)
                    {
                        Debug.LogError($"********** ALL UDP CLIENT CREATION METHODS FAILED: {ex3.Message} **********");
                    }
                }
            }
            
            // Only continue if we successfully initialized a client
            if (clientInitialized && discoveryClient != null)
            {
                // Start listening for broadcasts asynchronously
                isDiscoveryRunning = true;
                Debug.Log("********** UDP DISCOVERY CLIENT CREATED, STARTING LISTENER **********");
                
                // Start the async listener
                _ = RunListenerWithErrorHandling(cancellationTokenSource.Token);
                
                // Send discovery requests to actively find servers
                SendClientDiscoveryRequests();
                
                Debug.Log("********** UDP DISCOVERY STARTED SUCCESSFULLY **********");
            }
            else
            {
                Debug.LogError("********** FAILED TO CREATE UDP DISCOVERY CLIENT **********");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"********** UNHANDLED ERROR STARTING UDP DISCOVERY: {ex.Message} **********\n{ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Send discovery requests to help with NAT traversal
    /// </summary>
    private void SendClientDiscoveryRequests()
    {
        Debug.Log("********** SENDING CLIENT DISCOVERY REQUESTS **********");
        
        try
        {
            // Create a temporary UDP client for sending requests
            using (UdpClient requestClient = new UdpClient())
            {
                // Enable broadcast
                requestClient.EnableBroadcast = true;
                
                // Prepare discovery request message
                byte[] requestData = Encoding.UTF8.GetBytes("DISCOVER_GAME_SERVER");
                bool anySendSucceeded = false;
                
                // METHOD 1: Try sending to localhost (always works if server is on same machine)
                try
                {
                    IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), discoveryPort);
                    int bytesSent = requestClient.Send(requestData, requestData.Length, localEndPoint);
                    Debug.Log($"********** DISCOVERY REQUEST SENT TO LOCALHOST: {bytesSent} bytes **********");
                    anySendSucceeded = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to send discovery request to localhost: {ex.Message}");
                }
                
                // METHOD 2: Try broadcast address - may or may not work depending on network
                try
                {
                    IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
                    int bytesSent = requestClient.Send(requestData, requestData.Length, broadcastEndPoint);
                    Debug.Log($"********** DISCOVERY REQUEST BROADCAST SENT: {bytesSent} bytes **********");
                    anySendSucceeded = true;
                }
                catch (Exception ex)
                {
                    Debug.Log($"Failed to send discovery broadcast (common on modern networks): {ex.Message}");
                }
                
                // METHOD 3: Try subnet broadcast - more reliable on modern networks
                string localIP = GetLocalIPv4();
                if (!string.IsNullOrEmpty(localIP) && localIP != "127.0.0.1")
                {
                    string[] parts = localIP.Split('.');
                    if (parts.Length == 4)
                    {
                        // Try subnet broadcast (e.g., 192.168.1.255)
                        try
                        {
                            string broadcastIP = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
                            IPEndPoint subnetEndPoint = new IPEndPoint(IPAddress.Parse(broadcastIP), discoveryPort);
                            int bytesSent = requestClient.Send(requestData, requestData.Length, subnetEndPoint);
                            Debug.Log($"********** DISCOVERY REQUEST SENT TO SUBNET: {bytesSent} bytes to {broadcastIP} **********");
                            anySendSucceeded = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"Failed to send subnet discovery request: {ex.Message}");
                        }
                        
                        // METHOD 4: Try sending to specific common IPs in the subnet
                        string[] commonIPs = { "1", "100", "101", "102", "150", "200", "254" };
                        foreach (string host in commonIPs)
                        {
                            try
                            {
                                string targetIP = $"{parts[0]}.{parts[1]}.{parts[2]}.{host}";
                                // Skip our own IP to avoid confusion
                                if (targetIP == localIP) continue;
                                
                                IPEndPoint targetEndPoint = new IPEndPoint(IPAddress.Parse(targetIP), discoveryPort);
                                int bytesSent = requestClient.Send(requestData, requestData.Length, targetEndPoint);
                                Debug.Log($"********** DISCOVERY REQUEST SENT TO: {targetIP} ({bytesSent} bytes) **********");
                                anySendSucceeded = true;
                            }
                            catch
                            {
                                // Ignore individual failures - expected for non-existent IPs
                            }
                        }
                    }
                }
                
                // Also try sending to self (important for testing)
                try
                {
                    IPEndPoint selfEndPoint = new IPEndPoint(IPAddress.Parse(localIP), discoveryPort);
                    int bytesSent = requestClient.Send(requestData, requestData.Length, selfEndPoint);
                    Debug.Log($"********** DISCOVERY REQUEST SENT TO SELF: {localIP} ({bytesSent} bytes) **********");
                    anySendSucceeded = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to send discovery request to self: {ex.Message}");
                }
                
                if (anySendSucceeded)
                {
                    Debug.Log("********** DISCOVERY REQUESTS SENT SUCCESSFULLY **********");
                }
                else
                {
                    Debug.LogError("********** ALL DISCOVERY REQUEST METHODS FAILED! **********");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"********** ERROR SENDING DISCOVERY REQUESTS: {ex.Message} **********");
        }
    }
    
    /// <summary>
    /// Stop discovering servers
    /// </summary>
    public void StopServerDiscovery()
    {
        Debug.Log("MatchmakingManager: Stopping server discovery");
        
        // Stop the UDP discovery
        StopUdpDiscovery();
        
        // Stop the periodic server refresh
        if (serverDiscoveryCoroutine != null)
        {
            StopCoroutine(serverDiscoveryCoroutine);
            serverDiscoveryCoroutine = null;
        }
        
        Debug.Log("MatchmakingManager: Server discovery stopped");
    }
    
    /// <summary>
    /// Listen for broadcasts from servers and respond to discovery requests
    /// </summary>
    private async Task ListenForBroadcastsAsync(CancellationToken cancellationToken)
    {
        Debug.Log("********** STARTING BROADCAST LISTENER **********");
        
        int packetsReceived = 0;
        DateTime lastPacketTime = DateTime.Now;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Receive broadcast message with cancellation support
                UdpReceiveResult result;
                try 
                {
                    // Make sure discovery client still exists
                    if (discoveryClient == null)
                    {
                        Debug.LogError("********** DISCOVERY CLIENT IS NULL DURING LISTENING **********");
                        await Task.Delay(1000, cancellationToken); // Wait a bit before trying again
                        continue;
                    }
                
                    // Every 5 seconds, log a heartbeat if no packets have been received
                    TimeSpan timeSinceLastPacket = DateTime.Now - lastPacketTime;
                    if (timeSinceLastPacket.TotalSeconds > 5)
                    {
                        Debug.Log($"********** BROADCAST LISTENER HEARTBEAT **********\n" +
                                  $"Total packets received: {packetsReceived}\n" +
                                  $"Time since last packet: {timeSinceLastPacket.TotalSeconds:F1} seconds\n" +
                                  $"Is UDP client active: {discoveryClient != null}");
                        lastPacketTime = DateTime.Now; // Reset timer
                    }
                    
                    // We'll handle cancellation manually here
                    var receiveTask = discoveryClient.ReceiveAsync();
                    
                    // Create a task that completes when cancellation is requested
                    var cancellationTcs = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => cancellationTcs.TrySetResult(true)))
                    {
                        // Wait for either the receive to complete or cancellation
                        Task completedTask = await Task.WhenAny(receiveTask, cancellationTcs.Task);
                        
                        // If cancellation was requested, throw the exception
                        if (completedTask == cancellationTcs.Task)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }
                        
                        // Otherwise, get the result
                        result = await receiveTask;
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    Debug.LogWarning($"MatchmakingManager: UDP client was disposed during receive: {ex.Message}");
                    // The UDP client was disposed - exit the loop
                    break;
                }
                catch (NullReferenceException ex)
                {
                    Debug.LogError($"MatchmakingManager: Null reference during network receive: {ex.Message}");
                    // Something is null, wait a bit and try again
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }
                catch (OperationCanceledException)
                {
                    // Cancellation was requested, exit the loop
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"MatchmakingManager: Error receiving broadcast: {ex.Message}");
                    // Wait a bit before trying again to avoid tight loop on error
                    await Task.Delay(500, cancellationToken);
                    continue;
                }
                
                // Null check the result buffer
                if (result.Buffer == null)
                {
                    Debug.LogWarning("MatchmakingManager: Received null buffer in broadcast");
                    continue;
                }
                
                try
                {
                    string message = Encoding.UTF8.GetString(result.Buffer);
                    
                    // Check if this is a discovery request (clients sending probe packets)
                    if (message == "DISCOVER_GAME_SERVER" && (IsServer || IsHost) && currentServerInfo != null)
                    {
                        // This is a discovery request and we're a server, so respond directly to the requester
                        try
                        {
                            // Create a temporary client to respond
                            using (UdpClient responseClient = new UdpClient())
                            {
                                // Prepare server info to send back
                                string jsonData = JsonUtility.ToJson(currentServerInfo);
                                byte[] responseData = Encoding.UTF8.GetBytes(jsonData);
                                
                                // Send directly back to the requestor's endpoint
                                responseClient.Send(responseData, responseData.Length, result.RemoteEndPoint);
                                
                                Debug.Log($"MatchmakingManager: Responded to discovery request from {result.RemoteEndPoint}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"MatchmakingManager: Error responding to discovery request: {ex.Message}");
                        }
                        
                        continue; // Continue to next iteration
                    }
                    
                    // Process received data 
                    packetsReceived++;
                    lastPacketTime = DateTime.Now;
                    
                    Debug.Log($"********** RECEIVED UDP PACKET #{packetsReceived} **********\n" +
                              $"From: {result.RemoteEndPoint}\n" +
                              $"Size: {result.Buffer.Length} bytes\n" +
                              $"Message starts with: {message.Substring(0, Math.Min(20, message.Length))}...");
                    
                    // Process server info broadcast
                    try
                    {
                        // Try to parse the message as server info
                        ServerInfo serverInfo = JsonUtility.FromJson<ServerInfo>(message);
                        if (serverInfo != null && !string.IsNullOrEmpty(serverInfo.serverName))
                        {
                            // Create a unique key for this server
                            string serverKey = $"{serverInfo.ipAddress}:{serverInfo.port}";
                            
                            // Add or update in our discovered servers dictionary with timestamp
                            lock (discoveredServers) // Use lock to prevent threading issues
                            {
                                discoveredServers[serverKey] = Time.time;
                            }
                            
                            Debug.Log($"MatchmakingManager: Discovered server: {serverInfo.serverName} at {serverInfo.ipAddress}:{serverInfo.port}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Not a valid server info JSON, that's okay (might be other network traffic)
                        // Log detailed info only in debug builds
                        #if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogWarning($"MatchmakingManager: Error parsing server info: {ex.Message}");
                        #endif
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"MatchmakingManager: Error processing received data: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // This is expected when cancellation is requested
            Debug.Log("MatchmakingManager: Broadcast listener cancelled");
            throw; // Re-throw to signal cancellation to the caller
        }
        catch (Exception ex)
        {
            Debug.LogError($"MatchmakingManager: Error in broadcast listener: {ex.Message}");
            throw; // Re-throw to signal error to the caller
        }
        finally
        {
            Debug.Log("MatchmakingManager: Broadcast listener stopped");
        }
    }
    
    private IEnumerator RefreshServersCoroutine()
    {
        isRefreshingServers = true;
        
        while (true)
        {
            // Refresh the server list - use fire and forget pattern
            var _ = RefreshServerList();
            
            // Wait before the next refresh
            yield return new WaitForSeconds(serverRefreshRate);
        }
    }
    
    private async Task RefreshServerList()
    {
        Debug.Log("********** REFRESHING SERVER LIST **********");
        
        // Clear the current list
        Debug.Log($"********** SERVER LIST BEFORE REFRESH: {availableServers.Count} SERVERS **********");
        availableServers.Clear();
        
        // Use Unity Gaming Services if enabled
        if (useUnityServices && isServicesInitialized)
        {
            // Check if we're already in the process of refreshing with Unity Services
            if (isRefreshingWithUnityServices)
            {
                Debug.LogWarning("Already refreshing with Unity Services, skipping duplicate refresh");
                return;
            }
            
            try
            {
                isRefreshingWithUnityServices = true;
                Debug.Log("********** QUERYING UNITY GAMING SERVICES LOBBIES **********");
                List<ServerInfo> ugsServers = await QueryLobbiesAsync();
                availableServers.AddRange(ugsServers);
                Debug.Log($"********** FOUND {ugsServers.Count} LOBBIES VIA UNITY GAMING SERVICES **********");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error querying UGS lobbies: {ex.Message}");
            }
            finally
            {
                isRefreshingWithUnityServices = false;
            }
        }
        
        // Add any discovered servers from UDP broadcasts (if UGS is disabled or in debug mode)
        if (!useUnityServices || debugMode)
        {
            bool foundLocalServers = AddDiscoveredUdpServers();
            Debug.Log($"********** LOCAL UDP DISCOVERY: {(foundLocalServers ? "FOUND SERVERS" : "NO SERVERS FOUND")} **********");
        }
        
        // If no servers found and in the Unity Editor, add a test server to verify UI is working
        #if UNITY_EDITOR
        if (availableServers.Count == 0 && showTestServersInEditor)
        {
            Debug.Log("********** ADDING TEST SERVER IN EDITOR FOR DEBUGGING **********");
            ServerInfo testServer = new ServerInfo(
                "TEST SERVER (Editor Only)",
                "127.0.0.1",
                7777,
                1,
                maxPlayersPerServer,
                false
            );
            availableServers.Add(testServer);
            
            Debug.Log($"********** ADDED TEST SERVER TO VERIFY UI **********\n" +
                      $"Name: {testServer.serverName}\n" +
                      $"IP: {testServer.ipAddress}\n" +
                      $"Port: {testServer.port}");
        }
        #endif
        
        // Log the results
        Debug.Log($"********** SERVER LIST AFTER REFRESH: {availableServers.Count} SERVERS **********");
        if (availableServers.Count > 0)
        {
            foreach (var server in availableServers)
            {
                Debug.Log($"********** SERVER AVAILABLE: {server.serverName} **********\n" +
                          $"IP: {server.ipAddress}\n" +
                          $"Port: {server.port}\n" +
                          $"Players: {server.currentPlayers}/{server.maxPlayers}\n" +
                          $"In Game: {server.inGame}\n" +
                          $"Is UGS: {(!string.IsNullOrEmpty(server.lobbyId) ? "YES" : "NO")}");
            }
        }
        
        // Notify listeners about the updated server list
        Debug.Log($"********** SENDING SERVER LIST UPDATE NOTIFICATION WITH {availableServers.Count} SERVERS **********");
        Debug.Log($"********** HAS SUBSCRIBERS: {(OnServerListUpdated != null ? "YES" : "NO")} **********");
        OnServerListUpdated?.Invoke(availableServers);
    }
    
    /// <summary>
    /// Add servers discovered via UDP broadcast to the available servers list
    /// </summary>
    private bool AddDiscoveredUdpServers()
    {
        bool foundServers = false;
        float currentTime = Time.time;
        List<string> serversToRemove = new List<string>();
        
        Debug.Log($"********** CHECKING FOR DISCOVERED UDP SERVERS **********\n" +
                  $"Server keys in dictionary: {discoveredServers.Count}");
        
        // Add all discovered servers that have been seen recently
        foreach (var kvp in discoveredServers)
        {
            string serverKey = kvp.Key;
            float lastSeenTime = kvp.Value;
            
            // Check if the server has timed out
            if (currentTime - lastSeenTime > discoveryTimeout)
            {
                // Server hasn't been seen for a while, mark for removal
                serversToRemove.Add(serverKey);
                Debug.Log($"MatchmakingManager: Server {serverKey} timed out");
            }
            else
            {
                // Parse the server key to get IP and port
                string[] parts = serverKey.Split(':');
                if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort port))
                {
                    string ipAddress = parts[0];
                    
                    // Create server info and add to available servers
                    ServerInfo serverInfo = new ServerInfo(
                        $"Server at {ipAddress}",  // Use a generic name since we don't know the actual name
                        ipAddress,
                        port,
                        1,  // Assume at least one player (the host)
                        maxPlayersPerServer,
                        false  // Assume not in game
                    );
                    
                    availableServers.Add(serverInfo);
                    foundServers = true;
                    
                    Debug.Log($"********** ADDING DISCOVERED SERVER **********\n" +
                              $"IP: {ipAddress}\n" +
                              $"Port: {port}\n" +
                              $"Current servers count: {availableServers.Count}");
                }
            }
        }
        
        // Remove timed-out servers
        foreach (string serverKey in serversToRemove)
        {
            discoveredServers.Remove(serverKey);
            Debug.Log($"MatchmakingManager: Removed timed-out server {serverKey}");
        }
        
        return foundServers;
    }
    
    /// <summary>
    /// Attempt to join a server from the available servers list
    /// </summary>
    public void JoinServer(int serverIndex)
    {
        Debug.Log($"Joining server at index {serverIndex}");
        
        if (serverIndex < 0 || serverIndex >= availableServers.Count)
        {
            Debug.LogError($"Invalid server index: {serverIndex}");
            OnJoinServerFailed?.Invoke("Invalid server index");
            return;
        }
        
        JoinServer(availableServers[serverIndex]);
    }
    
    /// <summary>
    /// Join a specific server by its info
    /// </summary>
    public void JoinServer(ServerInfo serverInfo)
    {
        Debug.Log($"Joining server: {serverInfo.serverName}");
        
        // Clean up existing connection first
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.Log("Shutting down existing network session first");
            NetworkManager.Singleton.Shutdown();
            StartCoroutine(JoinServerAfterShutdown(serverInfo));
            return;
        }
        
        // Check if this is a Unity Gaming Services lobby
        if (useUnityServices && !string.IsNullOrEmpty(serverInfo.lobbyId))
        {
            Debug.Log($"Joining Unity Gaming Services lobby: {serverInfo.lobbyId}");
            JoinLobby(serverInfo.lobbyId);
            return;
        }
        
        // Otherwise use direct connection
        JoinServerDirect(serverInfo);
    }
    
    // Add this new method to handle direct connections
    private void JoinServerDirect(ServerInfo serverInfo)
    {
        Debug.Log($"Joining server via direct connection: {serverInfo.ipAddress}:{serverInfo.port}");
        
        // Verify that NetworkManager exists
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is null! Cannot join server.");
            OnJoinServerFailed?.Invoke("NetworkManager not found");
            return;
        }
        
        // Verify again that NetworkManager isn't already running
        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogError("NetworkManager is still listening! Cannot join server.");
            OnJoinServerFailed?.Invoke("NetworkManager shutdown failed");
            return;
        }
        
        // Configure transport for direct connection
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("UnityTransport component not found! Cannot join server.");
            OnJoinServerFailed?.Invoke("Transport not found");
            return;
        }
        
        transport.ConnectionData.Address = serverInfo.ipAddress;
        transport.ConnectionData.Port = serverInfo.port;
        
        // Connect to the server
        bool clientStarted = NetworkManager.Singleton.StartClient();
        
        if (clientStarted)
        {
            Debug.Log($"Client started successfully, connecting to {serverInfo.ipAddress}:{serverInfo.port}");
            currentServerInfo = serverInfo;
        }
        else
        {
            Debug.LogError("Failed to start client");
            OnJoinServerFailed?.Invoke("Failed to start client");
        }
    }
    
    // Add this new method to handle joining after shutdown
    private IEnumerator JoinServerAfterShutdown(ServerInfo serverInfo)
    {
        // Wait for the previous instance to fully shut down
        yield return new WaitForSeconds(1.0f);
        
        // Check if this is a Unity Gaming Services lobby
        if (useUnityServices && !string.IsNullOrEmpty(serverInfo.lobbyId))
        {
            JoinLobby(serverInfo.lobbyId);
        }
        else
        {
            JoinServerDirect(serverInfo);
        }
    }
    
    #endregion
    
    #region NetworkManager Callbacks
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"********** CLIENT CONNECTED TO SERVER **********\n" +
                  $"Client ID: {clientId}\n" +
                  $"Current server: {(currentServerInfo != null ? currentServerInfo.serverName : "None")}\n" +
                  $"Is Host: {IsHost}, Is Server: {IsServer}");
        
        if (currentServerInfo != null)
        {
            // Update player counts
            currentServerInfo.currentPlayers++;
            
            // Update UI if needed
            Debug.Log($"********** SERVER NOW HAS {currentServerInfo.currentPlayers} PLAYERS **********");
            
            // Save updated info
            RegisterServer();
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"********** CLIENT DISCONNECTED FROM SERVER **********\n" +
                  $"Client ID: {clientId}");
        
        if (currentServerInfo != null && currentServerInfo.currentPlayers > 0)
        {
            // Update player counts
            currentServerInfo.currentPlayers--;
            
            // Update UI if needed
            Debug.Log($"********** SERVER NOW HAS {currentServerInfo.currentPlayers} PLAYERS **********");
            
            // Save updated info
            RegisterServer();
        }
    }
    
    private void OnServerStarted()
    {
        Debug.Log($"********** SERVER STARTED SUCCESSFULLY **********\n" +
                  $"Server Name: {(currentServerInfo != null ? currentServerInfo.serverName : "Unknown")}\n" +
                  $"Is Host: {IsHost}, Is Server: {IsServer}");
        
        // Load initial scene if configured
        if (!string.IsNullOrEmpty(lobbySceneName))
        {
            try
            {
                Debug.Log($"********** LOADING LOBBY SCENE: {lobbySceneName} **********");
                
                // Use the Netcode SceneManager to ensure all clients load the scene
                NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
            }
            catch (Exception ex)
            {
                Debug.LogError($"********** ERROR LOADING LOBBY SCENE: {ex.Message} **********\n{ex.StackTrace}");
            }
        }
        
        // Register the server after starting if we have valid server info
        if (currentServerInfo != null)
        {
            // Make sure current player count is accurate
            currentServerInfo.currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
            
            Debug.Log($"Registering server '{currentServerInfo.serverName}' with {currentServerInfo.currentPlayers} players");
            RegisterServer();
        }
        else
        {
            Debug.LogWarning("Cannot register server: currentServerInfo is null");
        }
    }
    
    /// <summary>
    /// Handles scene events for the NetworkManager
    /// </summary>
    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        // Handle different scene events
        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
                Debug.Log($"MatchmakingManager: Scene load started: {sceneEvent.SceneName}");
                break;
                
            case SceneEventType.LoadComplete:
                Debug.Log($"MatchmakingManager: Scene load completed: {sceneEvent.SceneName}");
                
                // If this is the Lobby scene, initialize the LobbyManager
                if (sceneEvent.SceneName.Contains("Lobby"))
                {
                    Debug.Log("MatchmakingManager: Lobby scene loaded, initializing lobby components");
                    SafeStartCoroutine(InitializeLobbyManagerAfterDelay(), "InitializeLobbyManagerAfterDelay");
                }
                break;
                
            case SceneEventType.Unload:
                Debug.Log($"MatchmakingManager: Scene unload started: {sceneEvent.SceneName}");
                break;
                
            case SceneEventType.UnloadComplete:
                Debug.Log($"MatchmakingManager: Scene unload completed: {sceneEvent.SceneName}");
                break;
                
            case SceneEventType.Synchronize:
                Debug.Log($"MatchmakingManager: Scene synchronization started: {sceneEvent.SceneName}");
                break;
                
            case SceneEventType.SynchronizeComplete:
                Debug.Log($"MatchmakingManager: Scene synchronization completed: {sceneEvent.SceneName}");
                break;
        }
    }
    
    #endregion
    
    /// <summary>
    /// Get the current list of available servers
    /// </summary>
    public List<ServerInfo> GetAvailableServers()
    {
        return new List<ServerInfo>(availableServers);
    }
    
    /// <summary>
    /// Connection approval handler for NetworkManager
    /// </summary>
    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // Always approve connections in this demo
        Debug.Log($"MatchmakingManager: Connection approval request from client ID: {request.ClientNetworkId}");
        response.Approved = true;
        response.CreatePlayerObject = false; // We'll handle player creation manually
        response.Position = Vector3.zero;
        response.Rotation = Quaternion.identity;
        response.Pending = false;
    }
    
    /// <summary>
    /// Gets the IP address of the local machine or a sensible default that might be the editor
    /// </summary>
    private string GetDefaultGatewayIP()
    {
        try
        {
            // First try to get the server IP from the file
            string filePath = Path.Combine(Application.persistentDataPath, "serverInfo.json");
            if (File.Exists(filePath))
            {
                string jsonData = File.ReadAllText(filePath);
                ServerInfo serverInfo = JsonUtility.FromJson<ServerInfo>(jsonData);
                if (serverInfo != null && !string.IsNullOrEmpty(serverInfo.ipAddress))
                {
                    return serverInfo.ipAddress;
                }
            }
            
            // If we're on the same network as the editor, let's try to use the same subnet
            string localIP = GetLocalIPv4();
            if (!string.IsNullOrEmpty(localIP) && localIP != "127.0.0.1")
            {
                // Get local network parts (assume standard class C subnet)
                string[] parts = localIP.Split('.');
                if (parts.Length == 4)
                {
                    // Try the first few likely IP addresses in same subnet
                    // This is just a heuristic - the editor is likely on a low number IP
                    return $"{parts[0]}.{parts[1]}.{parts[2]}.1";
                }
            }
            
            // Last resort, use localhost
            return "127.0.0.1";
        }
        catch (Exception ex)
        {
            Debug.LogError($"MatchmakingManager: Error getting default gateway IP: {ex.Message}");
            return "127.0.0.1";
        }
    }
    
    // Character selection method
    public void SelectCharacter(int characterIndex)
    {
        if (characterPrefabs != null && characterIndex >= 0 && characterIndex < characterPrefabs.Length)
        {
            SelectedCharacterPrefab = characterPrefabs[characterIndex];
            Debug.Log($"MatchmakingManager: Selected character: {SelectedCharacterPrefab.name}");
        }
        else
        {
            Debug.LogWarning($"MatchmakingManager: Invalid character index: {characterIndex}");
        }
    }
    
    // Initialize default character in Awake
    private void SetDefaultCharacter()
    {
        if (characterPrefabs != null && characterPrefabs.Length > 0)
        {
            int index = Mathf.Clamp(defaultCharacterIndex, 0, characterPrefabs.Length - 1);
            SelectedCharacterPrefab = characterPrefabs[index];
            Debug.Log($"MatchmakingManager: Default character set to: {SelectedCharacterPrefab.name}");
        }
        else
        {
            Debug.LogWarning("MatchmakingManager: No character prefabs assigned!");
        }
    }
    
    /// <summary>
    /// Clears all server data, used when returning to main menu or when refreshing
    /// </summary>
    public void ClearAllServerData()
    {
        Debug.Log("MatchmakingManager: Clearing all server data");
        
        // Stop any ongoing server discovery
        StopServerDiscovery();
        
        // Clear all discovered servers
        discoveredServers.Clear();
        
        // Clear available servers list
        availableServers.Clear();
        
        // Notify subscribers that the server list is now empty
        OnServerListUpdated?.Invoke(new List<ServerInfo>());
        
        // Clear PlayerPrefs
        PlayerPrefs.DeleteKey("LocalServerName");
        PlayerPrefs.DeleteKey("LocalServerIP");
        PlayerPrefs.DeleteKey("LocalServerPort");
        PlayerPrefs.DeleteKey("LocalServerPlayers");
        PlayerPrefs.DeleteKey("LocalServerMaxPlayers");
        PlayerPrefs.DeleteKey("LocalServerInGame");
        PlayerPrefs.DeleteKey("LocalServerTimestamp");
        PlayerPrefs.Save();
        
        // If we are still a server, re-register it after clearing
        if (IsServer || IsHost)
        {
            Debug.Log("MatchmakingManager: We are still a server, re-registering after clearing data");
            RegisterServer();
        }
    }
    
    /// <summary>
    /// Safely starts a coroutine only if the GameObject is active
    /// </summary>
    private Coroutine SafeStartCoroutine(IEnumerator routine, string routineName = "")
    {
        if (gameObject == null || !gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"MatchmakingManager: Cannot start coroutine '{routineName}' - GameObject is inactive or null");
            return null;
        }
        
        try
        {
            return StartCoroutine(routine);
        }
        catch (Exception ex)
        {
            Debug.LogError($"MatchmakingManager: Error starting coroutine '{routineName}': {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Stop listening for UDP broadcast messages
    /// </summary>
    private void StopUdpDiscovery()
    {
        if (isRefreshingServers)
        {
            // Cancel any async operations
            if (cancellationTokenSource != null)
            {
                try
                {
                    // Cancel the token but wait a small amount of time for tasks to complete
                    // This helps prevent the "Thread may have been prematurely finalized" warning
                    cancellationTokenSource.Cancel();
                    
                    // Check if we can use a coroutine (only if the GameObject is active)
                    if (gameObject.activeInHierarchy)
                    {
                        // Start a coroutine to wait before closing resources
                        SafeStartCoroutine(CloseDiscoveryResourcesAfterDelay(), "CloseDiscoveryResourcesAfterDelay");
                    }
                    else
                    {
                        // GameObject is inactive, we can't use a coroutine, so clean up immediately
                        Debug.Log("MatchmakingManager: GameObject inactive, performing immediate cleanup");
                        CleanupDiscoveryResources();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error stopping UDP discovery: {ex.Message}");
                    
                    // Fall back to immediate cleanup if coroutine fails
                    CleanupDiscoveryResources();
                }
            }
            else
            {
                // If no cancellation token, just cleanup directly
                CleanupDiscoveryResources();
            }
            
            isRefreshingServers = false;
            Debug.Log("MatchmakingManager: Stopped UDP discovery");
        }
    }
    
    // Helper method to delay resource cleanup to give threads time to exit gracefully
    private IEnumerator CloseDiscoveryResourcesAfterDelay()
    {
        // Wait a short time for async operations to notice the cancellation
        yield return new WaitForSeconds(0.1f);
        
        // Clean up resources
        CleanupDiscoveryResources();
    }
    
    // Helper method to clean up discovery resources
    private void CleanupDiscoveryResources()
    {
        // Dispose the cancellation token source
        if (cancellationTokenSource != null)
        {
            cancellationTokenSource.Dispose();
            cancellationTokenSource = null;
        }
        
        // Close the UDP client
        if (discoveryClient != null)
        {
            try 
            {
                discoveryClient.Close();
            }
            catch (Exception ex) 
            {
                Debug.LogWarning($"Error closing discovery client: {ex.Message}");
            }
            finally 
            {
                discoveryClient = null;
            }
        }
    }
    
    // Helper method to properly start the listener task with error handling
    private async Task RunListenerWithErrorHandling(CancellationToken token)
    {
        try
        {
            await ListenForBroadcastsAsync(token);
        }
        catch (OperationCanceledException)
        {
            // This is expected when canceling
            Debug.Log("MatchmakingManager: Listener task was canceled");
        }
        catch (Exception ex)
        {
            Debug.LogError($"MatchmakingManager: Error in listener task: {ex.Message}");
        }
    }

    // Add this method to properly initialize the lobby after scene loading
    private IEnumerator InitializeLobbyManagerAfterDelay()
    {
        // Wait a moment for the scene to fully load
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("MatchmakingManager: Initializing lobby components after delay");
        
        // Look for any lobby-related components that need initialization
        var networkObjects = FindObjectsOfType<NetworkObject>();
        foreach (var netObj in networkObjects)
        {
            // Ensure network objects are properly set up
            if (IsServer && !netObj.IsSpawned && netObj.gameObject.activeInHierarchy)
            {
                try
                {
                    Debug.Log($"MatchmakingManager: Auto-spawning network object: {netObj.name}");
                    netObj.Spawn();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"MatchmakingManager: Error spawning network object {netObj.name}: {ex.Message}");
                }
            }
        }
        
        // Find any game-specific managers that might need initialization
        // This is a generic implementation - customize as needed for your game
        Debug.Log("MatchmakingManager: Lobby initialization completed");
    }

    // Cloud Server Creation
    private async Task<bool> CreateRelayServerAsync(string serverName)
    {
        if (!isServicesInitialized)
        {
            Debug.LogError("Unity Services not initialized. Cannot create relay server.");
            return false;
        }
        
        try
        {
            // Double-check that NetworkManager isn't already running
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogError("NetworkManager is still running. Cannot create a new relay server.");
                return false;
            }
            
            // Select region (auto or specific)
            string region = relayRegionIndex > 0 && relayRegionIndex < availableRegions.Length 
                ? availableRegions[relayRegionIndex] 
                : null; // null = auto
                
            Debug.Log($"Creating Relay allocation in region: {region ?? "auto"}");
            
            // Create allocation - add more detailed logging
            Debug.Log($"Requesting allocation for {maxPlayersPerServer} players...");
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayersPerServer, region);
            Debug.Log($"Allocation created successfully. Allocation ID: {allocation.AllocationId}");
            
            // Get join code - add more detailed logging
            Debug.Log("Requesting join code for allocation...");
            relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            
            Debug.Log($"Relay server created with join code: {relayJoinCode}");
            
            // Store the creation time to track potential expiration (allocations typically last 10 minutes)
            PlayerPrefs.SetString("RelayJoinCode", relayJoinCode);
            PlayerPrefs.SetString("RelayAllocationTime", DateTime.UtcNow.ToString("o"));
            PlayerPrefs.Save();
            
            // Set up relay transport
            Debug.Log("Configuring transport with relay data...");
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            
            try 
            {
                // Create relay server data
                RelayServerData relayServerData = new RelayServerData(allocation, "dtls");
                transport.SetRelayServerData(relayServerData);
                
                // The UseRelayServer property doesn't exist in this version of the transport
                // Just log that we're using relay
                Debug.Log($"Transport configured with relay server data. Allocation ID: {allocation.AllocationId}");
            }
            catch (Exception ex) 
            {
                Debug.LogError($"Error configuring transport with relay data: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
            
            // Create the lobby that will contain this relay join code
            try
            {
                Debug.Log($"Creating lobby '{serverName}' with relay code...");
                await CreateLobbyWithRelayCodeAsync(serverName, relayJoinCode);
                Debug.Log("Lobby created successfully with relay join code");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create lobby, but relay allocation was successful. Continuing with host: {ex.Message}");
                // We'll continue without the lobby - at least the relay will work
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating relay server: {ex.Message}");
            Debug.LogException(ex); // Log the full exception for better debugging
            return false;
        }
    }

    // Add this method to check if the current relay allocation is likely to be expired
    private bool IsRelayAllocationLikelyExpired()
    {
        if (!PlayerPrefs.HasKey("RelayAllocationTime"))
            return true;
            
        string timeStr = PlayerPrefs.GetString("RelayAllocationTime");
        if (DateTime.TryParse(timeStr, out DateTime allocationTime))
        {
            // Relay allocations typically expire after 10 minutes of inactivity
            TimeSpan timeSinceAllocation = DateTime.UtcNow - allocationTime;
            return timeSinceAllocation.TotalMinutes > 8; // Use 8 minutes as a safety margin
        }
        
        return true; // If we can't parse the time, assume it's expired
    }

    // Add this method to refresh the server's relay allocation when needed
    private async Task RefreshRelayAllocationIfNeededAsync()
    {
        // Only do this if we're the host
        if (!IsHost || currentLobby == null || string.IsNullOrEmpty(relayJoinCode))
            return;
            
        // Check if allocation might be expiring soon
        if (IsRelayAllocationLikelyExpired())
        {
            Debug.Log("Relay allocation may be expiring soon. Creating a new allocation...");
            
            try
            {
                // Create a new allocation
                string region = relayRegionIndex > 0 && relayRegionIndex < availableRegions.Length 
                    ? availableRegions[relayRegionIndex] 
                    : null;
                    
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayersPerServer, region);
                string newJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                
                Debug.Log($"Created new relay allocation with join code: {newJoinCode}");
                
                // Update the join code in the lobby
                UpdateLobbyOptions options = new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { "JoinCode", new DataObject(DataObject.VisibilityOptions.Public, newJoinCode) }
                    }
                };
                
                await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, options);
                
                // Update our stored join code
                relayJoinCode = newJoinCode;
                
                // Update the allocation time
                PlayerPrefs.SetString("RelayJoinCode", relayJoinCode);
                PlayerPrefs.SetString("RelayAllocationTime", DateTime.UtcNow.ToString("o"));
                PlayerPrefs.Save();
                
                Debug.Log("Successfully refreshed relay allocation and updated lobby join code");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to refresh relay allocation: {ex.Message}");
            }
        }
    }

    // Modify the lobbyHeartbeatCoroutine to also refresh relay allocations
    private IEnumerator LobbyHeartbeatCoroutine()
    {
        while (currentLobby != null)
        {
            yield return new WaitForSeconds(15); // Send heartbeat every 15 seconds
            
            try
            {
                // Start the async task without awaiting it in the coroutine
                var heartbeatTask = SendHeartbeat(currentLobby.Id);
                // We can't await in a coroutine, so we'll use a continuation to handle errors
                heartbeatTask.ContinueWith(task => {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Debug.LogError($"Failed to send heartbeat: {task.Exception.InnerException?.Message}");
                        // Check if lobby exists
                        var checkTask = CheckLobbyExists(currentLobby.Id);
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
                
                // Also refresh the relay allocation if needed (approximately every 5 minutes)
                // We'll use a random check to avoid all servers refreshing at the same exact time
                if (UnityEngine.Random.value < 0.2f) // ~20% chance each heartbeat (so ~every 75 seconds on average)
                {
                    var refreshTask = RefreshRelayAllocationIfNeededAsync();
                    refreshTask.ContinueWith(task => {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            Debug.LogError($"Failed to refresh relay allocation: {task.Exception.InnerException?.Message}");
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to send heartbeat: {ex.Message}");
            }
        }
    }
    
    
    // Helper method for heartbeat
    private async Task SendHeartbeat(string lobbyId)
    {
        await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
        Debug.Log($"Lobby heartbeat sent: {lobbyId}");
    }
    
    // Helper method to check if lobby exists
    private async Task CheckLobbyExists(string lobbyId)
    {
        try
        {
            currentLobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);
        }
        catch
        {
            Debug.LogError("Lobby no longer exists, stopping heartbeat");
            currentLobby = null;
        }
    }
    
    // Update lobby data periodically
    private IEnumerator LobbyUpdateCoroutine()
    {
        while (currentLobby != null)
        {
            yield return new WaitForSeconds(5); // Update every 5 seconds
            
            if (NetworkManager.Singleton == null || currentLobby == null)
                continue;
                
            try
            {
                // Update the lobby data with current player count
                int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
                
                // Check if game is in progress using active scene
                bool inGame = false;
                if (NetworkManager.Singleton.SceneManager != null)
                {
                    // Get active scene name safely
                    string activeScene = SceneManager.GetActiveScene().name;
                    inGame = activeScene != lobbySceneName;
                }
                
                // Start the async task without awaiting it in the coroutine
                var updateTask = UpdateLobbyData(currentLobby.Id, currentPlayers, inGame);
                updateTask.ContinueWith(task => {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Debug.LogError($"Failed to update lobby: {task.Exception.InnerException?.Message}");
                        // Check if lobby exists
                        var checkTask = CheckLobbyExists(currentLobby.Id);
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception preparing lobby update: {ex.Message}");
            }
        }
    }
    
    // Helper method for updating lobby data
    private async Task UpdateLobbyData(string lobbyId, int playerCount, bool inGame)
    {
        try {
            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { "CurrentPlayers", new DataObject(DataObject.VisibilityOptions.Public, playerCount.ToString()) },
                    { "InGame", new DataObject(DataObject.VisibilityOptions.Public, inGame.ToString()) }
                }
            };
            
            currentLobby = await LobbyService.Instance.UpdateLobbyAsync(lobbyId, options);
            Debug.Log($"Lobby updated: {lobbyId}, Players: {playerCount}");
        }
        catch (Exception ex) {
            Debug.LogError($"Error updating lobby data: {ex.Message}");
        }
    }
    
    // Query available lobbies
    public async Task<List<ServerInfo>> QueryLobbiesAsync()
    {
        if (!isServicesInitialized)
        {
            Debug.LogError("Unity Services not initialized. Cannot query lobbies.");
            return new List<ServerInfo>();
        }
        
        try
        {
            // Check if we're querying too frequently
            float currentTime = Time.realtimeSinceStartup;
            if (currentTime - lastLobbyQueryTime < minLobbyQueryInterval)
            {
                Debug.LogWarning($"Skipping lobby query due to rate limiting. Please wait {minLobbyQueryInterval - (currentTime - lastLobbyQueryTime):F1} seconds before querying again.");
                // Return the existing cached lobbies instead of making a new request
                List<ServerInfo> cachedServers = new List<ServerInfo>();
                foreach (var lobbyPair in availableLobbies)
                {
                    Lobby lobby = lobbyPair.Value;
                    // Use the data we already have to create server info objects
                    if (!lobby.Data.ContainsKey("JoinCode")) continue;
                    
                    string joinCode = lobby.Data["JoinCode"].Value;
                    int currentPlayers = 1;
                    int maxPlayers = maxPlayersPerServer;
                    bool inGame = false;
                    
                    if (lobby.Data.ContainsKey("CurrentPlayers"))
                        int.TryParse(lobby.Data["CurrentPlayers"].Value, out currentPlayers);
                    if (lobby.Data.ContainsKey("MaxPlayers"))
                        int.TryParse(lobby.Data["MaxPlayers"].Value, out maxPlayers);
                    if (lobby.Data.ContainsKey("InGame"))
                        bool.TryParse(lobby.Data["InGame"].Value, out inGame);
                    
                    ServerInfo serverInfo = new ServerInfo(
                        lobby.Name,
                        "UGS Relay",
                        7777,
                        currentPlayers,
                        maxPlayers,
                        inGame
                    );
                    serverInfo.lobbyId = lobby.Id;
                    cachedServers.Add(serverInfo);
                }
                return cachedServers;
            }
            
            // Update the timestamp for the lobby query
            lastLobbyQueryTime = currentTime;
            
            Debug.Log("Querying available lobbies from Unity Gaming Services...");
            
            // Options for lobby query
            QueryLobbiesOptions options = new QueryLobbiesOptions
            {
                Count = 25, // Get up to 25 lobbies
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                },
                Order = new List<QueryOrder>
                {
                    new QueryOrder(
                        field: Unity.Services.Lobbies.Models.QueryOrder.FieldOptions.Created, 
                        asc: false) // false = descending order (newest first)
                }
            };
            
            // Query lobbies
            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
            Debug.Log($"Found {response.Results.Count} lobbies via Unity Gaming Services");
            
            // Clear previous lobbies
            availableLobbies.Clear();
            
            // Convert lobbies to server info objects
            List<ServerInfo> serverList = new List<ServerInfo>();
            
            foreach (Lobby lobby in response.Results)
            {
                // Skip lobbies with incomplete data
                if (!lobby.Data.ContainsKey("JoinCode"))
                {
                    Debug.LogWarning($"Skipping lobby with missing JoinCode: {lobby.Name} (ID: {lobby.Id})");
                    continue;
                }
                
                // Save reference to lobby
                availableLobbies[lobby.Id] = lobby;
                
                // Extract data
                string joinCode = lobby.Data["JoinCode"].Value;
                
                // Use defaults if data is missing
                int currentPlayers = 1; // Default to at least 1 player (the host)
                int maxPlayers = maxPlayersPerServer;
                bool inGame = false;
                
                if (lobby.Data.ContainsKey("CurrentPlayers"))
                {
                    int.TryParse(lobby.Data["CurrentPlayers"].Value, out currentPlayers);
                }
                
                if (lobby.Data.ContainsKey("MaxPlayers"))
                {
                    int.TryParse(lobby.Data["MaxPlayers"].Value, out maxPlayers);
                }
                
                if (lobby.Data.ContainsKey("InGame"))
                {
                    bool.TryParse(lobby.Data["InGame"].Value, out inGame);
                }
                
                // Log detailed lobby info for debugging
                Debug.Log($"Found lobby: {lobby.Name} (ID: {lobby.Id})\n" +
                         $"  Join Code: {joinCode}\n" +
                         $"  Players: {currentPlayers}/{maxPlayers}\n" +
                         $"  In Game: {inGame}\n" +
                         $"  Created: {lobby.Created}\n" +
                         $"  Last Updated: {lobby.LastUpdated}");
                
                // Create server info
                ServerInfo serverInfo = new ServerInfo(
                    lobby.Name,
                    "UGS Relay", // IP address not relevant for relay
                    7777, // Port not relevant for relay
                    currentPlayers,
                    maxPlayers,
                    inGame
                );
                
                // Store the lobby ID in the server info for lookup later
                serverInfo.lobbyId = lobby.Id;
                
                serverList.Add(serverInfo);
            }
            
            Debug.Log($"Successfully processed {serverList.Count} lobbies");
            return serverList;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error querying lobbies: {ex.Message}");
            Debug.LogException(ex); // Log the full exception for better debugging
            return new List<ServerInfo>();
        }
    }
    
    // Add this to clean up when leaving a server
    public async Task LeaveServer()
    {
        if (currentLobby != null && isServicesInitialized)
        {
            try
            {
                // Leave the lobby
                await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, playerId);
                Debug.Log($"Left lobby: {currentLobby.Id}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error leaving lobby: {ex.Message}");
            }
            
            // Clean up coroutines
            if (lobbyHeartbeatCoroutine != null)
            {
                StopCoroutine(lobbyHeartbeatCoroutine);
                lobbyHeartbeatCoroutine = null;
            }
            
            if (lobbyUpdateCoroutine != null)
            {
                StopCoroutine(lobbyUpdateCoroutine);
                lobbyUpdateCoroutine = null;
            }
            
            currentLobby = null;
        }
        
        // Shut down the network connection
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        currentServerInfo = null;
    }

    // Add a new method to join a Unity Services server
    public void JoinLobby(string lobbyId)
    {
        if (!availableLobbies.ContainsKey(lobbyId))
        {
            Debug.LogError($"Lobby with ID {lobbyId} not found in available lobbies");
            OnJoinServerFailed?.Invoke("Lobby not found");
            return;
        }
        
        Lobby lobby = availableLobbies[lobbyId];
        // Start the async task without awaiting
        var _ = JoinRelayServer(lobby);
    }

    // Create a lobby with the relay join code
    private async Task CreateLobbyWithRelayCodeAsync(string serverName, string joinCode)
    {
        try
        {
            // Create lobby options
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "IsHost", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "true") }
                    }
                },
                Data = new Dictionary<string, DataObject>
                {
                    { "JoinCode", new DataObject(DataObject.VisibilityOptions.Public, joinCode) },
                    { "CurrentPlayers", new DataObject(DataObject.VisibilityOptions.Public, "1") },
                    { "MaxPlayers", new DataObject(DataObject.VisibilityOptions.Public, maxPlayersPerServer.ToString()) },
                    { "InGame", new DataObject(DataObject.VisibilityOptions.Public, "false") }
                }
            };
            
            // Create the lobby
            string lobbyNameWithId = $"{serverName}#{playerId.Substring(0, 6)}";
            currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyNameWithId, maxPlayersPerServer, options);
            
            Debug.Log($"Lobby created: {currentLobby.Name}, ID: {currentLobby.Id}");
            
            // Start heartbeat and update coroutines
            if (lobbyHeartbeatCoroutine != null)
                StopCoroutine(lobbyHeartbeatCoroutine);
                
            lobbyHeartbeatCoroutine = StartCoroutine(LobbyHeartbeatCoroutine());
            
            if (lobbyUpdateCoroutine != null)
                StopCoroutine(lobbyUpdateCoroutine);
                
            lobbyUpdateCoroutine = StartCoroutine(LobbyUpdateCoroutine());
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating lobby: {ex.Message}");
            throw;
        }
    }

    // Helper method to check if a join code is valid (without actually joining)
    private async Task<bool> IsJoinCodeValidAsync(string joinCode)
    {
        try
        {
            // Make a direct API call to validate the join code without joining
            // We can use the GetJoinCodeAsync method with a short timeout
            var task = RelayService.Instance.JoinAllocationAsync(joinCode);
            
            // Use a timeout to avoid waiting too long
            var timeoutTask = Task.Delay(5000); // 5 second timeout
            
            if (await Task.WhenAny(task, timeoutTask) == task)
            {
                // The join code is valid
                return true;
            }
            else
            {
                // Timed out
                Debug.LogWarning($"Join code validation timed out for: {joinCode}");
                return false;
            }
        }
        catch (Exception)
        {
            // Any exception means the join code is invalid
            return false;
        }
    }

    // Join a Unity Relay server
    public async Task JoinRelayServer(Lobby lobby)
    {
        try
        {
            // Make sure Unity Services are initialized
            if (!isServicesInitialized)
            {
                Debug.Log("Unity Services not initialized. Initializing now...");
                try {
                    await InitializeUnityServicesAsync();
                }
                catch (Exception ex) {
                    Debug.LogError($"Failed to initialize Unity Services: {ex.Message}");
                    OnJoinServerFailed?.Invoke("Failed to initialize Unity Services");
                    return;
                }
            }
            
            // Verify that NetworkManager exists and isn't running
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("NetworkManager.Singleton is null! Cannot join relay server.");
                OnJoinServerFailed?.Invoke("NetworkManager not found");
                return;
            }
            
            // If NetworkManager is already listening, shut it down and wait for it to fully shut down
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("NetworkManager is still listening! Shutting down and waiting...");
                
                // Unsubscribe and resubscribe to events to ensure clean state
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
                NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
                
                // Shut down the NetworkManager
                NetworkManager.Singleton.Shutdown();
                
                // Wait for NetworkManager to completely shut down with a generous timeout
                int attempts = 0;
                int maxAttempts = 20; // Increased from 10 to 20
                while (NetworkManager.Singleton.IsListening && attempts < maxAttempts)
                {
                    Debug.Log($"Waiting for NetworkManager to shut down... Attempt {attempts + 1}/{maxAttempts}");
                    await Task.Delay(300); // Shorter, more frequent checks
                    attempts++;
                }
                
                if (NetworkManager.Singleton.IsListening)
                {
                    Debug.LogError("NetworkManager failed to shut down after multiple attempts!");
                    // Additional cleanup attempt - try to force a full shutdown
                    try {
                        var networkManager = NetworkManager.Singleton.gameObject;
                        MonoBehaviour.Destroy(networkManager);
                        await Task.Delay(500);
                        Debug.LogWarning("Attempted to destroy NetworkManager GameObject as a last resort");
                    }
                    catch (Exception ex) {
                        Debug.LogError($"Failed to destroy NetworkManager: {ex.Message}");
                    }
                    OnJoinServerFailed?.Invoke("NetworkManager shutdown failed");
                    return;
                }
                
                // Add additional delay after shutdown to ensure everything is clean
                await Task.Delay(500);
                
                // Resubscribe to events
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
                
                Debug.Log("NetworkManager successfully shut down, proceeding with server join");
            }
            
            // Try to get the most up-to-date lobby information first
            try 
            {
                Debug.Log($"Attempting to refresh lobby information for ID: {lobby.Id}");
                lobby = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
                Debug.Log($"Successfully refreshed lobby data for: {lobby.Name}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not refresh lobby data (proceeding with current data): {ex.Message}");
                // Continue with the existing lobby data
            }
            
            // Get the relay join code from the lobby data
            if (!lobby.Data.ContainsKey("JoinCode"))
            {
                Debug.LogError("Lobby does not contain a JoinCode!");
                OnJoinServerFailed?.Invoke("Invalid lobby data - missing join code");
                return;
            }
            
            string joinCode = lobby.Data["JoinCode"].Value;
            Debug.Log($"Found join code in lobby: {joinCode}");
            
            try
            {
                // Join the relay server
                Debug.Log($"Joining relay server with join code: {joinCode}");
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                Debug.Log($"Successfully created join allocation from join code. Allocation ID: {joinAllocation.AllocationId}");
                
                // Set up the transport
                UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    Debug.LogError("UnityTransport component not found! Cannot join relay server.");
                    OnJoinServerFailed?.Invoke("Transport not found");
                    return;
                }
                
                // Reset the transport to clear any previous state
                Debug.Log("Resetting transport before configuring relay data...");
                transport.Shutdown();
                
                try 
                {
                    // Set up relay server data with specific protocol
                    Debug.Log("Creating new RelayServerData with DTLS protocol...");
                    RelayServerData relayServerData = new RelayServerData(joinAllocation, "dtls");
                    
                    // Extra debugging for relay server data
                    Debug.Log($"RelayServerData created. Allocation ID: {joinAllocation.AllocationId}");
                    Debug.Log($"Connection Data - Host: {joinAllocation.RelayServer.IpV4}:{joinAllocation.RelayServer.Port}");
                    
                    // Set up the transport with the relay data
                    transport.SetRelayServerData(relayServerData);
                    Debug.Log("Successfully configured transport with relay server data");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error configuring transport with relay data: {ex.Message}");
                    Debug.LogException(ex);
                    OnJoinServerFailed?.Invoke("Failed to configure transport");
                    return;
                }
                
                // Save the lobby reference and create the ServerInfo before starting the client
                currentLobby = lobby;
                
                // Create a ServerInfo object for UI
                ServerInfo serverInfo = new ServerInfo(
                    lobby.Name,
                    "UGS Relay", // IP address not relevant for relay
                    7777, // Port not relevant for relay
                    0, // We'll get the real count from the server
                    int.TryParse(lobby.Data.ContainsKey("MaxPlayers") ? lobby.Data["MaxPlayers"].Value : "0", out int maxPlayers) ? maxPlayers : 0,
                    bool.TryParse(lobby.Data.ContainsKey("InGame") ? lobby.Data["InGame"].Value : "false", out bool inGame) ? inGame : false
                );
                serverInfo.lobbyId = lobby.Id;
                currentServerInfo = serverInfo;
                
                // Add additional diagnostics before starting client
                Debug.Log($"NetworkManager state before StartClient: IsHost={NetworkManager.Singleton.IsHost}, IsServer={NetworkManager.Singleton.IsServer}, IsClient={NetworkManager.Singleton.IsClient}, IsListening={NetworkManager.Singleton.IsListening}");
                
                // Make sure NetworkConfig is initialized correctly
                if (NetworkManager.Singleton.NetworkConfig == null)
                {
                    Debug.LogError("NetworkConfig is null! Cannot start client.");
                    OnJoinServerFailed?.Invoke("NetworkConfig is null");
                    return;
                }
                
                // Ensure NetworkTransport is set in the NetworkConfig
                if (NetworkManager.Singleton.NetworkConfig.NetworkTransport == null)
                {
                    Debug.LogError("NetworkTransport is not set in NetworkConfig! Cannot start client.");
                    OnJoinServerFailed?.Invoke("NetworkTransport not set");
                    return;
                }
                
                // Check if the NetworkConfig is referencing our transport 
                if (NetworkManager.Singleton.NetworkConfig.NetworkTransport != transport) {
                    Debug.LogWarning("NetworkConfig transport reference doesn't match our transport component. Updating reference...");
                    NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
                }
                
                // Validate other NetworkConfig settings
                Debug.Log($"NetworkConfig Validation - ProtocolVersion: {NetworkManager.Singleton.NetworkConfig.ProtocolVersion}");
                Debug.Log($"NetworkConfig Validation - NetworkTransport: {(NetworkManager.Singleton.NetworkConfig.NetworkTransport != null ? "Set" : "NULL")}");
                
                // Try additional reset of NetworkConfig if needed
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
                
                // Start the client
                Debug.Log("Starting client to connect to relay server");
                
                try
                {
                    bool clientStarted = NetworkManager.Singleton.StartClient();
                    
                    if (clientStarted)
                    {
                        Debug.Log("Successfully started client to connect to the relay server");
                        OnJoinServerSuccess?.Invoke();
                    }
                    else
                    {
                        Debug.LogError("Failed to start client after joining relay. This could be due to:");
                        Debug.LogError("- NetworkManager already running in another mode");
                        Debug.LogError("- NetworkConfig not properly set up");
                        Debug.LogError("- Transport not properly configured with relay data");
                        
                        // Try to get more diagnostic info from NetworkManager
                        Debug.LogError($"NetworkManager state after failed StartClient: IsHost={NetworkManager.Singleton.IsHost}, IsServer={NetworkManager.Singleton.IsServer}, IsClient={NetworkManager.Singleton.IsClient}, IsListening={NetworkManager.Singleton.IsListening}");
                        
                        // Try to access transport info for more diagnostics
                        try {
                            var connectionData = transport.ConnectionData;
                            Debug.Log($"Transport connection data: {connectionData.Address}:{connectionData.Port}, using Relay: {transport.Protocol == UnityTransport.ProtocolType.RelayUnityTransport}");
                        }
                        catch (Exception ex) {
                            Debug.LogError($"Could not get transport connection data: {ex.Message}");
                        }
                        
                        currentServerInfo = null; // Clear the server info since it wasn't started
                        OnJoinServerFailed?.Invoke("Failed to start client");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception thrown when starting client: {ex.Message}");
                    Debug.LogException(ex);
                    currentServerInfo = null;
                    OnJoinServerFailed?.Invoke($"Error starting client: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error joining relay server: {ex.Message}");
                Debug.LogException(ex); // Log the full exception for better debugging
                
                // Provide a more user-friendly error message based on the exception
                string errorMessage = "Failed to join the game.";
                
                if (ex.Message.Contains("Not Found") || ex.Message.Contains("404"))
                {
                    errorMessage = "The game server appears to be expired or no longer available. The host may need to recreate the server.";
                }
                else if (ex.Message.Contains("Unauthorized") || ex.Message.Contains("401"))
                {
                    errorMessage = "Authentication error. Please restart the game and try again.";
                }
                else if (ex.Message.Contains("Timeout") || ex.Message.Contains("timed out"))
                {
                    errorMessage = "Connection timed out. Please check your internet connection and try again.";
                }
                
                OnJoinServerFailed?.Invoke(errorMessage);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error in JoinRelayServer: {ex.Message}");
            Debug.LogException(ex);
            OnJoinServerFailed?.Invoke("An unexpected error occurred. Please try again.");
        }
    }
}

// Helper extension methods for task cancellation
public static class TaskExtensions
{
    /// <summary>
    /// Extension method to add cancellation support to an existing Task<T>
    /// </summary>
    public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (cancellationToken.Register(() => tcs.TrySetResult(true)))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
                throw new OperationCanceledException(cancellationToken);
        }
        return await task; // The task has already completed successfully here
    }
    
    /// <summary>
    /// Extension method to add cancellation support to an existing Task
    /// </summary>
    public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (cancellationToken.Register(() => tcs.TrySetResult(true)))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
                throw new OperationCanceledException(cancellationToken);
        }
        await task; // The task has already completed successfully here
    }
}

