using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class LobbyController : NetworkBehaviour
{
    [Header("Lobby UI References")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TextMeshProUGUI lobbyTitleText;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private GameObject playerEntryPrefab;
    
    [Header("Character Selection")]
    [SerializeField] private Image[] characterImages;
    [SerializeField] private GameObject[] characterCheckmarks;
    [SerializeField] private Image[] characterDisabledOverlays; // Greyed out overlays
    
    [Header("Host Controls")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private TextMeshProUGUI countdownText;
    
    [Header("References")]
    [SerializeField] private NetworkManagerUI networkManagerUI;
    
    // Network variables 
    private NetworkVariable<int> hostSelectedCharacter = new NetworkVariable<int>(-1, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> clientSelectedCharacter = new NetworkVariable<int>(-1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> gameStarting = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> countdownValue = new NetworkVariable<float>(5.0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Local vars
    private Dictionary<ulong, int> playerCharacterSelections = new Dictionary<ulong, int>();
    private Dictionary<ulong, string> playerNames = new Dictionary<ulong, string>();
    private Dictionary<ulong, GameObject> playerEntries = new Dictionary<ulong, GameObject>();
    private int localSelectedCharacter = -1;
    private bool isCountdownActive = false;
    
    // Singleton pattern
    public static LobbyController Instance { get; private set; }
    
    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // Setup network variables callbacks
        hostSelectedCharacter.OnValueChanged += OnHostCharacterChanged;
        clientSelectedCharacter.OnValueChanged += OnClientCharacterChanged;
        gameStarting.OnValueChanged += OnGameStartingChanged;
        countdownValue.OnValueChanged += OnCountdownValueChanged;
    }
    
    private void Start()
    {
        // Set up button listeners
        for (int i = 0; i < characterImages.Length; i++)
        {
            int characterIndex = i; // Capture for lambda
            if (characterImages[i] != null)
            {
                Button btn = characterImages[i].GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => SelectCharacter(characterIndex));
                }
                else
                {
                    // Add button component if missing
                    btn = characterImages[i].gameObject.AddComponent<Button>();
                    btn.onClick.AddListener(() => SelectCharacter(characterIndex));
                }
            }
        }
        
        // Set up host controls
        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(OnStartGameClicked);
        }
        
        // Init UI
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(false);
        }
        
        // Hide all checkmarks and disabled overlays initially
        foreach (var checkmark in characterCheckmarks)
        {
            if (checkmark != null) checkmark.SetActive(false);
        }
        
        foreach (var overlay in characterDisabledOverlays)
        {
            if (overlay != null) overlay.gameObject.SetActive(false);
        }
        
        // Hide countdown initially
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }
        
        // Find NetworkManagerUI if not assigned
        if (networkManagerUI == null)
        {
            networkManagerUI = FindObjectOfType<NetworkManagerUI>();
        }
        
        // Register for network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }
    
    private void OnDestroy()
    {
        // Unregister network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    private void Update()
    {
        // If countdown is active, update it (server)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && isCountdownActive)
        {
            countdownValue.Value -= Time.deltaTime;
            
            if (countdownValue.Value <= 0)
            {
                countdownValue.Value = 0;
                isCountdownActive = false;
                StartGame();
            }
        }
    }
    
    // Network callbacks
    private void OnHostCharacterChanged(int previousValue, int newValue)
    {
        UpdateCharacterUI(0, newValue); // Host is usually client ID 0
    }
    
    private void OnClientCharacterChanged(int previousValue, int newValue)
    {
        // Find the client ID (other than host) that changed
        ulong clientId = 0;
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id != 0) // Not host
            {
                clientId = id;
                break;
            }
        }
        
        UpdateCharacterUI(clientId, newValue);
    }
    
    private void OnGameStartingChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            // Game is starting - activate countdown
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(true);
                countdownText.text = $"Starting in: {Mathf.CeilToInt(countdownValue.Value)}";
            }
        }
    }
    
    private void OnCountdownValueChanged(float previousValue, float newValue)
    {
        if (countdownText != null && countdownText.gameObject.activeInHierarchy)
        {
            countdownText.text = $"Starting in: {Mathf.CeilToInt(newValue)}";
        }
    }
    
    // Public methods
    
    public void ShowLobby()
    {
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
        }
        
        // Update UI based on connection type
        UpdateLobbyUI();
        
        // Update player list
        UpdatePlayerList();
    }
    
    public void HideLobby()
    {
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(false);
        }
    }
    
    public void AddPlayer(ulong clientId, string playerName)
    {
        // Update the name cache
        playerNames[clientId] = playerName;
        
        // Create or update UI entry
        Debug.Log($"AddPlayer called for client {clientId} with name: {playerName}");
        CreateOrUpdatePlayerEntry(clientId, playerName);
    }
    
    public void RemovePlayer(ulong clientId)
    {
        if (playerNames.ContainsKey(clientId))
        {
            playerNames.Remove(clientId);
        }
        
        if (playerCharacterSelections.ContainsKey(clientId))
        {
            playerCharacterSelections.Remove(clientId);
        }
        
        if (playerEntries.ContainsKey(clientId))
        {
            if (playerEntries[clientId] != null)
            {
                Destroy(playerEntries[clientId]);
            }
            playerEntries.Remove(clientId);
        }
        
        UpdatePlayerList();
        UpdateCharacterSelectionUI();
    }
    
    // Private methods
    
    private void UpdateLobbyUI()
    {
        if (NetworkManager.Singleton == null)
            return;
            
        bool isHost = NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;
        
        // Set lobby title
        if (lobbyTitleText != null)
        {
            lobbyTitleText.text = isHost ? "Lobby (Host)" : "Lobby";
        }
        
        // Show/hide host controls
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
        }
    }
    
    private void UpdatePlayerList()
    {
        if (playerListContent == null || playerEntryPrefab == null)
            return;
            
        Debug.Log($"UpdatePlayerList: Current player names: {string.Join(", ", playerNames.Values)}");
            
        // This was causing client not to see their own entry - we should keep all entries in playerNames
        // New version doesn't delete entries but only creates missing ones
        
        // Add entries for any players that don't have one yet
        foreach (var player in playerNames)
        {
            ulong clientId = player.Key;
            string playerName = player.Value;
            
            // Create or update entry
            CreateOrUpdatePlayerEntry(clientId, playerName);
        }
    }
    
    private void SelectCharacter(int characterIndex)
    {
        if (NetworkManager.Singleton == null)
            return;
            
        localSelectedCharacter = characterIndex;
        
        // Instead of directly modifying NetworkVariables, always use RPC for clients
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            // Host/server can directly set the value
            hostSelectedCharacter.Value = characterIndex;
            
            // Also update local caches
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            playerCharacterSelections[localClientId] = characterIndex;
        }
        else
        {
            // Clients should use ServerRpc instead
            SelectCharacterServerRpc(characterIndex);
        }
        
        // Update UI locally for immediate feedback
        UpdateCharacterSelectionUI();
        
        // Update network manager character selection if it exists
        if (networkManagerUI != null)
        {
            networkManagerUI.SelectCharacter(characterIndex);
        }
    }
    
    private void UpdateCharacterUI(ulong clientId, int characterIndex)
    {
        // Update player selection cache
        if (characterIndex >= 0)
        {
            playerCharacterSelections[clientId] = characterIndex;
        }
        else
        {
            if (playerCharacterSelections.ContainsKey(clientId))
            {
                playerCharacterSelections.Remove(clientId);
            }
        }
        
        // Update the player entry if it exists
        if (playerEntries.ContainsKey(clientId) && playerEntries[clientId] != null)
        {
            PlayerLobbyEntry entry = playerEntries[clientId].GetComponent<PlayerLobbyEntry>();
            if (entry != null)
            {
                entry.SetCharacterSelection(characterIndex);
            }
        }
        
        // Update character selection UI
        UpdateCharacterSelectionUI();
    }
    
    private void UpdateCharacterSelectionUI()
    {
        // Reset all checkmarks and disabled overlays
        for (int i = 0; i < characterCheckmarks.Length; i++)
        {
            if (characterCheckmarks[i] != null)
            {
                characterCheckmarks[i].SetActive(false);
            }
            
            if (characterDisabledOverlays[i] != null)
            {
                characterDisabledOverlays[i].gameObject.SetActive(false);
            }
        }
        
        // Set our local selection checkmark
        if (localSelectedCharacter >= 0 && localSelectedCharacter < characterCheckmarks.Length)
        {
            if (characterCheckmarks[localSelectedCharacter] != null)
            {
                characterCheckmarks[localSelectedCharacter].SetActive(true);
            }
        }
        
        // Disable/grey out characters selected by others
        foreach (var selection in playerCharacterSelections)
        {
            // Skip our own selection
            if (selection.Key == NetworkManager.Singleton.LocalClientId)
                continue;
                
            int charIndex = selection.Value;
            if (charIndex >= 0 && charIndex < characterDisabledOverlays.Length)
            {
                if (characterDisabledOverlays[charIndex] != null)
                {
                    characterDisabledOverlays[charIndex].gameObject.SetActive(true);
                }
            }
        }
    }
    
    private void OnStartGameClicked()
    {
        if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            return;
            
        // Start countdown
        isCountdownActive = true;
        gameStarting.Value = true;
        countdownValue.Value = 5.0f;
    }
    
    private void StartGame()
    {
        if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            return;
            
        // Start the game - call NetworkManagerUI's LoadGameSceneAfterDelay method
        if (networkManagerUI != null)
        {
            StartCoroutine(networkManagerUI.LoadGameSceneAfterDelay());
        }
        else
        {
            Debug.LogError("NetworkManagerUI reference is missing!");
            
            // Fallback: load game scene directly
            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.LoadScene("Game", UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
    }
    
    // RPCs for player synchronization
    [ClientRpc]
    public void UpdatePlayerNameClientRpc(ulong clientId, string playerName)
    {
        // Update the player name in our dictionary
        playerNames[clientId] = playerName;
        
        Debug.Log($"Received player name update for client {clientId}: {playerName}");
        
        // Update the UI
        UpdatePlayerList();
    }
    
    // RPC for client to request character selection
    [ServerRpc(RequireOwnership = false)]
    public void SelectCharacterServerRpc(int characterIndex, ServerRpcParams serverRpcParams = default)
    {
        var clientId = serverRpcParams.Receive.SenderClientId;
        playerCharacterSelections[clientId] = characterIndex;
        
        // Send the selection to all clients
        UpdateCharacterClientRpc(clientId, characterIndex);
    }
    
    [ClientRpc]
    public void UpdateCharacterClientRpc(ulong clientId, int characterIndex)
    {
        playerCharacterSelections[clientId] = characterIndex;
        UpdateCharacterUI(clientId, characterIndex);
    }
    
    // Network lifecycle methods
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (NetworkManager.Singleton.IsServer)
        {
            // Initialize network variables on server
            hostSelectedCharacter.Value = -1;
            clientSelectedCharacter.Value = -1;
            gameStarting.Value = false;
            countdownValue.Value = 5.0f;
        }
        
        // Show lobby automatically when joining
        ShowLobby();
        
        // Add ourselves to the player list with the name from PlayerPrefs
        string playerName = PlayerPrefs.GetString("PlayerName", "Player " + NetworkManager.Singleton.LocalClientId);
        Debug.Log($"Adding player to lobby with name: {playerName}");
        
        // Add ourselves to local cache first for immediate display
        AddPlayer(NetworkManager.Singleton.LocalClientId, playerName);
        
        // Inform others about ourselves based on connection type
        if (NetworkManager.Singleton.IsServer)
        {
            // If we're the server, we need to broadcast our own name to all clients (including self)
            UpdatePlayerNameClientRpc(NetworkManager.Singleton.LocalClientId, playerName);
            
            // Also, if we're the server, inform all existing clients about each other
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.Singleton.LocalClientId)
                {
                    // Get the client's name (if available)
                    string otherPlayerName = playerNames.ContainsKey(clientId) 
                        ? playerNames[clientId] 
                        : $"Player {clientId}";
                        
                    // Inform all clients about this player
                    UpdatePlayerNameClientRpc(clientId, otherPlayerName);
                }
            }
        }
        else
        {
            // If we're a client, immediately tell the server our name
            Debug.Log($"Client sending name to server: {playerName}");
            NotifyServerOfNameServerRpc(playerName);
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        // Hide lobby when disconnecting
        HideLobby();
        
        // Clear player list
        foreach (var entry in playerEntries.Values)
        {
            if (entry != null)
                Destroy(entry);
        }
        
        playerEntries.Clear();
        playerNames.Clear();
        playerCharacterSelections.Clear();
    }
    
    // Network event handlers
    private void OnClientConnected(ulong clientId)
    {
        // If we're the server, we need to inform the new client about existing players
        if (NetworkManager.Singleton.IsServer && clientId != NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log($"New client connected: {clientId}. Sending player info...");
            
            // Create a copy of the dictionary to avoid enumeration issues
            Dictionary<ulong, string> playerNamesCopy = new Dictionary<ulong, string>(playerNames);
            
            // When a new client connects, send them all existing player names
            foreach (var playerEntry in playerNamesCopy)
            {
                UpdatePlayerNameClientRpc(playerEntry.Key, playerEntry.Value);
            }
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        // Remove the disconnected player
        RemovePlayer(clientId);
    }
    
    // New ServerRpc for client to inform server of its name
    [ServerRpc(RequireOwnership = false)]
    public void NotifyServerOfNameServerRpc(string playerName, ServerRpcParams serverRpcParams = default)
    {
        var clientId = serverRpcParams.Receive.SenderClientId;
        Debug.Log($"Server received name from client {clientId}: {playerName}");
        
        // Update server's record of this client's name
        playerNames[clientId] = playerName;
        
        // Broadcast to all clients including the sender
        UpdatePlayerNameClientRpc(clientId, playerName);
    }
    
    // New helper method to create or update player entries directly
    private void CreateOrUpdatePlayerEntry(ulong clientId, string playerName)
    {
        if (playerListContent == null || playerEntryPrefab == null)
            return;
        
        // Check if we already have an entry for this player
        if (!playerEntries.ContainsKey(clientId) || playerEntries[clientId] == null)
        {
            // Create new entry
            GameObject entryObj = Instantiate(playerEntryPrefab, playerListContent);
            PlayerLobbyEntry entryComponent = entryObj.GetComponent<PlayerLobbyEntry>();
            
            if (entryComponent != null)
            {
                // Set player info immediately
                entryComponent.SetPlayerInfo(playerName, clientId, 
                    clientId == NetworkManager.Singleton.LocalClientId,
                    clientId == 0); // Host is typically client ID 0
                    
                // Set character selection if already selected
                if (playerCharacterSelections.ContainsKey(clientId))
                {
                    entryComponent.SetCharacterSelection(playerCharacterSelections[clientId]);
                }
            }
            
            playerEntries[clientId] = entryObj;
            Debug.Log($"Created new player entry for client {clientId} with name: {playerName}");
        }
        else
        {
            // Update existing entry
            PlayerLobbyEntry entryComponent = playerEntries[clientId].GetComponent<PlayerLobbyEntry>();
            if (entryComponent != null)
            {
                entryComponent.SetPlayerInfo(playerName, clientId, 
                    clientId == NetworkManager.Singleton.LocalClientId,
                    clientId == 0);
                    
                // Update character selection if already selected
                if (playerCharacterSelections.ContainsKey(clientId))
                {
                    entryComponent.SetCharacterSelection(playerCharacterSelections[clientId]);
                }
            }
            Debug.Log($"Updated existing player entry for client {clientId} with name: {playerName}");
        }
    }
} 