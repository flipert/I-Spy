using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the lobby state, character selection, and game start countdown
/// </summary>
public class LobbyManager : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private int minPlayersToStart = 4;
    [SerializeField] private int countdownDuration = 10;
    [SerializeField] private float characterSelectionTimeLimit = 20f;
    
    [Header("Character Settings")]
    [SerializeField] private GameObject[] availableCharacterPrefabs;
    
    // Singleton instance
    public static LobbyManager Instance { get; private set; }
    
    // Events
    public event Action<int> OnCountdownStarted;
    public event Action OnCountdownCancelled;
    public event Action<int> OnCountdownTick;
    public event Action OnGameStarting;
    public event Action<Dictionary<ulong, PlayerInfo>> OnLobbyStateUpdated;
    public event Action<ulong, int> OnPlayerCharacterSelected;
    
    // Network variables
    private NetworkVariable<int> countdownTime = new NetworkVariable<int>(-1);
    private NetworkVariable<bool> isCountdownActive = new NetworkVariable<bool>(false);
    
    // Player info structure
    [System.Serializable]
    public class PlayerInfo : INetworkSerializable
    {
        public ulong clientId;
        public string playerName;
        public int selectedCharacterIndex = -1;
        public bool isReady;
        
        public PlayerInfo() { }
        
        public PlayerInfo(ulong id, string name)
        {
            clientId = id;
            playerName = name;
            selectedCharacterIndex = -1;
            isReady = false;
        }
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref selectedCharacterIndex);
            serializer.SerializeValue(ref isReady);
        }
    }
    
    // Serializable wrapper for dictionary to be sent over the network
    public struct LobbyState : INetworkSerializable
    {
        public PlayerInfo[] players;
        
        // Convert from dictionary to serializable array
        public LobbyState(Dictionary<ulong, PlayerInfo> playerDict)
        {
            if (playerDict == null || playerDict.Count == 0)
            {
                players = new PlayerInfo[0];
                return;
            }
            
            players = new PlayerInfo[playerDict.Count];
            int index = 0;
            
            foreach (var pair in playerDict)
            {
                // Create a copy of the PlayerInfo to ensure it's properly initialized
                players[index] = new PlayerInfo();
                
                // Manually copy values to ensure proper initialization
                if (pair.Value != null)
                {
                    players[index].clientId = pair.Value.clientId;
                    players[index].playerName = pair.Value.playerName != null ? pair.Value.playerName : "Player";
                    players[index].selectedCharacterIndex = pair.Value.selectedCharacterIndex;
                    players[index].isReady = pair.Value.isReady;
                }
                else
                {
                    // Create a default player if the source is null
                    players[index].clientId = pair.Key;
                    players[index].playerName = "Player " + pair.Key;
                    players[index].selectedCharacterIndex = -1;
                    players[index].isReady = false;
                }
                
                index++;
            }
        }
        
        // Convert back to dictionary
        public Dictionary<ulong, PlayerInfo> ToDictionary()
        {
            Dictionary<ulong, PlayerInfo> result = new Dictionary<ulong, PlayerInfo>();
            
            if (players == null)
                return result;
                
            foreach (var player in players)
            {
                if (player == null)
                {
                    Debug.LogWarning("LobbyState.ToDictionary: Found null player in players array");
                    continue;
                }
                
                // Create a copy to ensure it's properly initialized
                PlayerInfo newPlayer = new PlayerInfo();
                newPlayer.clientId = player.clientId;
                newPlayer.playerName = player.playerName != null ? player.playerName : "Player";
                newPlayer.selectedCharacterIndex = player.selectedCharacterIndex;
                newPlayer.isReady = player.isReady;
                
                result[player.clientId] = newPlayer;
            }
            
            return result;
        }
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            // Serialize the length first
            int length = players != null ? players.Length : 0;
            serializer.SerializeValue(ref length);
            
            // If reading, initialize the array
            if (serializer.IsReader)
            {
                players = new PlayerInfo[length];
                // Initialize each PlayerInfo object to avoid null references
                for (int i = 0; i < length; i++)
                {
                    players[i] = new PlayerInfo();
                }
            }
            
            // Serialize each player
            for (int i = 0; i < length; i++)
            {
                if (serializer.IsWriter && players[i] == null)
                {
                    // If we're writing and the player is null, create a default one
                    players[i] = new PlayerInfo();
                }
                serializer.SerializeValue(ref players[i]);
            }
        }
    }
    
    // Dictionary to store player info by client ID
    private Dictionary<ulong, PlayerInfo> lobbyPlayers = new Dictionary<ulong, PlayerInfo>();
    
    // Dictionary to track which character indexes are already selected
    private HashSet<int> selectedCharacterIndexes = new HashSet<int>();
    
    // Coroutines
    private Coroutine countdownCoroutine;
    private Coroutine characterSelectionTimeoutCoroutine;
    
    private void Awake()
    {
        // Singleton setup
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
        
        // Make sure we have a NetworkObject component
        if (GetComponent<NetworkObject>() == null)
        {
            Debug.LogError("LobbyManager requires a NetworkObject component!");
            gameObject.AddComponent<NetworkObject>();
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        Debug.Log($"LobbyManager: OnNetworkSpawn called, IsServer: {IsServer}, IsHost: {IsHost}, IsClient: {IsClient}");
        
        // Initialize network variables
        countdownTime.OnValueChanged += OnCountdownTimeChanged;
        isCountdownActive.OnValueChanged += OnCountdownActiveChanged;
        
        // If we're the server, set up event listeners
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            
            // Initialize the server state
            Debug.Log("LobbyManager: Server initialized");
        }
        
        // If we're a client, register ourselves with the server
        if (IsClient && !IsServer)
        {
            try
            {
                Debug.Log($"LobbyManager: Client registering with server, LocalClientId: {NetworkManager.Singleton.LocalClientId}");
                RegisterPlayerServerRpc(NetworkManager.Singleton.LocalClientId, "Player " + NetworkManager.Singleton.LocalClientId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"LobbyManager: Error registering player with server: {ex.Message}");
            }
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        // Unregister event listeners
        countdownTime.OnValueChanged -= OnCountdownTimeChanged;
        isCountdownActive.OnValueChanged -= OnCountdownActiveChanged;
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    #region Server-Side Methods
    
    // Handle a client connecting to the lobby
    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer)
            return;
            
        Debug.Log($"Client connected to lobby: {clientId}");
        
        try 
        {
            // Add player to the lobby
            AddPlayerToLobby(clientId, "Player " + clientId);
            
            // Update the matchmaking manager player count
            if (MatchmakingManager.Instance != null)
            {
                MatchmakingManager.Instance.UpdateServerPlayerCount(lobbyPlayers.Count);
            }
            
            // Create a safe copy of the lobby players dictionary
            Dictionary<ulong, PlayerInfo> safePlayersCopy = new Dictionary<ulong, PlayerInfo>();
            foreach (var kvp in lobbyPlayers)
            {
                // Create a new PlayerInfo to ensure it's properly initialized
                PlayerInfo newPlayerInfo = new PlayerInfo(kvp.Key, kvp.Value != null ? kvp.Value.playerName : "Player " + kvp.Key);
                if (kvp.Value != null)
                {
                    // Copy other properties if available
                    newPlayerInfo.selectedCharacterIndex = kvp.Value.selectedCharacterIndex;
                    newPlayerInfo.isReady = kvp.Value.isReady;
                }
                safePlayersCopy[kvp.Key] = newPlayerInfo;
            }
            
            // Create a LobbyState with the safe copy
            LobbyState lobbyState = new LobbyState(safePlayersCopy);
            
            // Update all clients with the new lobby state
            UpdateLobbyStateClientRpc(lobbyState);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in OnClientConnected: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    // Handle a client disconnecting from the lobby
    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer)
            return;
            
        Debug.Log($"Client disconnected from lobby: {clientId}");
        
        // If the player was in the lobby, remove them
        RemovePlayerFromLobby(clientId);
        
        // Update the matchmaking manager player count
        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.UpdateServerPlayerCount(lobbyPlayers.Count);
        }
        
        // If a countdown was active and we now have too few players, cancel it
        if (isCountdownActive.Value && lobbyPlayers.Count < minPlayersToStart)
        {
            CancelCountdown();
        }
        
        // Update all clients with the new lobby state
        UpdateLobbyStateClientRpc(new LobbyState(lobbyPlayers));
    }
    
    // Add a player to the lobby
    private void AddPlayerToLobby(ulong clientId, string playerName)
    {
        if (lobbyPlayers.ContainsKey(clientId))
            return;
            
        // Create new player info
        PlayerInfo playerInfo = new PlayerInfo(clientId, playerName);
        
        // Add to dictionary
        lobbyPlayers[clientId] = playerInfo;
        
        // Start character selection timeout
        if (characterSelectionTimeoutCoroutine != null)
            StopCoroutine(characterSelectionTimeoutCoroutine);
            
        characterSelectionTimeoutCoroutine = StartCoroutine(CharacterSelectionTimeoutCoroutine());
    }
    
    // Remove a player from the lobby
    private void RemovePlayerFromLobby(ulong clientId)
    {
        if (!lobbyPlayers.ContainsKey(clientId))
            return;
            
        // Get the player's selected character index
        int characterIndex = lobbyPlayers[clientId].selectedCharacterIndex;
        
        // If they had selected a character, make it available again
        if (characterIndex >= 0)
        {
            selectedCharacterIndexes.Remove(characterIndex);
        }
        
        // Remove from dictionary
        lobbyPlayers.Remove(clientId);
    }
    
    // Update the player's selected character
    private void UpdatePlayerCharacter(ulong clientId, int characterIndex)
    {
        if (!lobbyPlayers.ContainsKey(clientId))
            return;
            
        // If the player had already selected a character, make it available again
        int oldCharacterIndex = lobbyPlayers[clientId].selectedCharacterIndex;
        if (oldCharacterIndex >= 0)
        {
            selectedCharacterIndexes.Remove(oldCharacterIndex);
        }
        
        // Update the player's selected character
        lobbyPlayers[clientId].selectedCharacterIndex = characterIndex;
        
        // Mark the new character as selected
        if (characterIndex >= 0)
        {
            selectedCharacterIndexes.Add(characterIndex);
        }
        
        // Notify clients about the character selection
        PlayerCharacterSelectedClientRpc(clientId, characterIndex);
    }
    
    // Start the countdown to game start
    private void StartCountdown()
    {
        if (!IsServer)
            return;
            
        if (lobbyPlayers.Count < minPlayersToStart)
        {
            Debug.Log($"Cannot start countdown: Not enough players. Have {lobbyPlayers.Count}, need {minPlayersToStart}");
            return;
        }
        
        if (isCountdownActive.Value)
        {
            Debug.Log("Countdown already active");
            return;
        }
        
        Debug.Log("Starting countdown to game start");
        
        // Set network variables
        isCountdownActive.Value = true;
        countdownTime.Value = countdownDuration;
        
        // Start the countdown coroutine
        if (countdownCoroutine != null)
            StopCoroutine(countdownCoroutine);
            
        countdownCoroutine = StartCoroutine(CountdownCoroutine());
    }
    
    // Cancel the countdown
    private void CancelCountdown()
    {
        if (!IsServer)
            return;
            
        if (!isCountdownActive.Value)
            return;
            
        Debug.Log("Cancelling countdown");
        
        // Set network variables
        isCountdownActive.Value = false;
        countdownTime.Value = -1;
        
        // Stop the countdown coroutine
        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }
    }
    
    // Countdown coroutine
    private IEnumerator CountdownCoroutine()
    {
        while (countdownTime.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            countdownTime.Value--;
        }
        
        if (countdownTime.Value == 0)
        {
            AssignRandomCharactersToUnassignedPlayers();
            StartGame();
        }
        
        countdownCoroutine = null;
    }
    
    // Character selection timeout coroutine
    private IEnumerator CharacterSelectionTimeoutCoroutine()
    {
        // Wait for the character selection time limit
        yield return new WaitForSeconds(characterSelectionTimeLimit);
        
        // Check if we have enough players to start
        if (lobbyPlayers.Count >= minPlayersToStart && !isCountdownActive.Value)
        {
            StartCountdown();
        }
        
        characterSelectionTimeoutCoroutine = null;
    }
    
    // Assign random characters to players who haven't selected one
    private void AssignRandomCharactersToUnassignedPlayers()
    {
        if (!IsServer)
            return;
            
        List<int> availableCharacterIndexes = new List<int>();
        
        // Build list of available character indexes
        for (int i = 0; i < availableCharacterPrefabs.Length; i++)
        {
            if (!selectedCharacterIndexes.Contains(i))
            {
                availableCharacterIndexes.Add(i);
            }
        }
        
        // Shuffle the available indexes
        ShuffleList(availableCharacterIndexes);
        
        // Assign random characters to players who haven't selected one
        int availableIndex = 0;
        foreach (var playerEntry in lobbyPlayers)
        {
            ulong clientId = playerEntry.Key;
            PlayerInfo playerInfo = playerEntry.Value;
            
            if (playerInfo.selectedCharacterIndex < 0)
            {
                if (availableIndex < availableCharacterIndexes.Count)
                {
                    int randomCharacterIndex = availableCharacterIndexes[availableIndex++];
                    UpdatePlayerCharacter(clientId, randomCharacterIndex);
                }
                else
                {
                    // If we run out of available characters, just assign the first one (duplicate)
                    // In a real game, you might want to handle this differently
                    UpdatePlayerCharacter(clientId, 0);
                }
            }
        }
    }
    
    // Start the game
    private void StartGame()
    {
        if (!IsServer)
            return;
            
        Debug.Log("Starting the game!");
        
        // Notify clients that the game is starting
        GameStartingClientRpc();
        
        // Update the matchmaking manager to show we're in-game
        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.SetServerInGame(true);
        }
        
        // Load the game scene
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }
    
    #endregion
    
    #region Client-Side Methods
    
    // Handle countdown time change
    private void OnCountdownTimeChanged(int oldValue, int newValue)
    {
        Debug.Log($"Countdown time changed: {oldValue} -> {newValue}");
        
        if (newValue >= 0)
        {
            OnCountdownTick?.Invoke(newValue);
        }
    }
    
    // Handle countdown active state change
    private void OnCountdownActiveChanged(bool oldValue, bool newValue)
    {
        Debug.Log($"Countdown active changed: {oldValue} -> {newValue}");
        
        if (newValue)
        {
            OnCountdownStarted?.Invoke(countdownTime.Value);
        }
        else
        {
            OnCountdownCancelled?.Invoke();
        }
    }
    
    #endregion
    
    #region Public Methods
    
    // Get the current lobby state
    public Dictionary<ulong, PlayerInfo> GetLobbyPlayers()
    {
        return new Dictionary<ulong, PlayerInfo>(lobbyPlayers);
    }
    
    // Check if a character is already selected
    public bool IsCharacterSelected(int characterIndex)
    {
        return selectedCharacterIndexes.Contains(characterIndex);
    }
    
    // Client method to select a character
    public void SelectCharacter(int characterIndex)
    {
        if (!IsClient)
            return;
            
        if (characterIndex < 0 || characterIndex >= availableCharacterPrefabs.Length)
        {
            Debug.LogError($"Invalid character index: {characterIndex}");
            return;
        }
        
        // Request character selection from the server
        SelectCharacterServerRpc(NetworkManager.Singleton.LocalClientId, characterIndex);
    }
    
    // Host method to start the game countdown
    public void StartGameCountdown()
    {
        if (!IsServer)
            return;
            
        StartCountdown();
    }
    
    #endregion
    
    #region RPCs
    
    // Register player with the server
    [ServerRpc(RequireOwnership = false)]
    public void RegisterPlayerServerRpc(ulong clientId, string playerName)
    {
        if (!IsServer)
            return;
            
        Debug.Log($"RegisterPlayerServerRpc: {clientId}, {playerName}");
        
        // Add the player to the lobby
        AddPlayerToLobby(clientId, playerName);
        
        // Update all clients with the new lobby state
        UpdateLobbyStateClientRpc(new LobbyState(lobbyPlayers));
    }
    
    // Request character selection
    [ServerRpc(RequireOwnership = false)]
    public void SelectCharacterServerRpc(ulong clientId, int characterIndex)
    {
        if (!IsServer)
            return;
            
        Debug.Log($"SelectCharacterServerRpc: {clientId}, {characterIndex}");
        
        // Validate character selection
        if (characterIndex < 0 || characterIndex >= availableCharacterPrefabs.Length)
        {
            Debug.LogError($"Invalid character index: {characterIndex}");
            return;
        }
        
        // Check if the character is already selected
        if (selectedCharacterIndexes.Contains(characterIndex) && 
            !(lobbyPlayers.ContainsKey(clientId) && lobbyPlayers[clientId].selectedCharacterIndex == characterIndex))
        {
            Debug.Log($"Character {characterIndex} is already selected");
            return;
        }
        
        // Update the player's character
        UpdatePlayerCharacter(clientId, characterIndex);
        
        // Update all clients with the new lobby state
        UpdateLobbyStateClientRpc(new LobbyState(lobbyPlayers));
    }
    
    // RPC to update lobby state on all clients
    [ClientRpc]
    private void UpdateLobbyStateClientRpc(LobbyState state)
    {
        try 
        {
            Debug.Log($"UpdateLobbyStateClientRpc received");
            
            // Check if the state is valid
            if (state.players == null)
            {
                Debug.LogWarning("UpdateLobbyStateClientRpc: Received null players array");
                state.players = new PlayerInfo[0];
            }
            
            // Convert the serializable array back to a dictionary
            Dictionary<ulong, PlayerInfo> players = state.ToDictionary();
            
            Debug.Log($"UpdateLobbyStateClientRpc: {players.Count} players");
            
            // Update the local lobby state
            lobbyPlayers = new Dictionary<ulong, PlayerInfo>(players);
            
            // Notify listeners
            OnLobbyStateUpdated?.Invoke(lobbyPlayers);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in UpdateLobbyStateClientRpc: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    // RPC to notify clients about character selection
    [ClientRpc]
    private void PlayerCharacterSelectedClientRpc(ulong clientId, int characterIndex)
    {
        Debug.Log($"PlayerCharacterSelectedClientRpc: {clientId}, {characterIndex}");
        
        // Notify listeners
        OnPlayerCharacterSelected?.Invoke(clientId, characterIndex);
    }
    
    // RPC to notify clients that the game is starting
    [ClientRpc]
    private void GameStartingClientRpc()
    {
        Debug.Log("GameStartingClientRpc");
        
        // Notify listeners
        OnGameStarting?.Invoke();
    }
    
    // Request start game countdown from clients
    [ServerRpc(RequireOwnership = false)]
    public void StartGameCountdownServerRpc()
    {
        Debug.Log("LobbyManager: StartGameCountdownServerRpc called");
        
        if (!IsServer)
        {
            Debug.LogError("LobbyManager: StartGameCountdownServerRpc called on client (not server)!");
            return;
        }
            
        try
        {
            Debug.Log("LobbyManager: Starting countdown from ServerRpc");
            StartCountdown();
        }
        catch (Exception ex)
        {
            Debug.LogError($"LobbyManager: Error in StartGameCountdownServerRpc: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    #endregion
    
    #region Utility Methods
    
    // Shuffle a list
    private void ShuffleList<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = UnityEngine.Random.Range(0, n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }
    
    #endregion
    
    // This method needs to be called after scene load to ensure the LobbyManager is properly spawned on the network
    public void InitializeNetworkObject()
    {
        if (!IsSpawned && NetworkManager.Singleton != null)
        {
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                Debug.Log("LobbyManager: Spawning NetworkObject");
                networkObject.Spawn();
            }
            else
            {
                Debug.LogError("LobbyManager: NetworkObject component not found!");
            }
        }
    }
}
