using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class GameManager : NetworkBehaviour
{
    [Header("Game Settings")]
    [SerializeField] private float gameTimeInMinutes = 10f;

    // Singleton pattern
    public static GameManager Instance { get; private set; }

    // Network variables for game state
    private NetworkVariable<float> gameTimeRemaining = new NetworkVariable<float>(0f);
    private NetworkVariable<bool> gameInProgress = new NetworkVariable<bool>(false);
    
    // Keep track of all players and their targets
    private Dictionary<ulong, PlayerController> spawnedPlayers = new Dictionary<ulong, PlayerController>();
    private Dictionary<ulong, ulong> playerTargets = new Dictionary<ulong, ulong>(); // Key: player ID, Value: target player ID
    private Dictionary<ulong, List<ulong>> playerPursuers = new Dictionary<ulong, List<ulong>>(); // Key: player ID, Value: list of players targeting them

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
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Initialize server-side variables
            gameTimeRemaining.Value = gameTimeInMinutes * 60f; // Convert to seconds
            
            // Subscribe to network events
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    private void Update()
    {
        if (IsServer && gameInProgress.Value)
        {
            // Update game time
            gameTimeRemaining.Value = Mathf.Max(0, gameTimeRemaining.Value - Time.deltaTime);
            
            // Check for game end
            if (gameTimeRemaining.Value <= 0)
            {
                EndGame();
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            // Unsubscribe from network events
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }
        
        base.OnNetworkDespawn();
    }

    #region Player Registration & Management

    // Called by PlayerController when a player spawns
    public void RegisterPlayer(PlayerController player)
    {
        if (!IsServer) return;

        ulong playerId = player.OwnerClientId;
        
        if (!spawnedPlayers.ContainsKey(playerId))
        {
            spawnedPlayers[playerId] = player;
            Debug.Log($"GameManager: Registered player {playerId}");
            
            // Initialize new player's pursuer list
            if (!playerPursuers.ContainsKey(playerId))
            {
                playerPursuers[playerId] = new List<ulong>();
            }
            
            // Assign initial targets if we have enough players
            if (spawnedPlayers.Count >= 2)
            {
                AssignAllTargets();
            }
        }
    }

    // Called when a player disconnects or if we need to unregister a player
    private void UnregisterPlayer(ulong playerId)
    {
        if (!IsServer) return;

        if (spawnedPlayers.ContainsKey(playerId))
        {
            spawnedPlayers.Remove(playerId);
            Debug.Log($"GameManager: Unregistered player {playerId}");
            
            // Remove any target assignments involving this player
            RemovePlayerFromTargeting(playerId);
            
            // Reassign targets if needed
            if (spawnedPlayers.Count >= 2)
            {
                AssignAllTargets();
            }
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        // The actual player registration happens when PlayerController calls RegisterPlayer
        Debug.Log($"GameManager: Client connected: {clientId}");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        UnregisterPlayer(clientId);
    }

    #endregion

    #region Target Assignment

    // Assign targets for all players (circular assignment)
    private void AssignAllTargets()
    {
        if (!IsServer || spawnedPlayers.Count < 2) return;

        // Clear existing targets
        playerTargets.Clear();
        
        // Clear all pursuer lists
        foreach (var playerId in spawnedPlayers.Keys)
        {
            if (playerPursuers.ContainsKey(playerId))
            {
                playerPursuers[playerId].Clear();
            }
            else
            {
                playerPursuers[playerId] = new List<ulong>();
            }
        }

        // Get a list of all player IDs
        List<ulong> playerIds = new List<ulong>(spawnedPlayers.Keys);
        
        // Shuffle the list for more randomness
        ShuffleList(playerIds);
        
        // Assign targets in a circular pattern
        for (int i = 0; i < playerIds.Count; i++)
        {
            ulong currentPlayerId = playerIds[i];
            ulong targetPlayerId = playerIds[(i + 1) % playerIds.Count];
            
            AssignTarget(currentPlayerId, targetPlayerId);
        }
    }

    // Assign a specific target to a player
    private void AssignTarget(ulong playerId, ulong targetPlayerId)
    {
        if (playerId == targetPlayerId) return; // Prevent self-targeting
        
        // Update the player's target
        playerTargets[playerId] = targetPlayerId;
        
        // Update the target's pursuer list
        if (playerPursuers.ContainsKey(targetPlayerId))
        {
            if (!playerPursuers[targetPlayerId].Contains(playerId))
            {
                playerPursuers[targetPlayerId].Add(playerId);
            }
        }
        
        // Notify the player about their new target
        if (spawnedPlayers.TryGetValue(playerId, out PlayerController player))
        {
            player.SetTargetClientRpc(targetPlayerId);
        }
        
        // Notify the target about their new pursuer
        if (spawnedPlayers.TryGetValue(targetPlayerId, out PlayerController target))
        {
            target.UpdatePursuersClientRpc(playerPursuers[targetPlayerId].ToArray());
        }
        
        Debug.Log($"GameManager: Assigned player {playerId} to target player {targetPlayerId}");
    }

    // Called when a player successfully kills their target
    public void PlayerKilledTarget(ulong killerId, ulong targetId)
    {
        if (!IsServer) return;
        
        Debug.Log($"GameManager: Player {killerId} killed their target {targetId}");
        
        // Award a point or update score here
        
        // The target's target becomes the killer's new target
        if (playerTargets.TryGetValue(targetId, out ulong newTarget))
        {
            // Don't assign self as target
            if (newTarget != killerId)
            {
                AssignTarget(killerId, newTarget);
            }
            else
            {
                // Find a new target for the killer
                AssignNewTargetForPlayer(killerId);
            }
        }
        else
        {
            // If the target didn't have a target, find a new one
            AssignNewTargetForPlayer(killerId);
        }
        
        // Remove the killed target from all structures
        RemovePlayerFromTargeting(targetId);
    }

    // Remove a player from all targeting structures
    private void RemovePlayerFromTargeting(ulong playerId)
    {
        // Remove player's target
        playerTargets.Remove(playerId);
        
        // Remove player from all pursuer lists
        foreach (var targetId in playerPursuers.Keys)
        {
            playerPursuers[targetId].RemoveAll(p => p == playerId);
            
            // Notify the target about updated pursuer list
            if (spawnedPlayers.TryGetValue(targetId, out PlayerController target))
            {
                target.UpdatePursuersClientRpc(playerPursuers[targetId].ToArray());
            }
        }
        
        // Remove player's pursuer list
        playerPursuers.Remove(playerId);
    }

    // Assign a new target to a specific player
    private void AssignNewTargetForPlayer(ulong playerId)
    {
        if (spawnedPlayers.Count < 2) return;
        
        List<ulong> possibleTargets = new List<ulong>();
        foreach (var potentialTarget in spawnedPlayers.Keys)
        {
            if (potentialTarget != playerId) // Don't target self
            {
                possibleTargets.Add(potentialTarget);
            }
        }
        
        if (possibleTargets.Count > 0)
        {
            // Pick a random target from possible targets
            ulong newTarget = possibleTargets[Random.Range(0, possibleTargets.Count)];
            AssignTarget(playerId, newTarget);
        }
    }

    #endregion

    #region Game Flow Control

    // Start the game (called when ready to begin)
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        if (!IsServer || gameInProgress.Value) return;
        
        if (spawnedPlayers.Count >= 2)
        {
            gameTimeRemaining.Value = gameTimeInMinutes * 60f;
            gameInProgress.Value = true;
            
            // Assign initial targets
            AssignAllTargets();
            
            Debug.Log("GameManager: Game started!");
        }
        else
        {
            Debug.LogWarning("GameManager: Cannot start game, need at least 2 players.");
        }
    }

    // End the game
    private void EndGame()
    {
        if (!IsServer) return;
        
        gameInProgress.Value = false;
        
        // Determine winner based on scores
        // ...
        
        Debug.Log("GameManager: Game ended!");
    }

    // Get the remaining game time (for UI display)
    public float GetGameTimeRemaining()
    {
        return gameTimeRemaining.Value;
    }

    // Check if game is in progress
    public bool IsGameInProgress()
    {
        return gameInProgress.Value;
    }

    #endregion

    #region Utility Methods

    // Shuffle a list (Fisher-Yates algorithm)
    private void ShuffleList<T>(List<T> list)
    {
        int n = list.Count;
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    #endregion
} 