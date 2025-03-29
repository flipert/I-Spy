using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    [System.Serializable]
    public class UICanvas
    {
        public string name;
        public GameObject canvasObject;
        public bool showOnStart = false;
        public bool networkDependent = true;  // Whether this UI depends on network state
    }

    [Header("Canvas References")]
    public List<UICanvas> canvases = new List<UICanvas>();
    
    [Header("Network References")]
    [SerializeField] private NetworkManagerUI networkManagerUI;
    
    // Singleton instance
    public static UIManager Instance { get; private set; }
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Find NetworkManagerUI if not assigned
        if (networkManagerUI == null)
        {
            networkManagerUI = FindObjectOfType<NetworkManagerUI>();
        }
    }
    
    private void Start()
    {
        // Initialize canvas states
        foreach (var canvas in canvases)
        {
            if (canvas.canvasObject != null)
            {
                canvas.canvasObject.SetActive(canvas.showOnStart);
            }
        }
        
        // Subscribe to network events if there's a NetworkManager
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
    }
    
    // Network event handlers
    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("UIManager: Local client connected");
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("UIManager: Local client disconnected");
        }
    }
    
    private void OnServerStarted()
    {
        Debug.Log("UIManager: Server started");
    }
    
    // Public methods to show/hide canvases
    public void ShowCanvas(string canvasName)
    {
        UICanvas canvas = canvases.Find(c => c.name == canvasName);
        if (canvas != null && canvas.canvasObject != null)
        {
            canvas.canvasObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning($"UIManager: Canvas '{canvasName}' not found");
        }
    }
    
    public void HideCanvas(string canvasName)
    {
        UICanvas canvas = canvases.Find(c => c.name == canvasName);
        if (canvas != null && canvas.canvasObject != null)
        {
            canvas.canvasObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"UIManager: Canvas '{canvasName}' not found");
        }
    }
    
    public void ShowOnlyCanvas(string canvasName)
    {
        foreach (var canvas in canvases)
        {
            if (canvas.canvasObject != null)
            {
                bool shouldShow = canvas.name == canvasName;
                canvas.canvasObject.SetActive(shouldShow);
            }
        }
    }
    
    // UI state transitions based on game state
    public void ShowMainMenu()
    {
        ShowOnlyCanvas("MainMenu");
    }
    
    public void ShowHostMenu()
    {
        ShowOnlyCanvas("HostMenu");
    }
    
    public void ShowClientMenu()
    {
        ShowOnlyCanvas("ClientMenu");
    }
    
    public void ShowLobby()
    {
        ShowOnlyCanvas("Lobby");
    }
    
    public void ShowInGameUI()
    {
        ShowOnlyCanvas("GameUI");
    }
    
    public void ShowPauseMenu()
    {
        ShowCanvas("PauseMenu");
    }
    
    public void HidePauseMenu()
    {
        HideCanvas("PauseMenu");
    }
} 