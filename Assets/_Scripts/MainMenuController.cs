using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class MainMenuController : MonoBehaviour
{
    [Header("Menu Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject hostPanel;
    [SerializeField] private GameObject clientPanel;
    [SerializeField] private GameObject settingsPanel;
    
    [Header("Main Menu Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    
    [Header("Host Panel")]
    [SerializeField] private TMP_InputField playerNameInputHost;
    [SerializeField] private Button startHostButton;
    [SerializeField] private Button backFromHostButton;
    [SerializeField] private TextMeshProUGUI ipAddressText;
    
    [Header("Client Panel")]
    [SerializeField] private TMP_InputField playerNameInputClient;
    [SerializeField] private TMP_InputField ipAddressInput;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button backFromClientButton;
    
    [Header("Settings Panel")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Button applySettingsButton;
    [SerializeField] private Button backFromSettingsButton;
    
    [Header("Network Reference")]
    [SerializeField] private NetworkManagerUI networkManagerUI;
    
    private void Start()
    {
        // Set up button listeners
        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostButtonClicked);
            
        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinButtonClicked);
            
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsButtonClicked);
            
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitButtonClicked);
            
        if (startHostButton != null)
            startHostButton.onClick.AddListener(OnStartHostButtonClicked);
            
        if (backFromHostButton != null)
            backFromHostButton.onClick.AddListener(OnBackFromHostButtonClicked);
            
        if (connectButton != null)
            connectButton.onClick.AddListener(OnConnectButtonClicked);
            
        if (backFromClientButton != null)
            backFromClientButton.onClick.AddListener(OnBackFromClientButtonClicked);
            
        if (applySettingsButton != null)
            applySettingsButton.onClick.AddListener(OnApplySettingsButtonClicked);
            
        if (backFromSettingsButton != null)
            backFromSettingsButton.onClick.AddListener(OnBackFromSettingsButtonClicked);
            
        // Show main panel by default
        ShowMainPanel();
        
        // Display local IP address if available
        ShowLocalIP();
    }
    
    // Panel visibility methods
    
    private void ShowMainPanel()
    {
        if (mainPanel != null)
            mainPanel.SetActive(true);
            
        if (hostPanel != null)
            hostPanel.SetActive(false);
            
        if (clientPanel != null)
            clientPanel.SetActive(false);
            
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }
    
    private void ShowHostPanel()
    {
        if (mainPanel != null)
            mainPanel.SetActive(false);
            
        if (hostPanel != null)
            hostPanel.SetActive(true);
            
        if (clientPanel != null)
            clientPanel.SetActive(false);
            
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }
    
    private void ShowClientPanel()
    {
        if (mainPanel != null)
            mainPanel.SetActive(false);
            
        if (hostPanel != null)
            hostPanel.SetActive(false);
            
        if (clientPanel != null)
            clientPanel.SetActive(true);
            
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }
    
    private void ShowSettingsPanel()
    {
        if (mainPanel != null)
            mainPanel.SetActive(false);
            
        if (hostPanel != null)
            hostPanel.SetActive(false);
            
        if (clientPanel != null)
            clientPanel.SetActive(false);
            
        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }
    
    // Button event handlers
    
    private void OnHostButtonClicked()
    {
        // Instead of directly starting the host, show the host panel for name entry
        ShowHostPanel();
        
        // If we have the IP text field, show the local IP
        ShowLocalIP();
    }
    
    private void OnJoinButtonClicked()
    {
        ShowClientPanel();
        
        // Default to localhost IP
        if (ipAddressInput != null && string.IsNullOrEmpty(ipAddressInput.text))
            ipAddressInput.text = "127.0.0.1";
    }
    
    private void OnSettingsButtonClicked()
    {
        ShowSettingsPanel();
        
        // Initialize settings values
        if (volumeSlider != null)
            volumeSlider.value = PlayerPrefs.GetFloat("MasterVolume", 1.0f);
            
        if (fullscreenToggle != null)
            fullscreenToggle.isOn = Screen.fullScreen;
    }
    
    private void OnQuitButtonClicked()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
    
    private void OnStartHostButtonClicked()
    {
        if (networkManagerUI == null)
        {
            Debug.LogError("NetworkManagerUI reference is missing!");
            return;
        }
        
        // Save player name
        string playerName = "Host";
        if (playerNameInputHost != null && !string.IsNullOrEmpty(playerNameInputHost.text))
            playerName = playerNameInputHost.text;
            
        PlayerPrefs.SetString("PlayerName", playerName);
        
        // Hide all panels - the NetworkManagerUI will handle showing the lobby
        HideAllPanels();
        
        // Start host using NetworkManagerUI
        networkManagerUI.OnHostButtonClicked();
    }
    
    private void OnBackFromHostButtonClicked()
    {
        ShowMainPanel();
    }
    
    private void OnConnectButtonClicked()
    {
        if (networkManagerUI == null)
        {
            Debug.LogError("NetworkManagerUI reference is missing!");
            return;
        }
        
        // Save player name
        string playerName = "Client";
        if (playerNameInputClient != null && !string.IsNullOrEmpty(playerNameInputClient.text))
            playerName = playerNameInputClient.text;
            
        PlayerPrefs.SetString("PlayerName", playerName);
        
        // Set IP address
        string ipAddress = "127.0.0.1";
        if (ipAddressInput != null && !string.IsNullOrEmpty(ipAddressInput.text))
            ipAddress = ipAddressInput.text;
            
        // Set the IP address directly on the NetworkManagerUI's ipInputField
        if (networkManagerUI.ipInputField != null)
        {
            networkManagerUI.ipInputField.text = ipAddress;
        }
        
        // Hide all panels - the NetworkManagerUI will handle showing the lobby
        HideAllPanels();
        
        // Start client
        networkManagerUI.OnClientButtonClicked();
    }
    
    private void OnBackFromClientButtonClicked()
    {
        ShowMainPanel();
    }
    
    private void OnApplySettingsButtonClicked()
    {
        // Apply volume settings
        if (volumeSlider != null)
        {
            float volume = volumeSlider.value;
            PlayerPrefs.SetFloat("MasterVolume", volume);
            AudioListener.volume = volume;
        }
        
        // Apply fullscreen setting
        if (fullscreenToggle != null)
        {
            Screen.fullScreen = fullscreenToggle.isOn;
        }
        
        PlayerPrefs.Save();
        
        // Return to main menu
        ShowMainPanel();
    }
    
    private void OnBackFromSettingsButtonClicked()
    {
        ShowMainPanel();
    }
    
    // Helper methods
    
    private void ShowLocalIP()
    {
        if (ipAddressText != null && networkManagerUI != null)
        {
            string localIP = NetworkManagerUI.GetLocalIPAddress();
            ipAddressText.text = "Your IP: " + localIP;
        }
    }
    
    // Helper method to hide all panels
    private void HideAllPanels()
    {
        if (mainPanel != null)
            mainPanel.SetActive(false);
            
        if (hostPanel != null)
            hostPanel.SetActive(false);
            
        if (clientPanel != null)
            clientPanel.SetActive(false);
            
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }
} 