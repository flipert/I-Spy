using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using TMPro;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections;

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
    
    private void Awake()
    {
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
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
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
        NetworkManager.Singleton.StartHost();
        // Fade out the UI
        StartCoroutine(FadeOutUI());
    }
    
    private void OnClientButtonClicked()
    {
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
    }
    else if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
    {
        Debug.Log($"Remote client {clientId} connected to the server");
    }
}
    
    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            UpdateStatusText("Disconnected");
            
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