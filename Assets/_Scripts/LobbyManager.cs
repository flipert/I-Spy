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

            Debug.Log($"Created Lobby with Relay! Name: {lobby.Name}, Code: {lobby.LobbyCode}, Relay Code: {relayCode}");
            Debug.Log($"Lobby Data: {string.Join(", ", lobby.Data.Select(kvp => $"{kvp.Key}: {kvp.Value.Value}"))}");

            ShowLobbyUI();
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
        StartCoroutine(StartGameCountdown());
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
                yield return new WaitForSeconds(1f);
            }
            
            // Show "Starting..." message
            countdownText.text = "Starting...";
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
                ulong clientId = ulong.Parse(player.Id);
                NetworkManagerUI.Instance.UpdateCharacterSelection(clientId, characterIndex);
            }
        }

        // Start as host if we're the host
        if (IsLobbyHost() && NetworkManager.Singleton != null)
        {
            Debug.Log("Starting as host...");
            NetworkManager.Singleton.StartHost();
        }
        else if (!IsLobbyHost() && NetworkManager.Singleton != null)
        {
            Debug.Log("Starting as client...");
            NetworkManager.Singleton.StartClient();
        }

        // Load the game scene - this will be handled by NetworkManager now
        if (IsLobbyHost())
        {
            NetworkManager.Singleton.SceneManager.LoadScene("Game", UnityEngine.SceneManagement.LoadSceneMode.Single);
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
                if (string.IsNullOrWhiteSpace(lobby.LobbyCode))
                {
                    Debug.LogWarning($"Skipping lobby {lobby.Id} because it has no lobby code");
                    continue;
                }

                // Log each lobby's data for debugging
                string lobbyInfo = $"Found Lobby - Name: {lobby.Name}, ID: {lobby.Id}, Code: {lobby.LobbyCode}";
                if (lobby.Data != null && lobby.Data.ContainsKey("RelayCode"))
                {
                    lobbyInfo += $", Relay Code: {lobby.Data["RelayCode"].Value}";
                }
                Debug.Log(lobbyInfo);

                GameObject entryGO = Instantiate(lobbyEntryPrefab, lobbyListContent);
                LobbyEntryUI entryUI = entryGO.GetComponent<LobbyEntryUI>();
                if (entryUI != null)
                {
                    // Store the lobby code in a local variable to ensure it's captured correctly
                    string lobbyCode = lobby.LobbyCode;
                    Debug.Log($"Setting up join button for lobby code: {lobbyCode}");
                    entryUI.Initialize(lobby, async () => {
                        Debug.Log($"Join button clicked for lobby code: {lobbyCode}");
                        await JoinLobbyByCode(lobbyCode);
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
        try
        {
            // Join Relay with code
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(relayCode);
            
            // Set up client's network transport with Relay
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            
            if (transport != null)
            {
                transport.SetClientRelayData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData,
                    allocation.HostConnectionData
                );
                
                return true;
            }
            
            return false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to join Relay: {e.Message}");
            return false;
        }
    }

    private void Update()
    {
        HandleLobbyHeartbeat();
        HandleLobbyPollAndUpdate();
    }

    private void HandleLobbyHeartbeat()
    {
        if (hostLobby != null)
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer < 0f)
            {
                heartbeatTimer = 15f; // Reset timer
                LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id);
            }
        }
    }

    private void HandleLobbyPollAndUpdate()
    {
        if (joinedLobby == null) return;

        lobbyUpdateTimer -= Time.deltaTime;
        if (lobbyUpdateTimer < 0f)
        {
            lobbyUpdateTimer = lobbyUpdateInterval;
            
            try
            {
                _ = UpdateLobbyData(); // Fire and forget
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error in HandleLobbyPollAndUpdate: {e}");
            }
        }
    }

    private async Task UpdateLobbyData()
    {
        try
        {
            joinedLobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
            UpdatePlayerList();
            
            // Log the current lobby data for debugging
            if (joinedLobby.Data != null && joinedLobby.Data.ContainsKey("RelayCode"))
            {
                Debug.Log($"Current Lobby Data - ID: {joinedLobby.Id}, Code: {joinedLobby.LobbyCode}, Relay Code: {joinedLobby.Data["RelayCode"].Value}");
            }
        }
        catch (LobbyServiceException e)
        {
            if (e.Reason == LobbyExceptionReason.RateLimited)
            {
                // If rate limited, increase the update interval temporarily
                lobbyUpdateInterval = Mathf.Min(lobbyUpdateInterval * 1.5f, 5f);
                Debug.LogWarning($"Rate limited. Increasing update interval to {lobbyUpdateInterval} seconds");
            }
            else if (e.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                Debug.LogWarning("Lobby no longer exists");
                joinedLobby = null;
                ShowMainMenuUI();
            }
            else
            {
                Debug.LogError($"Failed to update lobby: {e}");
            }
        }
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
            Debug.Log($"Joined lobby by code: {joinedLobby.Name}");

            if (joinedLobby.Data != null && joinedLobby.Data.ContainsKey("RelayCode"))
            {
                string relayCode = joinedLobby.Data["RelayCode"].Value;
                Debug.Log($"Found Relay code in joined lobby: {relayCode}");
                
                bool relayJoined = await JoinRelay(relayCode);
                if (!relayJoined)
                {
                    Debug.LogError("Failed to join Relay after joining lobby by code");
                    await LeaveLobby();
                    return;
                }
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