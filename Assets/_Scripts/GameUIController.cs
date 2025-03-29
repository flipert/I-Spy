using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections;

public class GameUIController : MonoBehaviour
{
    [Header("HUD Elements")]
    [SerializeField] private GameObject hudContainer;
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI connectionStatusText;
    [SerializeField] private GameObject pingIndicator;
    
    [Header("Game Messages")]
    [SerializeField] private GameObject messagePanel;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private float messageDuration = 3f;
    
    [Header("Player List")]
    [SerializeField] private GameObject playerListPanel;
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private GameObject playerEntryPrefab;
    [SerializeField] private KeyCode playerListKey = KeyCode.Tab;
    
    [Header("Pause Menu")]
    [SerializeField] private GameObject pauseMenuPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;
    
    // Private variables
    private bool isPaused = false;
    private Coroutine messageCoroutine;
    
    private void Start()
    {
        // Set up button listeners
        if (resumeButton != null)
            resumeButton.onClick.AddListener(OnResumeClicked);
        
        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnectClicked);
        
        // Initialize UI states
        if (hudContainer != null)
            hudContainer.SetActive(true);
        
        if (messagePanel != null)
            messagePanel.SetActive(false);
        
        if (playerListPanel != null)
            playerListPanel.SetActive(false);
        
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(false);
            
        // Update connection status
        UpdateConnectionStatus();
    }
    
    private void Update()
    {
        // Toggle player list with Tab key
        if (Input.GetKeyDown(playerListKey))
        {
            if (playerListPanel != null)
                playerListPanel.SetActive(true);
        }
        
        if (Input.GetKeyUp(playerListKey))
        {
            if (playerListPanel != null)
                playerListPanel.SetActive(false);
        }
        
        // Toggle pause menu with Escape key
        if (Input.GetKeyDown(pauseKey))
        {
            TogglePauseMenu();
        }
    }
    
    // Public methods for external calls
    
    public void SetPlayerName(string name)
    {
        if (playerNameText != null)
            playerNameText.text = name;
    }
    
    public void SetScore(int score)
    {
        if (scoreText != null)
            scoreText.text = "Score: " + score;
    }
    
    public void ShowMessage(string message, float duration = -1)
    {
        if (messagePanel == null || messageText == null)
            return;
            
        // Cancel any existing message coroutine
        if (messageCoroutine != null)
            StopCoroutine(messageCoroutine);
            
        // Set the message
        messageText.text = message;
        messagePanel.SetActive(true);
        
        // Start the timer to hide the message
        float actualDuration = duration > 0 ? duration : messageDuration;
        messageCoroutine = StartCoroutine(HideMessageAfterDelay(actualDuration));
    }
    
    public void UpdatePlayerList(NetworkManager networkManager)
    {
        if (playerListContainer == null || playerEntryPrefab == null)
            return;
            
        // Clear existing entries
        foreach (Transform child in playerListContainer)
        {
            Destroy(child.gameObject);
        }
        
        // Add an entry for each connected client
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            GameObject entryObj = Instantiate(playerEntryPrefab, playerListContainer);
            PlayerListEntry entry = entryObj.GetComponent<PlayerListEntry>();
            
            if (entry != null)
            {
                // Set the player info in the entry
                // You'll need to get the player's name from somewhere (maybe a NetworkVariable in the Player script)
                string playerName = "Player " + clientId;
                bool isLocal = clientId == networkManager.LocalClientId;
                bool isHost = clientId == 0; // Host is typically client ID 0
                
                entry.SetPlayerInfo(playerName, clientId, isLocal, isHost);
            }
        }
    }
    
    public void UpdateConnectionStatus()
    {
        if (connectionStatusText == null)
            return;
            
        if (NetworkManager.Singleton == null)
        {
            connectionStatusText.text = "Not Connected";
            return;
        }
        
        if (NetworkManager.Singleton.IsHost)
            connectionStatusText.text = "Host";
        else if (NetworkManager.Singleton.IsServer)
            connectionStatusText.text = "Server";
        else if (NetworkManager.Singleton.IsClient)
            connectionStatusText.text = "Client";
        else
            connectionStatusText.text = "Not Connected";
    }
    
    // Private methods
    
    private IEnumerator HideMessageAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (messagePanel != null)
            messagePanel.SetActive(false);
            
        messageCoroutine = null;
    }
    
    private void TogglePauseMenu()
    {
        isPaused = !isPaused;
        
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(isPaused);
            
        // Optional: Pause the game
        Time.timeScale = isPaused ? 0f : 1f;
    }
    
    private void OnResumeClicked()
    {
        isPaused = false;
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(false);
            
        Time.timeScale = 1f;
    }
    
    private void OnDisconnectClicked()
    {
        // Resume time scale before disconnecting
        Time.timeScale = 1f;
        
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                NetworkManager.Singleton.Shutdown();
            else if (NetworkManager.Singleton.IsClient)
                NetworkManager.Singleton.Shutdown();
                
            // The NetworkManager should handle returning to the main menu
        }
    }
} 