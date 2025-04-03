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

    private Lobby hostLobby;
    private Lobby joinedLobby; // Added to track joined lobby
    private float heartbeatTimer;
    private float lobbyPollTimer;
    private bool isRefreshingLobbies;

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

            // Create lobby options with Relay code
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = GetPlayer(),
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayCode", new DataObject(DataObject.VisibilityOptions.Member, relayCode) }
                }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            hostLobby = lobby;
            joinedLobby = hostLobby;

            Debug.Log($"Created Lobby with Relay! Name: {lobby.Name}, Code: {lobby.LobbyCode}, Relay Code: {relayCode}");

            ShowLobbyUI();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to create lobby: {e}");
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

        // Update UI elements
        if (lobbyCodeText && joinedLobby != null)
        {
            lobbyCodeText.text = $"Lobby Code: {joinedLobby.LobbyCode}";
        }

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

        // Add logic to update player list UI here later...
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
                Count = 25, // Number of lobbies to fetch
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(
                        field: QueryFilter.FieldOptions.AvailableSlots,
                        op: QueryFilter.OpOptions.GT,
                        value: "0") // Only show lobbies with available slots
                },
                Order = new List<QueryOrder>
                {
                    new QueryOrder(
                        asc: true,
                        field: QueryOrder.FieldOptions.Created)
                }
            };

            QueryResponse lobbies = await LobbyService.Instance.QueryLobbiesAsync(options);
            Debug.Log($"Found {lobbies.Results.Count} lobbies");

            foreach (Lobby lobby in lobbies.Results)
            {
                GameObject entryGO = Instantiate(lobbyEntryPrefab, lobbyListContent);
                LobbyEntryUI entryUI = entryGO.GetComponent<LobbyEntryUI>();
                if (entryUI != null)
                {
                    entryUI.Initialize(lobby, async () => await JoinSelectedLobby(lobby));
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
            // Get the Relay code from the lobby data
            string relayCode = selectedLobby.Data["RelayCode"].Value;
            
            // Join the Relay allocation
            bool relayJoined = await JoinRelay(relayCode);
            if (!relayJoined)
            {
                Debug.LogError("Failed to join Relay");
                return;
            }

            // Join the lobby
            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = GetPlayer()
            };

            joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(selectedLobby.Id, options);
            Debug.Log($"Joined lobby: {joinedLobby.Name} with Relay code: {relayCode}");
            
            ShowLobbyUI();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to join lobby: {e}");
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

    // --- Find/Join Lobby Functions (To be added later) ---

} 