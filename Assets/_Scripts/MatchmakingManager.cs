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
    
    [Header("Character Selection")]
    [SerializeField] private GameObject[] characterPrefabs;
    [SerializeField] private int defaultCharacterIndex = 0;
    
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
    
    private void Awake()
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
        
        // Stop server discovery
        StopServerDiscovery();
        
        // Close any remaining network connections
        if (broadcastClient != null)
        {
            broadcastClient.Close();
            broadcastClient = null;
        }
        
        if (discoveryClient != null)
        {
            discoveryClient.Close();
            discoveryClient = null;
        }
        
        // Cancel any pending tasks
        if (cancellationTokenSource != null)
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource = null;
        }
    }
    
    #region Server Creation and Management
    
    /// <summary>
    /// Create a new dedicated server with the given name
    /// </summary>
    public void CreateServer(string serverName)
    {
        Debug.Log($"MatchmakingManager: Creating server: {serverName}");
        
        // Check if we're already hosting
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer))
        {
            Debug.LogWarning("MatchmakingManager: Already hosting a server. Shutting down before creating new one.");
            NetworkManager.Singleton.Shutdown();
            // Allow a short delay for proper cleanup
            StartCoroutine(CreateServerAfterShutdown(serverName));
            return;
        }
        
        CreateServerInternal(serverName);
    }
    
    private IEnumerator CreateServerAfterShutdown(string serverName)
    {
        // Wait for the previous instance to fully shut down
        yield return new WaitForSeconds(1.0f);
        CreateServerInternal(serverName);
    }
    
    private void CreateServerInternal(string serverName)
    {
        // Ensure NetworkManager exists
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("MatchmakingManager: NetworkManager.Singleton is null! Cannot create server.");
            OnCreateServerFailed?.Invoke("NetworkManager not found");
            return;
        }
        
        // Configure the transport
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.Log("MatchmakingManager: Adding UnityTransport component");
            transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
        }
        
        Debug.Log("MatchmakingManager: Configuring transport");
        // Use default settings for hosting
        transport.ConnectionData.Address = "0.0.0.0"; // Listen on all interfaces
        transport.ConnectionData.Port = 7777; // Default port
        
        // IMPORTANT: Set the transport in the NetworkConfig
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        
        // Set up the NetworkManager SceneManager if null
        if (NetworkManager.Singleton.SceneManager == null)
        {
            Debug.Log("MatchmakingManager: Initializing NetworkManager.SceneManager");
            // Initialize NetworkSceneManager if needed
            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = true;
        }
        
        // Get our real local IP for discovery
        string localIP = GetLocalIPv4();
        Debug.Log($"MatchmakingManager: Local IP is {localIP}. This needs to be accessible from the internet!");
        
        // Create the server info
        currentServerInfo = new ServerInfo(
            serverName,
            localIP, // Use our real local IP for clients to connect to
            7777, // Default port
            0, // No players yet
            maxPlayersPerServer,
            false // Not in game yet
        );
        
        // Clear any old server registrations
        CleanupOldServerRegistrations();
        
        // Register callback events if not already registered
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        
        Debug.Log($"MatchmakingManager: Starting host with server name: {serverName}");
        
        try {
            // Start the server
            NetworkManager.Singleton.StartHost();
            
            // Register the server so it can be discovered
            RegisterServer();
            
            Debug.Log($"MatchmakingManager: Created server: {serverName}");
            OnCreateServerSuccess?.Invoke();
        }
        catch (Exception e) {
            Debug.LogError($"MatchmakingManager: Failed to start host: {e.Message}");
            OnCreateServerFailed?.Invoke(e.Message);
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
    
    private void RegisterServer()
    {
        try
        {
            Debug.Log($"MatchmakingManager: Registering server: {currentServerInfo.serverName} at {currentServerInfo.ipAddress}:{currentServerInfo.port}");
            
            // Add our server to the list of available servers
            if (!availableServers.Contains(currentServerInfo))
            {
                availableServers.Add(currentServerInfo);
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
            
            Debug.Log($"MatchmakingManager: Server info written to {filePath}");
            
            // Start broadcasting server presence on the network
            StartServerBroadcast();
            
            // Debug output the PlayerPrefs values after setting them
            Debug.Log($"MatchmakingManager: Verified PlayerPrefs after registration: " +
                      $"Name={PlayerPrefs.GetString("LocalServerName")} " +
                      $"IP={PlayerPrefs.GetString("LocalServerIP")} " +
                      $"Port={PlayerPrefs.GetInt("LocalServerPort")}");
            
            // Notify success
            OnCreateServerSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError($"MatchmakingManager: Error registering server: {ex.Message}");
            OnCreateServerFailed?.Invoke("Error registering server: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Start broadcasting this server's presence on the local network
    /// </summary>
    private void StartServerBroadcast()
    {
        if (broadcastClient != null)
        {
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
            StartCoroutine(BroadcastServerInfoCoroutine());
            
            Debug.Log("MatchmakingManager: Started server broadcast");
        }
        catch (Exception ex)
        {
            Debug.LogError($"MatchmakingManager: Error starting server broadcast: {ex.Message}");
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
        while (true)
        {
            try
            {
                // Prepare server info to broadcast
                string jsonData = JsonUtility.ToJson(currentServerInfo);
                byte[] data = Encoding.UTF8.GetBytes(jsonData);
                
                // Try different broadcast methods
                TryBroadcastToNetwork(data);
                
                Debug.Log($"MatchmakingManager: Broadcast server info: {currentServerInfo.serverName} at {currentServerInfo.ipAddress}:{currentServerInfo.port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"MatchmakingManager: Error broadcasting server info: {ex.Message}");
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
        // LOCAL NETWORK BROADCASTING:
        // First, try standard LAN broadcasting methods
        
        // Method 1: General broadcast (works on simple networks)
        try
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
            broadcastClient.Send(data, data.Length, endpoint);
            Debug.Log("MatchmakingManager: Local broadcast sent");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MatchmakingManager: General broadcast failed: {ex.Message}");
        }
        
        // Method 2: Try local subnet broadcast
        try
        {
            // Get local IP and create subnet broadcast
            string localIP = GetLocalIPv4();
            if (!string.IsNullOrEmpty(localIP) && localIP != "127.0.0.1")
            {
                string[] parts = localIP.Split('.');
                if (parts.Length == 4)
                {
                    // Create subnet broadcast address (e.g., 192.168.1.255)
                    string broadcastIP = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
                    IPEndPoint subnetEndpoint = new IPEndPoint(IPAddress.Parse(broadcastIP), discoveryPort);
                    broadcastClient.Send(data, data.Length, subnetEndpoint);
                    Debug.Log($"MatchmakingManager: Subnet broadcast sent to {broadcastIP}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MatchmakingManager: Subnet broadcast failed: {ex.Message}");
        }
        
        // INTERNET DISCOVERY:
        
        // Method 3: Use common gateway addresses - this helps discovery in some network configurations
        try {
            // Try sending to common default gateway IPs
            string[] commonGateways = new string[] {
                "192.168.0.1", "192.168.1.1", "192.168.2.1", "10.0.0.1", "10.0.0.138"
            };
            
            foreach (string gateway in commonGateways) {
                try {
                    IPEndPoint gatewayEndpoint = new IPEndPoint(IPAddress.Parse(gateway), discoveryPort);
                    broadcastClient.Send(data, data.Length, gatewayEndpoint);
                }
                catch {
                    // Ignore individual gateway failures
                }
            }
            
            // Try to determine and use the actual gateway
            string actualGateway = GetDefaultGatewayIP();
            if (!string.IsNullOrEmpty(actualGateway)) {
                IPEndPoint gatewayEndpoint = new IPEndPoint(IPAddress.Parse(actualGateway), discoveryPort);
                broadcastClient.Send(data, data.Length, gatewayEndpoint);
                Debug.Log($"MatchmakingManager: Sent to gateway: {actualGateway}");
            }
        }
        catch (Exception ex) {
            Debug.LogWarning($"MatchmakingManager: Gateway broadcast failed: {ex.Message}");
        }
        
        // Method 4: External IP Address Discovery (UDP Hole Punching technique)
        try {
            // Send to a range of potential IP addresses in the local network
            // This effectively "punches" through NAT in some configurations
            string localIP = GetLocalIPv4();
            if (!string.IsNullOrEmpty(localIP) && localIP != "127.0.0.1") {
                string[] parts = localIP.Split('.');
                if (parts.Length == 4) {
                    // Try sending to a range of IPs in the same subnet
                    for (int i = 1; i < 255; i++) {
                        // Don't send too many packets to avoid flooding
                        if (i % 25 == 0) { // Only try IPs at intervals to reduce traffic
                            string targetIP = $"{parts[0]}.{parts[1]}.{parts[2]}.{i}";
                            try {
                                IPEndPoint targetEndpoint = new IPEndPoint(IPAddress.Parse(targetIP), discoveryPort);
                                broadcastClient.Send(data, data.Length, targetEndpoint);
                            }
                            catch {
                                // Ignore individual failures
                            }
                        }
                    }
                    Debug.Log("MatchmakingManager: Sent discovery packets to multiple IPs in subnet");
                }
            }
        }
        catch (Exception ex) {
            Debug.LogWarning($"MatchmakingManager: Network range broadcast failed: {ex.Message}");
        }
        
        // Log networking information - this helps with troubleshooting
        string externalIP = GetExternalIP();
        Debug.Log("==== SERVER NETWORK INFO ====");
        Debug.Log($"Local IP: {GetLocalIPv4()}");
        Debug.Log($"Possible Public IP: {(string.IsNullOrEmpty(externalIP) ? "Unknown (check whatismyip.com)" : externalIP)}");
        Debug.Log($"Discovery Port: {discoveryPort}, Game Port: {currentServerInfo.port}");
        Debug.Log("==== IMPORTANT ====");
        Debug.Log("For internet play, ensure ports 7777 and 47777 (UDP/TCP) are forwarded on your router");
        Debug.Log("====================================");
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
        // Don't start discovery if we're already in a game session
        if (IsServer || IsHost)
        {
            Debug.Log("MatchmakingManager: Won't start discovery while hosting a server");
            return;
        }
        
        // Stop any existing discovery process first
        StopServerDiscovery();
        
        // Clear the old server data to avoid showing stale entries
        discoveredServers.Clear();
        
        Debug.Log("MatchmakingManager: Starting server discovery process");
        
        // Start UDP discovery for both LAN and potential internet servers
        StartUdpDiscovery();
        
        // Start the coroutine for periodic server list refresh
        isRefreshingServers = true;
        serverDiscoveryCoroutine = StartCoroutine(RefreshServersCoroutine());
        
        // Force an immediate refresh
        RefreshServerList();
        
        // Display helpful networking information
        DisplayNetworkInfo();
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
        // Stop any existing discovery
        StopUdpDiscovery();
        
        try
        {
            // Create cancellation token for async operations
            cancellationTokenSource = new CancellationTokenSource();
            
            // Create and configure UDP client for listening
            try
            {
                discoveryClient = new UdpClient();
                
                // Configure to reuse address (important for multiple clients on same machine)
                discoveryClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                
                // Enable broadcast capability
                discoveryClient.EnableBroadcast = true;
                
                // Increase receive buffer size for better packet reception
                discoveryClient.Client.ReceiveBufferSize = 65536;
                
                // Bind to the discovery port
                discoveryClient.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
                
                Debug.Log($"MatchmakingManager: Successfully bound UDP client to port {discoveryPort}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MatchmakingManager: Error in primary binding, trying fallback: {ex.Message}");
                
                // Fallback: If binding fails, try to create directly with port
                if (discoveryClient != null)
                {
                    discoveryClient.Close();
                }
                
                discoveryClient = new UdpClient(discoveryPort);
                discoveryClient.EnableBroadcast = true;
            }
            
            // Start listening for broadcasts asynchronously
            isDiscoveryRunning = true;
            Debug.Log("MatchmakingManager: Starting to listen for server broadcasts");
            
            // Better way to run the async method with cancellation support
            _ = RunListenerWithErrorHandling(cancellationTokenSource.Token);
            
            // Try to send discovery packets to help with NAT traversal
            SendClientDiscoveryRequests();
            
            Debug.Log("MatchmakingManager: UDP discovery started successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"MatchmakingManager: Error starting UDP discovery: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Send discovery requests to help with NAT traversal
    /// </summary>
    private void SendClientDiscoveryRequests()
    {
        try
        {
            // Create a temporary UDP client for sending requests
            using (UdpClient requestClient = new UdpClient())
            {
                // Enable broadcast
                requestClient.EnableBroadcast = true;
                
                // Send a simple discovery request message
                byte[] requestData = Encoding.UTF8.GetBytes("DISCOVER_GAME_SERVER");
                
                // Try sending to broadcast
                requestClient.Send(requestData, requestData.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));
                
                // Try subnet broadcast
                string localIP = GetLocalIPv4();
                if (!string.IsNullOrEmpty(localIP) && localIP != "127.0.0.1")
                {
                    string[] parts = localIP.Split('.');
                    if (parts.Length == 4)
                    {
                        // Create subnet broadcast (e.g., 192.168.1.255)
                        string broadcastIP = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
                        requestClient.Send(requestData, requestData.Length, 
                            new IPEndPoint(IPAddress.Parse(broadcastIP), discoveryPort));
                            
                        // Try sending to several IPs in the subnet to improve discovery
                        for (int i = 1; i < 255; i += 25) // Send to IPs at intervals
                        {
                            if (i == int.Parse(parts[3])) continue; // Skip our own IP
                            
                            string targetIP = $"{parts[0]}.{parts[1]}.{parts[2]}.{i}";
                            try
                            {
                                requestClient.Send(requestData, requestData.Length, 
                                    new IPEndPoint(IPAddress.Parse(targetIP), discoveryPort));
                            }
                            catch
                            {
                                // Ignore individual send failures
                            }
                        }
                    }
                }
                
                // Try gateway
                string gateway = GetDefaultGatewayIP();
                if (!string.IsNullOrEmpty(gateway))
                {
                    requestClient.Send(requestData, requestData.Length, 
                        new IPEndPoint(IPAddress.Parse(gateway), discoveryPort));
                }
                
                Debug.Log("MatchmakingManager: Sent discovery requests to multiple targets");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MatchmakingManager: Error sending discovery requests: {ex.Message}");
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
        Debug.Log("MatchmakingManager: Starting to listen for broadcasts");
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Receive broadcast message with cancellation support
                UdpReceiveResult result;
                try 
                {
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
                        discoveredServers[serverKey] = Time.time;
                        
                        Debug.Log($"MatchmakingManager: Discovered server: {serverInfo.serverName} at {serverInfo.ipAddress}:{serverInfo.port}");
                    }
                }
                catch (Exception)
                {
                    // Not a valid server info JSON, that's okay (might be other network traffic)
                    // Don't log this to avoid console spam
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
            // Refresh the server list
            RefreshServerList();
            
            // Wait before the next refresh
            yield return new WaitForSeconds(serverRefreshRate);
        }
    }
    
    private void RefreshServerList()
    {
        Debug.Log("MatchmakingManager: Refreshing server list");
        
        // Clear the current list
        Debug.Log($"MatchmakingManager: Clearing server list (count before: {availableServers.Count})");
        availableServers.Clear();
        
        // Add any discovered servers from UDP broadcasts
        bool foundServers = AddDiscoveredUdpServers();
        
        // If network discovery hasn't found any servers, log a message
        if (!foundServers)
        {
            // This code can load saved servers from PlayerPrefs if implemented
            Debug.Log("MatchmakingManager: No discovered servers found");
        }
        
        // REMOVED: Do not add potential hosts automatically
        // This code was causing "phantom" server entries to appear
        /*
        // If we still don't have any servers, add potential hosts in the subnet as a fallback
        if (availableServers.Count == 0 && addPotentialHostsWhenEmpty)
        {
            // This is just for user experience - showing potential hosts the user might try to connect to
            string localIP = GetLocalIPv4();
            if (!string.IsNullOrEmpty(localIP))
            {
                string[] ipParts = localIP.Split('.');
                if (ipParts.Length == 4)
                {
                    string subnet = $"{ipParts[0]}.{ipParts[1]}.{ipParts[2]}";
                    
                    // Add a few potential servers in the local subnet
                    Debug.Log($"MatchmakingManager: Adding potential hosts in subnet {subnet}.x");
                    availableServers.Add(new ServerInfo("Potential Host", $"{subnet}.1", 7777, 0, maxPlayersPerServer, false));
                    
                    // Don't add too many potential hosts to avoid cluttering the UI
                    if (debugShowExtraHosts)
                    {
                        availableServers.Add(new ServerInfo("Potential Host", $"{subnet}.100", 7777, 0, maxPlayersPerServer, false));
                        availableServers.Add(new ServerInfo("Potential Host", $"{subnet}.254", 7777, 0, maxPlayersPerServer, false));
                    }
                }
            }
        }
        */
        
        // Log the results
        Debug.Log($"MatchmakingManager: Server list updated with {availableServers.Count} servers");
        if (availableServers.Count > 0)
        {
            foreach (var server in availableServers)
            {
                Debug.Log($"MatchmakingManager: Available server: {server.serverName} at {server.ipAddress}:{server.port} ({server.currentPlayers}/{server.maxPlayers})");
            }
        }
        
        // Notify listeners about the updated server list
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
        
        Debug.Log($"MatchmakingManager: Checking for discovered UDP servers. Count in dictionary: {discoveredServers.Count}");
        
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
                    
                    Debug.Log($"MatchmakingManager: Added discovered server at {ipAddress}:{port}");
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
        if (serverIndex < 0 || serverIndex >= availableServers.Count)
        {
            OnJoinServerFailed?.Invoke("Invalid server index");
            return;
        }
        
        ServerInfo serverToJoin = availableServers[serverIndex];
        JoinServer(serverToJoin);
    }
    
    /// <summary>
    /// Join a specific server by its info
    /// </summary>
    public void JoinServer(ServerInfo serverInfo)
    {
        Debug.Log($"MatchmakingManager: Attempting to join server: {serverInfo.serverName} at {serverInfo.ipAddress}:{serverInfo.port}");
        
        // Create a NetworkManager if it doesn't exist
        if (NetworkManager.Singleton == null)
        {
            Debug.Log("MatchmakingManager: Creating NetworkManager for client connection");
            GameObject networkManagerObject = new GameObject("NetworkManager");
            NetworkManager networkManager = networkManagerObject.AddComponent<NetworkManager>();
            UnityTransport transport = networkManagerObject.AddComponent<UnityTransport>();
            DontDestroyOnLoad(networkManagerObject);
            
            // Configure NetworkManager
            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }
            
            // Set the transport in the NetworkConfig
            networkManager.NetworkConfig.NetworkTransport = transport;
            
            networkManager.NetworkConfig.PlayerPrefab = null;
            networkManager.NetworkConfig.ConnectionApproval = true;
        }
        
        if (NetworkManager.Singleton == null)
        {
            string error = "Failed to create NetworkManager";
            Debug.LogError("MatchmakingManager: " + error);
            OnJoinServerFailed?.Invoke(error);
            return;
        }
        
        if (serverInfo.inGame)
        {
            string error = "Game already in progress";
            Debug.LogWarning("MatchmakingManager: " + error);
            OnJoinServerFailed?.Invoke(error);
            return;
        }
        
        if (serverInfo.currentPlayers >= serverInfo.maxPlayers)
        {
            string error = "Server is full";
            Debug.LogWarning("MatchmakingManager: " + error);
            OnJoinServerFailed?.Invoke(error);
            return;
        }
        
        try
        {
            // Ensure NetworkConfig is initialized
            if (NetworkManager.Singleton.NetworkConfig == null)
            {
                NetworkManager.Singleton.NetworkConfig = new NetworkConfig();
            }
            
            // Register event handlers if not already registered
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            
            // Configure the NetworkManager transport
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.Log("MatchmakingManager: Adding UnityTransport component");
                transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
            }
            
            // Set the transport in the NetworkConfig
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            
            Debug.Log($"MatchmakingManager: Setting transport address to {serverInfo.ipAddress}:{serverInfo.port}");
            transport.ConnectionData.Address = serverInfo.ipAddress;
            transport.ConnectionData.Port = serverInfo.port;
            
            // Start the client
            Debug.Log($"MatchmakingManager: Starting client connection to {serverInfo.ipAddress}:{serverInfo.port}");
            NetworkManager.Singleton.StartClient();
            
            Debug.Log($"MatchmakingManager: Client connection started to {serverInfo.serverName}");
        }
        catch (Exception ex)
        {
            string error = $"Error joining server: {ex.Message}";
            Debug.LogError("MatchmakingManager: " + error);
            OnJoinServerFailed?.Invoke(error);
        }
    }
    
    #endregion
    
    #region NetworkManager Callbacks
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"MatchmakingManager: Client connected with ID: {clientId}, Local client ID: {NetworkManager.Singleton.LocalClientId}");
        
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("MatchmakingManager: This is our local client that connected!");
            // Debug.Log("Connected as " + (NetworkManager.Singleton.IsHost ? "Host" : "Client"));
            
            // If we're the host/server, load the lobby scene
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                Debug.Log($"MatchmakingManager: Loading lobby scene: {lobbySceneName}");
                
                try
                {
                    // Check if SceneManager exists
                    if (NetworkManager.Singleton.SceneManager != null)
                    {
                        // Register scene loaded event handler
                        NetworkManager.Singleton.SceneManager.OnLoadComplete += OnSceneLoadComplete;
                        
                        // Check if scene exists without using SceneUtility
                        bool sceneExists = false;
                        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                        {
                            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                            if (sceneName == lobbySceneName)
                            {
                                sceneExists = true;
                                break;
                            }
                        }
                        
                        if (sceneExists)
                        {
                            Debug.Log($"MatchmakingManager: Found {lobbySceneName} in build settings, loading via NetworkManager.SceneManager");
                            try
                            {
                                NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"MatchmakingManager: Error loading scene through NetworkManager: {ex.Message}");
                                // Fall back to manual scene loading
                                SceneManager.LoadScene(lobbySceneName);
                            }
                        }
                        else
                        {
                            Debug.LogError($"MatchmakingManager: Lobby scene '{lobbySceneName}' does not exist in the build settings!");
                            // Fall back to manual scene loading
                            Debug.Log($"MatchmakingManager: Trying to load scene manually: {lobbySceneName}");
                            SceneManager.LoadScene(lobbySceneName);
                        }
                    }
                    else
                    {
                        Debug.LogError("MatchmakingManager: NetworkManager.SceneManager is null! Cannot load lobby scene through network. Trying manual load.");
                        // Try manual scene loading
                        SceneManager.LoadScene(lobbySceneName);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"MatchmakingManager: Error loading lobby scene: {ex.Message}\n{ex.StackTrace}");
                    // Last resort - try direct scene loading
                    try
                    {
                        SceneManager.LoadScene(lobbySceneName);
                    }
                    catch (Exception innerEx)
                    {
                        Debug.LogError($"MatchmakingManager: Critical error! Could not load lobby scene: {innerEx.Message}");
                    }
                }
            }
            
            // Local client connected successfully
            OnJoinServerSuccess?.Invoke();
        }
        else if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
        {
            Debug.Log($"MatchmakingManager: Remote client {clientId} connected to the server");
        }
        
        if (NetworkManager.Singleton.IsServer)
        {
            // Update player count
            UpdateServerPlayerCount(NetworkManager.Singleton.ConnectedClientsIds.Count);
        }
    }
    
    // Handler for scene load completion
    private void OnSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        Debug.Log($"MatchmakingManager: Scene '{sceneName}' loaded for client {clientId}");
        
        // If this is the lobby scene
        if (sceneName == lobbySceneName)
        {
            // Find and initialize the LobbyManager
            StartCoroutine(InitializeLobbyManagerAfterDelay());
        }
    }
    
    // Wait a brief moment for all scene objects to initialize before accessing LobbyManager
    private IEnumerator InitializeLobbyManagerAfterDelay()
    {
        // Small delay to ensure scene is fully initialized
        yield return new WaitForSeconds(0.2f);
        
        LobbyManager lobbyManager = FindObjectOfType<LobbyManager>();
        if (lobbyManager != null)
        {
            Debug.Log("MatchmakingManager: Found LobbyManager, initializing");
            
            // Check if it has a NetworkObject component
            NetworkObject networkObject = lobbyManager.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                // If not spawned, spawn it
                if (!networkObject.IsSpawned)
                {
                    Debug.Log("MatchmakingManager: Spawning LobbyManager's NetworkObject");
                    try
                    {
                        networkObject.Spawn();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"MatchmakingManager: Error spawning LobbyManager: {ex.Message}");
                    }
                }
                else
                {
                    Debug.Log("MatchmakingManager: LobbyManager's NetworkObject already spawned");
                }
            }
            else
            {
                Debug.LogError("MatchmakingManager: LobbyManager does not have a NetworkObject component!");
            }
        }
        else
        {
            Debug.LogError("MatchmakingManager: LobbyManager not found in the scene!");
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client disconnected: {clientId}");
        
        if (NetworkManager.Singleton.IsServer)
        {
            // Update player count
            UpdateServerPlayerCount(NetworkManager.Singleton.ConnectedClientsIds.Count);
        }
    }
    
    private void OnServerStarted()
    {
        Debug.Log("MatchmakingManager: Server started successfully");
        
        // Load the lobby scene now that the server has started
        Debug.Log("MatchmakingManager: Loading lobby scene through NetworkManager");
        
        try
        {
            // Make sure we have a lobby scene name defined
            string lobbySceneName = "Lobby";
            
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                // Check if the scene exists in the build settings
                bool sceneExists = false;
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                    string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                    if (sceneName == lobbySceneName)
                    {
                        sceneExists = true;
                        break;
                    }
                }
                
                if (sceneExists)
                {
                    Debug.Log($"MatchmakingManager: Found {lobbySceneName} in build settings, loading via NetworkManager");
                    NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
                }
                else
                {
                    Debug.LogError($"MatchmakingManager: Lobby scene '{lobbySceneName}' not found in build settings");
                }
            }
            else
            {
                Debug.LogError("MatchmakingManager: NetworkManager or SceneManager is null");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"MatchmakingManager: Error loading lobby scene: {ex.Message}\n{ex.StackTrace}");
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
                    Debug.Log("MatchmakingManager: Lobby scene loaded, initializing LobbyManager");
                    StartCoroutine(InitializeLobbyManagerAfterDelay());
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
    /// Stop listening for UDP broadcast messages
    /// </summary>
    private void StopUdpDiscovery()
    {
        if (isDiscoveryRunning)
        {
            // Cancel any async operations
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            }
            
            // Close the UDP client
            if (discoveryClient != null)
            {
                discoveryClient.Close();
                discoveryClient = null;
            }
            
            isDiscoveryRunning = false;
            Debug.Log("MatchmakingManager: Stopped UDP discovery");
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
}

// Helper extension methods for task cancellation
public static class TaskExtensions
{
    // For Task<T> (with return value)
    public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
                throw new OperationCanceledException(cancellationToken);
        }
        return await task;
    }
    
    // For Task (without return value)
    public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
                throw new OperationCanceledException(cancellationToken);
        }
        await task;
    }
}

