using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// UI controller for the lobby screen
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject mainLobbyPanel;
    [SerializeField] private GameObject characterSelectionPanel;
    
    [Header("Lobby Info")]
    [SerializeField] private TextMeshProUGUI lobbyNameText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    
    [Header("Player List")]
    [SerializeField] private Transform playerListContent;
    [SerializeField] private GameObject playerEntryPrefab;
    
    [Header("Character Selection")]
    [SerializeField] private GameObject[] characterCards;
    [SerializeField] private Button[] characterSelectButtons;
    [SerializeField] private Color selectedColor = new Color(0.8f, 1f, 0.8f);
    [SerializeField] private Color unavailableColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);
    [SerializeField] private Color availableColor = Color.white;
    
    [Header("Start Game")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private TextMeshProUGUI countdownText;
    [SerializeField] private GameObject countdownPanel;
    
    // Reference to managers
    private LobbyManager lobbyManager;
    private MatchmakingManager matchmakingManager;
    
    // Track the UI objects for each player
    private Dictionary<ulong, GameObject> playerUIEntries = new Dictionary<ulong, GameObject>();
    
    // Track the local player's character selection
    private int selectedCharacterIndex = -1;
    
    private void Awake()
    {
        // Find managers
        lobbyManager = FindObjectOfType<LobbyManager>();
        matchmakingManager = FindObjectOfType<MatchmakingManager>();
        
        if (lobbyManager == null)
        {
            Debug.LogError("LobbyManager not found!");
        }
        
        if (matchmakingManager == null)
        {
            Debug.LogError("MatchmakingManager not found!");
        }
        
        // Setup UI
        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(OnStartGameClicked);
            
            // Only show start button for host
            startGameButton.gameObject.SetActive(NetworkManager.Singleton != null && 
                (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer));
        }
        
        // Setup character selection buttons
        SetupCharacterSelectionButtons();
        
        // Hide countdown panel initially
        if (countdownPanel != null)
            countdownPanel.SetActive(false);
            
        // Set initial panel visibility
        if (mainLobbyPanel != null)
            mainLobbyPanel.SetActive(true);
            
        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(true);
    }
    
    private void OnEnable()
    {
        if (lobbyManager != null)
        {
            // Subscribe to events
            lobbyManager.OnLobbyStateUpdated += OnLobbyStateUpdated;
            lobbyManager.OnPlayerCharacterSelected += OnPlayerCharacterSelected;
            lobbyManager.OnCountdownStarted += OnCountdownStarted;
            lobbyManager.OnCountdownCancelled += OnCountdownCancelled;
            lobbyManager.OnCountdownTick += OnCountdownTick;
            lobbyManager.OnGameStarting += OnGameStarting;
        }
    }
    
    private void OnDisable()
    {
        if (lobbyManager != null)
        {
            // Unsubscribe from events
            lobbyManager.OnLobbyStateUpdated -= OnLobbyStateUpdated;
            lobbyManager.OnPlayerCharacterSelected -= OnPlayerCharacterSelected;
            lobbyManager.OnCountdownStarted -= OnCountdownStarted;
            lobbyManager.OnCountdownCancelled -= OnCountdownCancelled;
            lobbyManager.OnCountdownTick -= OnCountdownTick;
            lobbyManager.OnGameStarting -= OnGameStarting;
        }
    }
    
    private void Start()
    {
        // Update UI if lobby manager is already initialized
        if (lobbyManager != null && lobbyManager.GetLobbyPlayers() != null)
        {
            OnLobbyStateUpdated(lobbyManager.GetLobbyPlayers());
        }
        
        // Set the lobby name if available
        if (lobbyNameText != null && matchmakingManager != null && matchmakingManager.CurrentServerInfo != null)
        {
            lobbyNameText.text = matchmakingManager.CurrentServerInfo.serverName;
        }
    }
    
    #region UI Initialization
    
    // Setup character selection buttons
    private void SetupCharacterSelectionButtons()
    {
        if (characterSelectButtons == null || characterSelectButtons.Length == 0)
            return;
            
        for (int i = 0; i < characterSelectButtons.Length; i++)
        {
            Button button = characterSelectButtons[i];
            if (button != null)
            {
                int characterIndex = i;
                button.onClick.AddListener(() => OnCharacterSelected(characterIndex));
            }
        }
    }
    
    #endregion
    
    #region Event Handlers
    
    // Handle lobby state update event
    private void OnLobbyStateUpdated(Dictionary<ulong, LobbyManager.PlayerInfo> players)
    {
        // Update player count
        if (playerCountText != null)
        {
            playerCountText.text = $"Players: {players.Count}";
        }
        
        // Update player list
        UpdatePlayerList(players);
        
        // Update character selection UI
        UpdateCharacterSelectionUI(players);
    }
    
    // Handle character selection event
    private void OnPlayerCharacterSelected(ulong clientId, int characterIndex)
    {
        // If this is the local player, update the selected character index
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            selectedCharacterIndex = characterIndex;
        }
        
        // Update the character selection UI
        UpdateCharacterSelectionUI(lobbyManager.GetLobbyPlayers());
    }
    
    // Handle countdown started event
    private void OnCountdownStarted(int duration)
    {
        if (countdownPanel != null)
            countdownPanel.SetActive(true);
            
        if (countdownText != null)
            countdownText.text = duration.ToString();
    }
    
    // Handle countdown cancelled event
    private void OnCountdownCancelled()
    {
        if (countdownPanel != null)
            countdownPanel.SetActive(false);
    }
    
    // Handle countdown tick event
    private void OnCountdownTick(int secondsRemaining)
    {
        if (countdownText != null)
            countdownText.text = secondsRemaining.ToString();
    }
    
    // Handle game starting event
    private void OnGameStarting()
    {
        // Show loading message or animation
        if (countdownText != null)
            countdownText.text = "Starting Game...";
    }
    
    #endregion
    
    #region UI Updates
    
    // Update the player list UI
    private void UpdatePlayerList(Dictionary<ulong, LobbyManager.PlayerInfo> players)
    {
        // Remove players who left
        List<ulong> playersToRemove = new List<ulong>();
        foreach (var entry in playerUIEntries)
        {
            if (!players.ContainsKey(entry.Key))
            {
                playersToRemove.Add(entry.Key);
            }
        }
        
        foreach (ulong clientId in playersToRemove)
        {
            Destroy(playerUIEntries[clientId]);
            playerUIEntries.Remove(clientId);
        }
        
        // Add or update players
        foreach (var playerEntry in players)
        {
            ulong clientId = playerEntry.Key;
            LobbyManager.PlayerInfo playerInfo = playerEntry.Value;
            
            if (playerUIEntries.ContainsKey(clientId))
            {
                // Update existing entry
                UpdatePlayerEntry(playerUIEntries[clientId], playerInfo);
            }
            else
            {
                // Create new entry
                CreatePlayerEntry(playerInfo);
            }
        }
    }
    
    // Create a new player entry in the UI
    private void CreatePlayerEntry(LobbyManager.PlayerInfo playerInfo)
    {
        if (playerEntryPrefab == null || playerListContent == null)
            return;
            
        GameObject entry = Instantiate(playerEntryPrefab, playerListContent);
        playerUIEntries[playerInfo.clientId] = entry;
        
        UpdatePlayerEntry(entry, playerInfo);
    }
    
    // Update an existing player entry in the UI
    private void UpdatePlayerEntry(GameObject entry, LobbyManager.PlayerInfo playerInfo)
    {
        // Get UI components
        TextMeshProUGUI playerNameText = entry.transform.Find("PlayerNameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI characterNameText = entry.transform.Find("CharacterNameText")?.GetComponent<TextMeshProUGUI>();
        Image readyStatusImage = entry.transform.Find("ReadyStatusIcon")?.GetComponent<Image>();
        
        // Update player name
        if (playerNameText != null)
        {
            playerNameText.text = playerInfo.playerName;
        }
        
        // Update character name
        if (characterNameText != null)
        {
            if (playerInfo.selectedCharacterIndex >= 0)
            {
                characterNameText.text = $"Character {playerInfo.selectedCharacterIndex + 1}";
            }
            else
            {
                characterNameText.text = "Selecting...";
            }
        }
        
        // Update ready status
        if (readyStatusImage != null)
        {
            readyStatusImage.color = playerInfo.isReady ? Color.green : Color.red;
        }
    }
    
    // Update the character selection UI
    private void UpdateCharacterSelectionUI(Dictionary<ulong, LobbyManager.PlayerInfo> players)
    {
        if (lobbyManager == null || characterCards == null)
            return;
            
        // For each character, check if it's selected by any player
        for (int i = 0; i < characterCards.Length; i++)
        {
            if (characterCards[i] == null)
                continue;
                
            bool isSelectedByCurrentPlayer = (selectedCharacterIndex == i);
            bool isSelectedByOtherPlayer = IsCharacterSelectedByOtherPlayer(i, players);
            
            // Update the character card UI
            UpdateCharacterCardUI(characterCards[i], isSelectedByCurrentPlayer, isSelectedByOtherPlayer);
            
            // Update button interactability
            if (i < characterSelectButtons.Length && characterSelectButtons[i] != null)
            {
                // Can only select if not selected by another player
                characterSelectButtons[i].interactable = !isSelectedByOtherPlayer;
            }
        }
    }
    
    // Update the UI for a character card
    private void UpdateCharacterCardUI(GameObject characterCard, bool isSelectedByCurrentPlayer, bool isSelectedByOtherPlayer)
    {
        // First get the card background image
        Image cardImage = characterCard.GetComponent<Image>();
        
        if (cardImage != null)
        {
            if (isSelectedByCurrentPlayer)
            {
                cardImage.color = selectedColor;
            }
            else if (isSelectedByOtherPlayer)
            {
                cardImage.color = unavailableColor;
            }
            else
            {
                cardImage.color = availableColor;
            }
        }
        
        // Update the selection indicator if any
        Transform selectionIndicator = characterCard.transform.Find("SelectionIndicator");
        if (selectionIndicator != null)
        {
            selectionIndicator.gameObject.SetActive(isSelectedByCurrentPlayer);
        }
        
        // Update the unavailable overlay if any
        Transform unavailableOverlay = characterCard.transform.Find("UnavailableOverlay");
        if (unavailableOverlay != null)
        {
            unavailableOverlay.gameObject.SetActive(isSelectedByOtherPlayer);
        }
    }
    
    #endregion
    
    #region UI Event Handlers
    
    // Handle character selection button click
    private void OnCharacterSelected(int characterIndex)
    {
        if (lobbyManager == null)
            return;
            
        // Check if character is already selected by another player
        if (IsCharacterSelectedByOtherPlayer(characterIndex, lobbyManager.GetLobbyPlayers()))
        {
            Debug.Log($"Character {characterIndex} is already selected by another player");
            return;
        }
        
        // Update selected character
        selectedCharacterIndex = characterIndex;
        
        // Notify lobby manager
        lobbyManager.SelectCharacter(characterIndex);
    }
    
    // Handle start game button click
    private void OnStartGameClicked()
    {
        if (lobbyManager == null)
        {
            Debug.LogError("LobbyUI: lobbyManager is null!");
            return;
        }
        
        Debug.Log("LobbyUI: Start Game button clicked");
        
        // Check if the LobbyManager is properly spawned on the network
        if (!lobbyManager.IsSpawned)
        {
            Debug.LogError("LobbyUI: LobbyManager is not spawned!");
            
            // Try to initialize the LobbyManager
            lobbyManager.InitializeNetworkObject();
            
            // Wait for initialization (in a real implementation, you might want to use a coroutine instead)
            if (!lobbyManager.IsSpawned)
            {
                Debug.LogError("LobbyUI: Failed to spawn LobbyManager, cannot start game countdown");
                return;
            }
        }
        
        // Request to start the game
        Debug.Log("LobbyUI: Calling StartGameCountdownServerRpc");
        lobbyManager.StartGameCountdownServerRpc();
    }
    
    #endregion
    
    #region Helper Methods
    
    // Check if a character is selected by another player
    private bool IsCharacterSelectedByOtherPlayer(int characterIndex, Dictionary<ulong, LobbyManager.PlayerInfo> players)
    {
        if (NetworkManager.Singleton == null)
            return false;
            
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        
        foreach (var player in players)
        {
            if (player.Key != localClientId && player.Value.selectedCharacterIndex == characterIndex)
            {
                return true;
            }
        }
        
        return false;
    }
    
    #endregion
}
