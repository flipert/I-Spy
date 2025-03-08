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
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private TMP_InputField ipInputField;
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
    
    // Singleton pattern
    public static NetworkManagerUI Instance { get; private set; }
    
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
        
        // Make sure the NetworkManager persists between scenes
        if (NetworkManager.Singleton != null)
        {
            DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
            
            // Configure NetworkManager for scene management
            // Disable auto-spawn for now - PlayerSpawner will handle it
            NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
        }
        
        // Set up button listeners
        hostButton.onClick.AddListener(OnHostButtonClicked);
        clientButton.onClick.AddListener(OnClientButtonClicked);
        
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
            Debug.LogError("NetworkManager.Singleton is null. Make sure there is a NetworkManager in the scene.");
        }
    }
    
    private void Start()
    {
        // This UI should persist if we're in the main menu
        if (SceneManager.GetActiveScene().name != gameSceneName)
        {
            DontDestroyOnLoad(gameObject);
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
        }
    }
    
    private void OnHostButtonClicked()
    {
        // Start as host (both server and client)
        if (NetworkManager.Singleton != null)
        {
            // We'll use SceneManagement to handle player spawning after scene load
            NetworkManager.Singleton.StartHost();
            // Fade out the UI
            StartCoroutine(FadeOutUI());
            
            // Load the game scene directly after a short delay
            StartCoroutine(LoadGameSceneAfterDelay());
        }
    }
    
    private void OnClientButtonClicked()
    {
        if (NetworkManager.Singleton == null) return;
        
        // We'll use SceneManagement to handle player spawning after scene load
        
        // Get IP from input field
        string ipAddress = ipInputField != null ? ipInputField.text : defaultIP;
        if (string.IsNullOrEmpty(ipAddress))
        {
            ipAddress = defaultIP;
        }
        
        // Set the connection data
        NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().ConnectionData.Address = ipAddress;
        NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().ConnectionData.Port = defaultPort;
        
        // Start as client
        NetworkManager.Singleton.StartClient();
        UpdateStatusText("Connecting to " + ipAddress + "...");
        
        // Fade out the UI
        StartCoroutine(FadeOutUI());
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
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected with ID: {clientId}, Local client ID: {NetworkManager.Singleton.LocalClientId}");
        
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("This is our local client that connected!");
            UpdateStatusText("Connected as " + (NetworkManager.Singleton.IsHost ? "Host" : "Client"));
            
            // If we're a client (not host), load the game scene after connection
            if (!NetworkManager.Singleton.IsHost)
            {
                StartCoroutine(LoadGameSceneAfterDelay());
            }
        }
        else if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
        {
            Debug.Log($"Remote client {clientId} connected to the server");
        }
    }
    
    private IEnumerator LoadGameSceneAfterDelay()
    {
        yield return new WaitForSeconds(sceneLoadDelay);
        
        Debug.Log($"Loading game scene: {gameSceneName}");
        
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            // Register a callback for when the scene is loaded to setup camera
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnGameSceneLoaded;
            
            // If we're the host/server, use NetworkManager to switch scenes
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
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
} 