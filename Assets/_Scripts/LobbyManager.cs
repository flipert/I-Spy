using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI; // Required for Button
using Unity.Netcode;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System.Linq; // Add this for Select extension method
using TMPro;

public class LobbyManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject mainMenuPanel; // Drag your main menu buttons container here
    [SerializeField] private GameObject lobbyPanel; // Drag your LobbyPanel here
    [SerializeField] private Button startGameButton; // Drag your StartGameButton here
    [SerializeField] private Button leaveLobbyButton; // Drag your LeaveLobbyButton here
    [SerializeField] private TMPro.TextMeshProUGUI lobbyCodeText; // Drag LobbyCodeText here
    [SerializeField] private TMPro.TextMeshProUGUI countdownText; // Add reference to countdown text

    [Header("Lobby Browser UI")]
    [SerializeField] private GameObject lobbyBrowserPanel; // Panel to show available lobbies
    [SerializeField] private Transform lobbyListContent; // Parent transform for lobby entries
    [SerializeField] private GameObject lobbyEntryPrefab; // Prefab for each lobby in the list
    [SerializeField] private Button refreshLobbiesButton;
    [SerializeField] private Button closeBrowserButton;

    [Header("Lobby Settings")]
    [SerializeField] private string lobbyNameInput = "MyLobby";
    [SerializeField] private int maxPlayersInput = 4;
    [SerializeField] private float lobbyUpdateInterval = 2f; // Increased to 2 seconds to avoid rate limits

    [Header("Lobby UI")]
    [SerializeField] private Transform playerListContent; // Parent transform for player entries
    [SerializeField] private GameObject playerEntryPrefab; // Prefab for each player in the list

    private Lobby hostLobby;
    private Lobby joinedLobby; // Added to track joined lobby
    private float heartbeatTimer;
    private float lobbyPollTimer;
    private bool isRefreshingLobbies;
    private float lobbyUpdateTimer;
    private int previousPlayerCount = 0; // Track previous player count to detect joins

    // Add this to track if game is starting
    private bool isGameStarting = false;
    private const string KEY_GAME_STARTING = "GameStarting";

    private void Update()
    {
        // Handle heartbeat for hosted lobbies
        HandleLobbyHeartbeat();
        
        // Poll for lobby updates when in a lobby
        PollForLobbyUpdates();
    }

    private void HandleLobbyHeartbeat()
    {
        if (hostLobby != null)
        {
            if (heartbeatTimer <= 0f)
            {
                heartbeatTimer = 15f; // Reset timer
                LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id).ContinueWith(
                    (task) => {
                        if (task.IsFaulted)
                        {
                            Debug.LogError($"Failed to send heartbeat: {task.Exception}");
                        }
                        else
                        {
                            Debug.Log("Sent lobby heartbeat ping");
                        }
                    });
            }
            else
            {
                heartbeatTimer -= Time.deltaTime;
            }
        }
    }

    private async void PollForLobbyUpdates()
    {
        // Only poll if we're in a lobby but not hosting (host has the source of truth)
        if (joinedLobby != null && !IsLobbyHost())
        {
            if (lobbyPollTimer <= 0f)
            {
                lobbyPollTimer = lobbyUpdateInterval; // Reset timer
                
                try
                {
                    Lobby updatedLobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
                    
                    // Check if player count has changed (someone joined or left)
                    if (updatedLobby.Players.Count != previousPlayerCount)
                    {
                        Debug.Log($"Player count changed: {previousPlayerCount} -> {updatedLobby.Players.Count}");
                        
                        // If player count increased, someone joined
                        if (updatedLobby.Players.Count > previousPlayerCount)
                        {
                            // Find the new player(s)
                            foreach (Player player in updatedLobby.Players)
                            {
                                bool isNewPlayer = true;
                                foreach (Player existingPlayer in joinedLobby.Players)
                                {
                                    if (existingPlayer.Id == player.Id)
                                    {
                                        isNewPlayer = false;
                                        break;
                                    }
                                }
                                
                                if (isNewPlayer)
                                {
                                    Debug.Log($"<color=green>New player joined the lobby: {player.Id}</color>");
                                }
                            }
                        }
                        
                        previousPlayerCount = updatedLobby.Players.Count;
                    }
                    
                    // Check if game is starting
                    if (!isGameStarting && updatedLobby.Data != null && 
                        updatedLobby.Data.ContainsKey(KEY_GAME_STARTING) && 
                        updatedLobby.Data[KEY_GAME_STARTING].Value == "true")
                    {
                        Debug.Log("<color=yellow>Host has started the game! Starting countdown...</color>");
                        isGameStarting = true;
                        StartCoroutine(StartGameCountdown());
                    }
                    
                    // Update our local copy of the lobby
                    joinedLobby = updatedLobby;
                    UpdatePlayerList();
                }
                catch (LobbyServiceException e)
                {
                    Debug.LogWarning($"Failed to poll for lobby updates: {e.Message}");
                }
            }
            else
            {
                lobbyPollTimer -= Time.deltaTime;
            }
        }
    }

    // Wrapper function for the UI Button
    public void CreateLobby_Button()
    {
        // Call the async method but don't wait for it here (fire and forget for UI)
        // The _ discards the returned Task to avoid compiler warnings
        _ = CreateLobby(lobbyNameInput, maxPlayersInput);
    }

    // Added async keyword
    public async Task CreateLobby(string lobbyName, int maxPlayers)
    {
        try
        {
            // Create Relay allocation first
            string relayCode = await CreateRelayCode();
            if (string.IsNullOrEmpty(relayCode))
            {
                Debug.LogError("Failed to create Relay allocation");
                return;
            }

            Debug.Log($"Created Relay code: {relayCode}");

            // Create lobby options with Relay code
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = GetPlayer(),
                Data = new Dictionary<string, DataObject>
                {
                    { 
                        "RelayCode", 
                        new DataObject(
                            DataObject.VisibilityOptions.Public,
                            relayCode
                        ) 
                    }
                }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            hostLobby = lobby;
            joinedLobby = hostLobby;

            // Log detailed information about the created lobby
            Debug.Log($"Created Lobby Details:");
            Debug.Log($"- Name: {lobby.Name}");
            Debug.Log($"- ID: {lobby.Id}");
            Debug.Log($"- Code: {lobby.LobbyCode}");
            Debug.Log($"- Max Players: {lobby.MaxPlayers}");
            Debug.Log($"- Host ID: {lobby.HostId}");
            Debug.Log($"- Is Private: {lobby.IsPrivate}");
            if (lobby.Data != null)
            {
                foreach (var data in lobby.Data)
                {
                    Debug.Log($"- Data: {data.Key} = {data.Value.Value}");
                }
            }

            ShowLobbyUI();

            // Start the heartbeat
            heartbeatTimer = 15f;
            StartCoroutine(HeartbeatLobbyCoroutine(lobby.Id, 15f));
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to create lobby: {e}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Unexpected error creating lobby: {e}");
        }
    }

    // Helper function to create player object (can be expanded)
    private Player GetPlayer()
    {
        // Get the selected character index from NetworkManagerUI
        int selectedCharacter = 0; // Default value
        if (NetworkManagerUI.Instance != null)
        {
            selectedCharacter = NetworkManagerUI.Instance.GetClientCharacterIndex(NetworkManager.Singleton.LocalClientId);
        }

        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { 
                    "CharacterIndex", 
                    new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Member, 
                        selectedCharacter.ToString()
                    )
                }
            }
        };
    }

    // Add this method to get character selection from lobby data
    private int GetCharacterIndexFromLobbyPlayer(Player player)
    {
        if (player.Data != null && 
            player.Data.ContainsKey("CharacterIndex") && 
            int.TryParse(player.Data["CharacterIndex"].Value, out int characterIndex))
        {
            return characterIndex;
        }
        return 0; // Default to first character if no selection found
    }

    // Coroutine to send heartbeat pings for hosted lobbies
    private System.Collections.IEnumerator HeartbeatLobbyCoroutine(string lobbyId, float waitTimeSeconds)
    {
        var delay = new WaitForSecondsRealtime(waitTimeSeconds);
        while (hostLobby != null) // Continue as long as we are hosting this lobby
        {
            LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            Debug.Log("Lobby Heartbeat Sent");
            yield return delay;
        }
    }
    
    // Need to handle leaving/deleting lobby when host disconnects or game ends
    // Placeholder for now
    async void OnDestroy()
    {
        await LeaveLobby();
    }

    public async Task LeaveLobby()
    {
        if (hostLobby != null)
        {
            try
            {
                 // Check if we are the host
                 string playerId = AuthenticationService.Instance.PlayerId;
                 if (hostLobby.HostId == playerId) 
                 { 
                      Debug.Log($"Host deleting lobby: {hostLobby.Id}");
                      await LobbyService.Instance.DeleteLobbyAsync(hostLobby.Id);
                      hostLobby = null;
                 }
                 else 
                 {
                      // Player is leaving, not deleting
                      Debug.Log($"Player leaving lobby: {hostLobby.Id}");
                      await LobbyService.Instance.RemovePlayerAsync(hostLobby.Id, playerId);
                      hostLobby = null; // Clear local lobby reference
                 }
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"Error leaving/deleting lobby: {e}");
            }
        }
    }

    // --- UI Management ---
    private void ShowMainMenuUI()
    {
        if (mainMenuPanel) mainMenuPanel.SetActive(true);
        if (lobbyPanel) lobbyPanel.SetActive(false);
    }

    private void ShowLobbyUI()
    {
        if (mainMenuPanel) mainMenuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(true);
        if (lobbyBrowserPanel) lobbyBrowserPanel.SetActive(false); // Hide the lobby browser panel

        if (lobbyCodeText != null && joinedLobby != null)
        {
            lobbyCodeText.text = $"Lobby Code: {joinedLobby.LobbyCode}";
        }

        UpdatePlayerList();

        // Only host can start the game
        if (startGameButton)
        {
           startGameButton.gameObject.SetActive(IsLobbyHost());
           startGameButton.onClick.RemoveAllListeners();
           startGameButton.onClick.AddListener(() => StartGame());
        }
        
        // Wire up Leave Button
        if (leaveLobbyButton)
        {
           leaveLobbyButton.onClick.RemoveAllListeners(); // Clear previous listeners
           leaveLobbyButton.onClick.AddListener(async () => {
               await LeaveLobby();
               // After leaving, show main menu again
               ShowMainMenuUI(); 
           });
        }
    }

    private void UpdatePlayerList()
    {
        if (playerListContent == null || playerEntryPrefab == null || joinedLobby == null)
        {
            return;
        }

        // Clear existing player entries
        foreach (Transform child in playerListContent)
        {
            Destroy(child.gameObject);
        }

        // Create new entries for each player
        foreach (var player in joinedLobby.Players)
        {
            GameObject playerEntry = Instantiate(playerEntryPrefab, playerListContent);
            PlayerEntryUI entryUI = playerEntry.GetComponent<PlayerEntryUI>();
            
            if (entryUI != null)
            {
                // Get player name from player data or use a default
                string playerName = "Player";
                if (player.Data != null && player.Data.ContainsKey("PlayerName"))
                {
                    playerName = player.Data["PlayerName"].Value;
                }

                // Check if player is ready
                bool isReady = false;
                if (player.Data != null && player.Data.ContainsKey("IsReady"))
                {
                    bool.TryParse(player.Data["IsReady"].Value, out isReady);
                }

                entryUI.Initialize(playerName, isReady);
            }
        }
    }

    private bool IsLobbyHost() 
    {
        return joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }

    // Add this new method
    private void StartGame()
    {
        if (!IsLobbyHost())
        {
            Debug.LogError("Only the host can start the game!");
            return;
        }

        Debug.Log("Host is starting the game...");
        
        // Update lobby data to notify clients that game is starting
        try
        {
            UpdateLobbyGameStartingStatus(true);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to update lobby game starting status: {e.Message}");
        }
        
        StartCoroutine(StartGameCountdown());
    }
    
    private async void UpdateLobbyGameStartingStatus(bool isStarting)
    {
        if (hostLobby == null) return;
        
        try
        {
            // Create update data
            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    {
                        KEY_GAME_STARTING,
                        new DataObject(
                            DataObject.VisibilityOptions.Member,
                            isStarting ? "true" : "false"
                        )
                    }
                }
            };
            
            // Update the lobby
            Lobby updatedLobby = await LobbyService.Instance.UpdateLobbyAsync(hostLobby.Id, options);
            hostLobby = updatedLobby;
            joinedLobby = updatedLobby;
            
            Debug.Log($"Updated lobby game starting status to: {isStarting}");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to update lobby game starting status: {e.Message}");
        }
    }

    private System.Collections.IEnumerator StartGameCountdown()
    {
        // Disable the start button during countdown
        if (startGameButton) startGameButton.interactable = false;
        
        // Show and initialize countdown text
        if (countdownText)
        {
            countdownText.gameObject.SetActive(true);
            
            // Countdown from 5 to 1
            for (int i = 5; i >= 1; i--)
            {
                countdownText.text = i.ToString();
                Debug.Log($"Game starting in {i}...");
                yield return new WaitForSeconds(1f);
            }
            
            // Show "Starting..." message
            countdownText.text = "Starting...";
            Debug.Log("Game starting now!");
        }
        else
        {
            // If no countdown text UI, just wait 5 seconds
            yield return new WaitForSeconds(5f);
        }

        // Update character selections in NetworkManagerUI for all players
        if (NetworkManagerUI.Instance != null && joinedLobby != null)
        {
            foreach (Player player in joinedLobby.Players)
            {
                int characterIndex = GetCharacterIndexFromLobbyPlayer(player);
                
                // Parse player ID to client ID - use try-catch to handle potential parsing errors
                try
                {
                    ulong clientId = ulong.Parse(player.Id);
                    NetworkManagerUI.Instance.UpdateCharacterSelection(clientId, characterIndex);
                    Debug.Log($"Updated character selection for player {player.Id} to character {characterIndex}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to parse player ID {player.Id} to client ID: {e.Message}");
                }
            }
        }

        // Start networking based on role (host or client)
        if (IsLobbyHost() && NetworkManager.Singleton != null)
        {
            Debug.Log("Starting as host...");
            NetworkManager.Singleton.StartHost();
            
            // Load the game scene - this will be handled by NetworkManager now
            // This will automatically synchronize to all connected clients
            NetworkManager.Singleton.SceneManager.LoadScene("Game", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else if (!IsLobbyHost() && NetworkManager.Singleton != null)
        {
            Debug.Log("Starting as client...");
            NetworkManager.Singleton.StartClient();
            
            // The host will handle scene loading for clients through NetworkManager's scene management
            Debug.Log("Waiting for host to load the game scene...");
        }
    }

    public async void FindLobbies_Button()
    {
        ShowLobbyBrowserUI();
        await QueryLobbies();
    }

    private async Task QueryLobbies()
    {
        if (isRefreshingLobbies) return;

        isRefreshingLobbies = true;
        try
        {
            // Clear existing lobby entries
            foreach (Transform child in lobbyListContent)
            {
                Destroy(child.gameObject);
            }

            QueryLobbiesOptions options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(
                        field: QueryFilter.FieldOptions.AvailableSlots,
                        op: QueryFilter.OpOptions.GT,
                        value: "0")
                }
            };

            QueryResponse lobbies = await LobbyService.Instance.QueryLobbiesAsync(options);
            Debug.Log($"Found {lobbies.Results.Count} lobbies");

            foreach (Lobby lobby in lobbies.Results)
            {
                // Log detailed information about each found lobby
                string lobbyInfo = $"\nFound Lobby:";
                lobbyInfo += $"\n- Name: {lobby.Name}";
                lobbyInfo += $"\n- ID: {lobby.Id}";
                lobbyInfo += $"\n- Code: {lobby.LobbyCode}";
                lobbyInfo += $"\n- Players: {lobby.Players.Count}/{lobby.MaxPlayers}";
                lobbyInfo += $"\n- Host ID: {lobby.HostId}";
                
                if (lobby.Data != null && lobby.Data.ContainsKey("RelayCode"))
                {
                    lobbyInfo += $"\n- Relay Code: {lobby.Data["RelayCode"].Value}";
                }
                Debug.Log(lobbyInfo);

                // Create lobby entry even if lobby code is missing
                GameObject entryGO = Instantiate(lobbyEntryPrefab, lobbyListContent);
                LobbyEntryUI entryUI = entryGO.GetComponent<LobbyEntryUI>();
                if (entryUI != null)
                {
                    entryUI.Initialize(lobby, async () => {
                        // If lobby code is missing, try to join by ID instead
                        if (string.IsNullOrWhiteSpace(lobby.LobbyCode))
                        {
                            Debug.Log($"Attempting to join lobby by ID: {lobby.Id}");
                            try
                            {
                                JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
                                {
                                    Player = GetPlayer()
                                };
                                joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id, options);
                                
                                if (joinedLobby.Data != null && joinedLobby.Data.ContainsKey("RelayCode"))
                                {
                                    string relayCode = joinedLobby.Data["RelayCode"].Value;
                                    Debug.Log($"Found Relay code in joined lobby: {relayCode}");
                                    
                                    bool relayJoined = await JoinRelay(relayCode);
                                    if (!relayJoined)
                                    {
                                        Debug.LogError("Failed to join Relay after joining lobby by ID");
                                        await LeaveLobby();
                                        return;
                                    }
                                }
                                
                                ShowLobbyUI();
                            }
                            catch (LobbyServiceException e)
                            {
                                Debug.LogError($"Failed to join lobby by ID: {e}");
                            }
                        }
                        else
                        {
                            Debug.Log($"Attempting to join lobby with code: {lobby.LobbyCode}");
                            await JoinLobbyByCode(lobby.LobbyCode);
                        }
                    });
                }
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to query lobbies: {e}");
        }
        finally
        {
            isRefreshingLobbies = false;
        }
    }

    private async Task JoinSelectedLobby(Lobby selectedLobby)
    {
        try
        {
            if (selectedLobby == null)
            {
                Debug.LogError("Selected lobby is null");
                return;
            }

            string lobbyCode = selectedLobby.LobbyCode;
            Debug.Log($"Attempting to join lobby with code: {lobbyCode}");

            if (string.IsNullOrWhiteSpace(lobbyCode))
            {
                Debug.LogError("Cannot join lobby: Lobby code is null or empty");
                return;
            }

            await JoinLobbyByCode(lobbyCode);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Unexpected error joining selected lobby: {e}");
        }
    }

    private void ShowLobbyBrowserUI()
    {
        if (mainMenuPanel) mainMenuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(false);
        if (lobbyBrowserPanel) 
        {
            lobbyBrowserPanel.SetActive(true);
            
            // Wire up refresh button
            if (refreshLobbiesButton)
            {
                refreshLobbiesButton.onClick.RemoveAllListeners();
                refreshLobbiesButton.onClick.AddListener(async () => await QueryLobbies());
            }

            // Wire up close button
            if (closeBrowserButton)
            {
                closeBrowserButton.onClick.RemoveAllListeners();
                closeBrowserButton.onClick.AddListener(() => {
                    lobbyBrowserPanel.SetActive(false);
                    mainMenuPanel.SetActive(true);
                });
            }
        }
    }

    private async Task<string> CreateRelayCode()
    {
        try
        {
            // Create Relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(4); // Max 4 players
            
            // Get the join code
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            
            Debug.Log($"Created Relay allocation with code: {relayCode}");
            
            // Set up host's network transport with Relay
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            
            if (transport != null)
            {
                transport.SetHostRelayData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData
                );

                // Store the allocation data
                Debug.Log($"Successfully configured host relay data with allocation ID: {allocation.AllocationId}");
            }
            
            return relayCode;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to create Relay allocation: {e.Message}");
            return string.Empty;
        }
    }

    private async Task<bool> JoinRelay(string relayCode)
    {
        // Number of retry attempts
        const int maxRetries = 3;
        // Delay between retries (in milliseconds)
        const int retryDelayMs = 1000;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // If this is a retry attempt, log it and wait before trying again
                if (attempt > 0)
                {
                    Debug.Log($"Retry attempt {attempt}/{maxRetries-1} to join Relay with code: {relayCode}");
                    // Wait before retrying
                    await Task.Delay(retryDelayMs);
                }
                else
                {
                    Debug.Log($"Attempting to join Relay with code: {relayCode}");
                }

                // Join Relay with code - CLIENTS JOIN EXISTING ALLOCATIONS, NOT CREATE NEW ONES
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);
                Debug.Log($"Successfully joined Relay allocation with ID: {joinAllocation.AllocationId}");
                
                // Set up client's network transport with Relay
                var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                
                if (transport != null)
                {
                    transport.SetClientRelayData(
                        joinAllocation.RelayServer.IpV4,
                        (ushort)joinAllocation.RelayServer.Port,
                        joinAllocation.AllocationIdBytes,
                        joinAllocation.Key,
                        joinAllocation.ConnectionData,
                        joinAllocation.HostConnectionData
                    );

                    Debug.Log("Successfully configured client relay data");
                    return true;
                }
                
                Debug.LogError("Failed to find Unity Transport component");
                return false;
            }
            catch (System.Exception e)
            {
                // On last attempt, log error and return false
                if (attempt == maxRetries - 1)
                {
                    Debug.LogError($"Failed to join Relay with code '{relayCode}' after {maxRetries} attempts: {e.Message}");
                    return false;
                }
                
                // Otherwise, log warning and continue to next retry
                Debug.LogWarning($"Attempt {attempt+1}/{maxRetries} to join Relay failed: {e.Message}. Retrying...");
            }
        }
        
        // This should never be reached due to the return in the last catch block, but added for safety
        return false;
    }

    public async Task JoinLobbyByCode(string lobbyCode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(lobbyCode))
            {
                Debug.LogError("Cannot join lobby: Provided lobby code is null or empty");
                return;
            }

            Debug.Log($"Attempting to join lobby with code: {lobbyCode}");

            JoinLobbyByCodeOptions options = new JoinLobbyByCodeOptions
            {
                Player = GetPlayer()
            };

            joinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options);
            Debug.Log($"Successfully joined lobby: {joinedLobby.Name}");

            if (joinedLobby.Data != null && joinedLobby.Data.ContainsKey("RelayCode"))
            {
                string relayCode = joinedLobby.Data["RelayCode"].Value;
                Debug.Log($"Found Relay code in joined lobby: {relayCode}");
                
                // Try joining the Relay
                bool relayJoined = await JoinRelay(relayCode);
                if (!relayJoined)
                {
                    Debug.LogError("Failed to join Relay after joining lobby by code");
                    await LeaveLobby();
                    return;
                }
                
                Debug.Log("Successfully joined both Lobby and Relay!");
            }
            else
            {
                Debug.LogError("Joined lobby does not contain Relay code");
                await LeaveLobby();
                return;
            }

            ShowLobbyUI();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to join lobby by code: {e}");
        }
    }

    // --- Find/Join Lobby Functions (To be added later) ---

} 