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
    [Header("Countdown Overlay")]
    [SerializeField] private GameObject countdownOverlay; // The full-screen overlay for countdown
    [SerializeField] private TMPro.TextMeshProUGUI countdownText; // Text element within the overlay
    [SerializeField] private Button cancelCountdownButton; // Button to cancel the countdown

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
                        activeCountdownCoroutine = StartCoroutine(StartGameCountdown());
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
        _ = CreateLobby(lobbyNameInput, maxPlayersInput, false);
    }

    // Added async keyword
    private async Task<bool> CreateLobby(string lobbyName, int maxPlayers, bool isPrivate)
    {
        try
        {
            // Create player object for the host
            Player player = GetPlayer();
            
            // Generate a relay code for the lobby
            string relayCode = await CreateRelayCode();
            
            if (string.IsNullOrEmpty(relayCode))
            {
                Debug.LogError("Failed to create Relay Allocation.");
                return false;
            }
            
            Debug.Log($"Relay code for lobby: {relayCode}");
            
            // Create lobby options
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = player,
                Data = new Dictionary<string, DataObject>
                {
                    // Store the relay code in the lobby data
                    {
                        "RelayCode", new DataObject(
                            DataObject.VisibilityOptions.Member,
                            relayCode
                        )
                    },
                    // Add a timestamp to help with relay code validation
                    {
                        "RelayTimestamp", new DataObject(
                            DataObject.VisibilityOptions.Member,
                            System.DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
                        )
                    },
                    // Add a flag to track game starting state
                    {
                        KEY_GAME_STARTING, new DataObject(
                            DataObject.VisibilityOptions.Member,
                            "false"
                        )
                    }
                }
            };

            // Create lobby with Unity Services
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            hostLobby = lobby;
            joinedLobby = lobby;
            
            // Store the host's lobby locally
            Debug.Log($"Created Lobby with ID: {lobby.Id} and Code: {lobby.LobbyCode}");
            
            // Show the lobby UI
            ShowLobbyUI();
            
            // Start heartbeat to keep lobby alive
            StartCoroutine(HeartbeatLobbyCoroutine(lobby.Id, 30f));
            
            return true;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to create lobby: {e}");
            return false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Unexpected error creating lobby: {e}");
            return false;
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
        // Ensure we're not sending heartbeats too frequently (Unity has a rate limit)
        // Recommended minimum is 30 seconds between heartbeats
        float safeWaitTime = Mathf.Max(waitTimeSeconds, 30f);
        var delay = new WaitForSecondsRealtime(safeWaitTime);
        
        // Reset the failure counter when starting a new heartbeat coroutine
        heartbeatFailureCount = 0;
        
        while (hostLobby != null) // Continue as long as we are hosting this lobby
        {
            // Wait before sending heartbeat to prevent rate limiting
            yield return delay;
            
            // Send the heartbeat directly (no ref parameter needed anymore)
            try
            {
                LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
                Debug.Log("Lobby Heartbeat Sent");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error sending heartbeat: {e.Message}");
            }
            
            // Wait for next heartbeat opportunity
            yield return delay;
        }
    }
    
    // Store failure count as a field instead of passing by ref
    private int heartbeatFailureCount = 0;
    private readonly int MAX_HEARTBEAT_FAILURES = 5;
    
    // Countdown state tracking
    private bool countdownCancelled = false;
    private Coroutine activeCountdownCoroutine = null;
    
    private void SendHeartbeat(string lobbyId, ref int failureCount, int maxFailures)
    {
        try
        {
            // Store the current failure count locally for this scope
            heartbeatFailureCount = failureCount;
            int localMaxFailures = maxFailures;
            
            // Send heartbeat asynchronously
            LobbyService.Instance.SendHeartbeatPingAsync(lobbyId)
                .ContinueWith(task => {
                    if (task.IsFaulted) 
                    {
                        heartbeatFailureCount++;
                        // Only log every other failure to reduce console spam
                        if (heartbeatFailureCount % 2 == 0)
                        {
                            Debug.LogWarning($"Lobby heartbeat failed {heartbeatFailureCount}/{localMaxFailures} times: {task.Exception?.InnerException?.Message}");
                        }
                        
                        // If we've failed too many times, stop trying
                        if (heartbeatFailureCount >= localMaxFailures)
                        {
                            Debug.LogError("Too many lobby heartbeat failures. Lobby may expire.");
                            // Could add UI notification here
                        }
                    }
                    else
                    {
                        // Reset failure count on success
                        heartbeatFailureCount = 0;
                        Debug.Log("Lobby Heartbeat Sent Successfully");
                    }
                    
                    // Update the original failure count after task completes
                    // This is executed on a different thread, so it doesn't directly update the parameter
                });
            
            // Return the current value (this will be read after the lambda runs)
            failureCount = heartbeatFailureCount;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Error sending heartbeat: {e.Message}");
            failureCount++;
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
            // This broadcasts to all clients that the game is starting
            UpdateLobbyGameStartingStatus(true);
            
            // Make sure the lobby data is set properly before proceeding
            if (hostLobby != null && hostLobby.Data != null && hostLobby.Data.ContainsKey(KEY_GAME_STARTING))
            {
                Debug.Log($"Game starting status in lobby: {hostLobby.Data[KEY_GAME_STARTING].Value}");
            }
            else
            {
                Debug.LogWarning("Failed to verify game starting state in lobby data");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to update lobby game starting status: {e.Message}");
            // Continue anyway since we have other ways to notify clients
        }
        
        // Start the countdown coroutine and store a reference to it
        activeCountdownCoroutine = StartCoroutine(StartGameCountdown());
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

    // Method for the cancel button to call
    public void CancelGameCountdown()
    {
        // Only the host can cancel the countdown
        if (!IsLobbyHost())
        {
            return;
        }
        
        countdownCancelled = true;
        Debug.Log("Game countdown cancelled by user.");
        
        // Hide the overlay
        if (countdownOverlay != null)
        {
            countdownOverlay.SetActive(false);
        }
        
        // Re-enable the start button
        if (startGameButton != null)
        {
            startGameButton.interactable = true;
        }
        
        // Update lobby data to indicate game is no longer starting
        UpdateLobbyGameStartingStatus(false);
        
        // Cancel the coroutine if it's running
        if (activeCountdownCoroutine != null)
        {
            StopCoroutine(activeCountdownCoroutine);
            activeCountdownCoroutine = null;
        }
    }

    private System.Collections.IEnumerator StartGameCountdown()
    {
        // Note: The coroutine reference is stored when StartCoroutine is called
        // We don't set it here
        
        // Reset cancellation flag
        countdownCancelled = false;
        
        // Disable the start button during countdown
        if (startGameButton) 
            startGameButton.interactable = false;
        
        Debug.Log("Starting game countdown...");
        
        // Show and initialize countdown overlay
        if (countdownOverlay != null)
        {
            // Activate the overlay
            countdownOverlay.SetActive(true);
            
            // Reset text color if it was previously set to red for errors
            if (countdownText != null)
            {
                countdownText.color = Color.white;
            }
            
            // Set up cancel button if we're the host
            if (cancelCountdownButton != null)
            {
                // Clear existing listeners to avoid duplicates
                cancelCountdownButton.onClick.RemoveAllListeners();
                cancelCountdownButton.onClick.AddListener(CancelGameCountdown);
                
                // Only host can cancel
                cancelCountdownButton.gameObject.SetActive(IsLobbyHost());
            }
            
            // Countdown from 5 to 1
            for (int i = 5; i >= 1; i--)
            {
                // Check if cancelled during countdown
                if (countdownCancelled)
                {
                    yield break; // Exit if cancelled
                }
                
                if (countdownText != null)
                {
                    countdownText.text = i.ToString();
                }
                
                Debug.Log($"Game starting in {i}...");
                yield return new WaitForSeconds(1f);
            }
            
            // Check if cancelled during the last second
            if (countdownCancelled)
            {
                yield break;
            }
            
            // Show "Starting..." message
            if (countdownText != null)
            {
                countdownText.text = "Starting...";
            }
        }
        else
        {
            // If no countdown overlay, just wait 5 seconds
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

        // Ensure transport is properly configured before starting the network connection
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("Could not find UnityTransport component on NetworkManager!");
            yield break;
        }

        // For clients, ensure we have a valid connection before proceeding
        if (!IsLobbyHost())
        {
            // Verify we have transport data already configured
            if (string.IsNullOrEmpty(transport.ConnectionData.Address) || transport.ConnectionData.Address == "0.0.0.0")
            {
                Debug.LogError("Client doesn't have valid connection data configured");
                
                // Set a timeout to notify the player
                if (countdownOverlay != null)
                {
                    countdownOverlay.SetActive(true);
                    
                    if (countdownText != null)
                    {
                        countdownText.text = "Connection error!";
                        countdownText.color = Color.red;
                    }
                    
                    // Make sure the cancel button is visible for all players in this case
                    if (cancelCountdownButton != null)
                    {
                        cancelCountdownButton.onClick.RemoveAllListeners();
                        cancelCountdownButton.onClick.AddListener(() => countdownOverlay.SetActive(false));
                        cancelCountdownButton.gameObject.SetActive(true);
                    }
                }
                yield break;
            }
            else
            {
                Debug.Log($"Client using existing connection data: {transport.ConnectionData.Address}:{transport.ConnectionData.Port}");
            }
        }

        // Make sure we persist required objects through scene changes
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.gameObject != null)
        {
            DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
            
            // Register for transport failures
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }

        // Start networking based on role (host or client)
        if (IsLobbyHost())
        {
            Debug.Log("Starting as host...");
            
            // Call the preservation method to ensure NetworkManager persists
            PreserveNetworkObjects();
            
            // Start the network as host
            NetworkManager.Singleton.StartHost();
            
            // Wait to ensure connection is established
            yield return new WaitForSeconds(1.0f);
            
            try 
            {
                // Determine the scene to load
                string sceneToLoad = "Game";
                Debug.Log($"Host loading game scene: {sceneToLoad}");
                
                // Try to use NetworkSceneManager if possible, otherwise fall back to direct loading
                if (NetworkManager.Singleton.SceneManager != null)
                {
                    Debug.Log("Loading scene via NetworkSceneManager");
                    NetworkManager.Singleton.SceneManager.LoadScene(sceneToLoad, UnityEngine.SceneManagement.LoadSceneMode.Single);
                }
                else
                {
                    Debug.Log("NetworkSceneManager not available, loading scene directly");
                    UnityEngine.SceneManagement.SceneManager.LoadScene(sceneToLoad);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error loading game scene: {e.Message}");
                // Show error to host
                if (countdownText != null)
                {
                    countdownText.text = "Failed to start game!";
                    countdownText.color = Color.red;
                }
                
                // Re-enable start button in case they want to try again
                if (startGameButton) startGameButton.interactable = true;
            }
        }
        else // Client
        {
            Debug.Log("Starting as client...");
            
            // Setup timeout to prevent client from waiting indefinitely
            float connectionTimeout = 15f; // 15 seconds timeout
            float startTime = Time.time;
            bool connected = false;
            
            // Show connecting message
            if (countdownText != null)
            {
                countdownText.text = "Connecting...";
            }
            
            // Start client connection - outside try block to avoid yield issues
            bool connectionStarted = true;
            try
            {
                NetworkManager.Singleton.StartClient();
            }
            catch (System.Exception e)
            {
                connectionStarted = false;
                Debug.LogError($"Error starting client connection: {e.Message}");
                if (countdownText != null)
                {
                    countdownText.text = "Connection error!";
                    countdownText.color = Color.red;
                }
            }
            
            // Only proceed with connection check if we successfully started connecting
            if (connectionStarted)
            {
                // Wait for connection to be established or timeout
                while (!connected && Time.time - startTime < connectionTimeout)
                {
                    // Check if client is connected
                    if (NetworkManager.Singleton.IsConnectedClient)
                    {
                        connected = true;
                        Debug.Log("Client successfully connected to host!");
                        
                        // Update UI
                        if (countdownText != null)
                        {
                            countdownText.text = "Connected! Waiting for game start...";
                        }
                        
                        // Break out of the waiting loop
                        break;
                    }
                    
                    // Wait a bit before checking again
                    yield return new WaitForSeconds(0.5f);
                }
                
                // Handle timeout
                if (!connected)
                {
                    Debug.LogError("Client connection timed out!");
                    if (countdownText != null)
                    {
                        countdownText.text = "Connection timed out!";
                        countdownText.color = Color.red;
                    }
                    
                    // Could add a retry button here
                }
                else
                {
                    // Successfully connected, now register for scene events and wait for host to load the game scene
                    Debug.Log("Client connected and waiting for host to load the game scene");
                    
                    // Register for scene load completion events
                    if (NetworkManager.Singleton != null)
                    {
                        Debug.Log("Setting up scene change detection");
                        
                        // Use a simpler scene transition approach - just wait 5 seconds for scene to transition
                        // and then manually check if we need to load it
                        StartCoroutine(CheckAndLoadGameScene());
                    }
                    else
                    {
                        Debug.LogError("NetworkManager or SceneManager is null, cannot register for scene events");
                    }
                    
                    // We'll handle scene loading through our dedicated coroutine
                }
            }
        }
    }
    
    private void OnTransportFailure()
    {
        Debug.LogError("Network transport failure detected!");
        
        // If we're in the lobby and a transport failure occurs, try to handle it gracefully
        if (lobbyPanel != null && lobbyPanel.activeSelf)
        {
            // Show error message to user
            if (countdownText != null)
            {
                countdownText.text = "Connection failed!";
                countdownText.color = Color.red;
            }
            
            // Re-enable the start button if we're the host
            if (IsLobbyHost() && startGameButton != null)
            {
                startGameButton.interactable = true;
            }
            else
            {
                // For clients, we might want to show a rejoin button or return to main menu
                Debug.Log("Client experiencing transport failure, consider returning to main menu");
                // Could show a button to return to main menu here
            }
        }
    }
    
    // Simple coroutine to check and load the game scene for clients
    private System.Collections.IEnumerator CheckAndLoadGameScene()
    {
        Debug.Log("Starting client scene transition check");
        float startTime = Time.realtimeSinceStartup;
        float timeout = 10f; // Max time to wait before forcing scene load
        bool sceneLoaded = false;
        
        while (Time.realtimeSinceStartup - startTime < timeout && !sceneLoaded)
        {
            // Check if we're already in the Game scene
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Game")
            {
                Debug.Log("Already in Game scene - no action needed");
                sceneLoaded = true;
                break;
            }
            
            // Check if we're still connected as a client
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
            {
                Debug.LogWarning("Client disconnected during scene transition");
                if (countdownText != null)
                {
                    countdownText.text = "Connection lost!";
                    countdownText.color = Color.red;
                }
                yield break;
            }
            
            // Update UI to show we're still waiting
            if (countdownText != null)
            {
                countdownText.text = $"Game starting... {Mathf.Round(timeout - (Time.realtimeSinceStartup - startTime))}s";
            }
            
            // Give a short wait between checks
            yield return new WaitForSeconds(0.5f);
        }
        
        // If we've timed out and scene still hasn't loaded, force it
        if (!sceneLoaded)
        {
            Debug.LogWarning("CLIENT SCENE TRANSITION TIMED OUT! Forcing scene load.");
            
            // Force preserve critical objects before loading scene
            PreserveNetworkObjects();
            
            try
            {
                // Critical part: Force load the game scene directly
                Debug.Log("Manually loading Game scene for client after timeout");
                UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error loading game scene: {e.Message}");
                if (countdownText != null)
                {
                    countdownText.text = "Failed to join game!";
                    countdownText.color = Color.red;
                }
            }
        }
    }
    
    // Method for handling DontDestroyOnLoad objects
    private void PreserveNetworkObjects()
    {
        // Ensure NetworkManager persists across scene changes
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("Preserving NetworkManager for scene transition");
            DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
        }
        
        // Ensure NetworkManagerUI persists across scene changes
        if (NetworkManagerUI.Instance != null)
        {
            Debug.Log("Preserving NetworkManagerUI for scene transition");
            DontDestroyOnLoad(NetworkManagerUI.Instance.gameObject);
            
            // The NetworkManagerUI handles character selections, so make sure it stays active
            NetworkManagerUI.Instance.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogError("NetworkManagerUI.Instance not found! Creating NetworkManagerUI...");
            
            // Try to instantiate NetworkManagerUI from prefab if it doesn't exist
            GameObject networkManagerUIPrefab = Resources.Load<GameObject>("Prefabs/NetworkManagerUI");
            if (networkManagerUIPrefab != null)
            {
                GameObject instantiated = Instantiate(networkManagerUIPrefab);
                DontDestroyOnLoad(instantiated);
                Debug.Log("Created NetworkManagerUI from prefab");
            }
            else
            {
                Debug.LogError("Failed to load NetworkManagerUI prefab from Resources/Prefabs/NetworkManagerUI!");
            }
        }
        
        // Ensure GameManager persists or will be created
        if (GameManager.Instance != null)
        {
            Debug.Log("Preserving GameManager for scene transition");
            DontDestroyOnLoad(GameManager.Instance.gameObject);
        }
        else
        {
            Debug.LogWarning("GameManager.Instance not found! The Game scene should include a GameManager.");
            
            // Try to instantiate GameManager from prefab if it doesn't exist
            GameObject gameManagerPrefab = Resources.Load<GameObject>("Prefabs/GameManager");
            if (gameManagerPrefab != null)
            {
                GameObject instantiated = Instantiate(gameManagerPrefab);
                DontDestroyOnLoad(instantiated);
                Debug.Log("Created GameManager from prefab");
            }
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
        
        // Get the transport component once outside the loop
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("Failed to find Unity Transport component");
            return false;
        }
        
        // First, check if we already have valid relay data (e.g., from a previous successful join)
        if (!string.IsNullOrEmpty(transport.ConnectionData.Address) && 
            transport.ConnectionData.Address != "0.0.0.0" && 
            transport.ConnectionData.Port != 0)
        {
            Debug.Log("Client already has valid connection data configured, skipping relay join");
            return true;
        }
        
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
                transport.SetClientRelayData(
                    joinAllocation.RelayServer.IpV4,
                    (ushort)joinAllocation.RelayServer.Port,
                    joinAllocation.AllocationIdBytes,
                    joinAllocation.Key,
                    joinAllocation.ConnectionData,
                    joinAllocation.HostConnectionData
                );

                Debug.Log($"Successfully configured client relay data: {joinAllocation.RelayServer.IpV4}:{joinAllocation.RelayServer.Port}");
                return true;
            }
            catch (System.Exception e)
            {
                // On last attempt, try a more specific error message based on the exception
                if (attempt == maxRetries - 1)
                {
                    string errorMessage = e.Message;
                    
                    // Check for common error patterns
                    if (errorMessage.Contains("join code not found"))
                    {
                        Debug.LogError($"Relay join code '{relayCode}' is no longer valid or has expired. The host may need to create a new relay code.");
                    }
                    else if (errorMessage.Contains("allocation ID not found"))
                    {
                        Debug.LogError($"Relay allocation no longer exists. The host may have disconnected or the allocation expired.");
                    }
                    else
                    {
                        Debug.LogError($"Failed to join Relay with code '{relayCode}' after {maxRetries} attempts: {errorMessage}");
                    }
                    
                    // At this point we could try a direct connection instead, but we'd need the host's IP
                    return false;
                }
                
                // Otherwise, log warning and continue to next retry
                Debug.LogWarning($"Attempt {attempt+1}/{maxRetries} to join Relay failed: {e.Message}. Retrying...");
            }
        }
        
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
                
                // Check if the relay code is potentially expired (more than 60 seconds old)
                bool potentiallyExpired = false;
                if (joinedLobby.Data.ContainsKey("RelayTimestamp"))
                {
                    string timestampStr = joinedLobby.Data["RelayTimestamp"].Value;
                    if (long.TryParse(timestampStr, out long timestamp))
                    {
                        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        long ageInSeconds = now - timestamp;
                        Debug.Log($"Relay code age: {ageInSeconds} seconds");
                        
                        // If code is more than 60 seconds old, it might be expired
                        if (ageInSeconds > 60)
                        {
                            potentiallyExpired = true;
                            Debug.LogWarning("Relay code might be expired. Requesting host to refresh relay code.");
                        }
                    }
                }
                
                // Try joining the Relay
                bool relayJoined = await JoinRelay(relayCode);
                
                // If join failed and code might be expired, request a refresh from host
                if (!relayJoined && potentiallyExpired)
                {
                    Debug.LogWarning("Failed to join with expired relay code. Using direct connection instead.");
                    
                    // For now, proceed with lobby join but no relay
                    // In a production app, you could implement a relay code refresh mechanism here
                    // such as a ClientRPC call or custom lobby data update
                    
                    ShowLobbyUI();
                    return;
                }
                else if (!relayJoined)
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