using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class GameManager : NetworkBehaviour
{
    [Header("Game Settings")]
    [SerializeField] private float targetAssignmentDelay = 5f;
    
    // Network variables for synchronizing game state
    private NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Dictionary to track which players are targeting which players
    // Key: Target player ID, Value: List of hunter player IDs
    private Dictionary<ulong, List<ulong>> playerTargets = new Dictionary<ulong, List<ulong>>();
    
    // Network variable to track target assignments (clientID -> targetID)
    // We'll use this structure: [hunterID1, targetID1, hunterID2, targetID2, etc.]
    private NetworkVariable<ulong[]> targetAssignments = new NetworkVariable<ulong[]>(new ulong[0],
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Singleton pattern for easy access
    public static GameManager Instance { get; private set; }
    
    // Methods for other scripts to get targeting information
    public ulong GetTargetForPlayer(ulong playerId) 
    {
        var assignments = targetAssignments.Value;
        for (int i = 0; i < assignments.Length; i += 2) 
        {
            if (assignments[i] == playerId && i + 1 < assignments.Length) 
            {
                return assignments[i + 1]; // Return the target ID
            }
        }
        return 0; // No target found (0 is not a valid client ID)
    }
    
    public int GetHunterCountForPlayer(ulong playerId)
    {
        if (playerTargets.TryGetValue(playerId, out var hunters))
        {
            return hunters.Count;
        }
        return 0;
    }
    
    public List<ulong> GetHuntersForPlayer(ulong playerId)
    {
        if (playerTargets.TryGetValue(playerId, out var hunters))
        {
            return hunters;
        }
        return new List<ulong>();
    }
    
    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Setup callbacks for the network variables
        targetAssignments.OnValueChanged += OnTargetAssignmentsChanged;
        gameStarted.OnValueChanged += OnGameStartedChanged;
        
        // If we're the server, start the game after a short delay
        if (IsServer || IsHost)
        {
            StartCoroutine(StartGameAfterDelay(1f));
        }
    }
    
    private IEnumerator StartGameAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Only the server can start the game
        if (IsServer || IsHost)
        {
            gameStarted.Value = true;
            
            // Start the target assignment after the specified delay
            StartCoroutine(AssignTargetsAfterDelay(targetAssignmentDelay));
        }
    }
    
    private IEnumerator AssignTargetsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Only the server assigns targets
        if (IsServer || IsHost)
        {
            AssignTargets();
        }
    }
    
    // Event handlers for network variables
    private void OnTargetAssignmentsChanged(ulong[] previousValue, ulong[] newValue)
    {
        // When target assignments change, update the local player targets dictionary
        UpdatePlayerTargetsFromAssignments(newValue);
        
        // Notify any listeners (like UI) that targets have been assigned
        OnTargetsAssigned();
    }
    
    private void OnGameStartedChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            Debug.Log("Game has started!");
        }
    }
    
    // Assign targets to players (server-only)
    private void AssignTargets()
    {
        if (!IsServer && !IsHost) return;
        
        List<ulong> players = NetworkManager.Singleton.ConnectedClientsIds.ToList();
        
        // Need at least 2 players to assign targets
        if (players.Count < 2)
        {
            Debug.LogWarning("Not enough players to assign targets");
            return;
        }
        
        // Shuffle the player list for random assignment
        players = players.OrderBy(x => Random.value).ToList();
        
        // Create circular targeting: player 0 targets player 1, player 1 targets player 2, ..., 
        // and the last player targets player 0
        ulong[] assignments = new ulong[players.Count * 2];
        
        for (int i = 0; i < players.Count; i++)
        {
            ulong hunterID = players[i];
            ulong targetID = players[(i + 1) % players.Count];
            
            assignments[i * 2] = hunterID;      // Hunter ID
            assignments[i * 2 + 1] = targetID;  // Target ID
            
            Debug.Log($"Assigned player {hunterID} to target player {targetID}");
        }
        
        // Set the network variable to broadcast to all clients
        targetAssignments.Value = assignments;
    }
    
    // Update the local player targets dictionary from assignments array
    private void UpdatePlayerTargetsFromAssignments(ulong[] assignments)
    {
        // Clear existing assignments
        playerTargets.Clear();
        
        // Process each hunter-target pair
        for (int i = 0; i < assignments.Length; i += 2)
        {
            if (i + 1 >= assignments.Length) break;
            
            ulong hunterID = assignments[i];
            ulong targetID = assignments[i + 1];
            
            // Make sure the target exists in the dictionary
            if (!playerTargets.ContainsKey(targetID))
            {
                playerTargets[targetID] = new List<ulong>();
            }
            
            // Add the hunter to the target's list
            playerTargets[targetID].Add(hunterID);
        }
        
        // Debug output for local player
        ulong localPlayerID = NetworkManager.Singleton.LocalClientId;
        
        // Log who the local player is targeting
        ulong localPlayerTarget = GetTargetForPlayer(localPlayerID);
        if (localPlayerTarget != 0)
        {
            Debug.Log($"You are targeting Player {localPlayerTarget}");
        }
        
        // Log how many players are targeting the local player
        if (playerTargets.TryGetValue(localPlayerID, out var hunters))
        {
            Debug.Log($"{hunters.Count} player(s) are targeting you: {string.Join(", ", hunters)}");
        }
        else
        {
            Debug.Log("No players are targeting you");
        }
    }
    
    // Event for when targets are assigned - other scripts can listen to this
    public delegate void TargetsAssignedDelegate();
    public event TargetsAssignedDelegate TargetsAssigned;
    
    private void OnTargetsAssigned()
    {
        TargetsAssigned?.Invoke();
    }
} 