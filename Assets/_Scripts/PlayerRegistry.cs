using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlayerRegistry : NetworkBehaviour
{
    [System.Serializable]
    public class PlayerData
    {
        public ulong clientId;
        public string playerName;
        public int avatarIndex;
    }
    
    [Header("Player Avatars")]
    [SerializeField] private Sprite[] availableAvatars;
    
    // Dictionary to store player data
    private Dictionary<ulong, PlayerData> playerDataDict = new Dictionary<ulong, PlayerData>();
    
    // Network Variables to sync player data 
    // Format: [clientId1, avatarIndex1, clientId2, avatarIndex2, ...]
    private NetworkVariable<ulong[]> playerAvatarAssignments = new NetworkVariable<ulong[]>(
        new ulong[0], NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Singleton pattern
    public static PlayerRegistry Instance { get; private set; }
    
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
        
        // Register callback for player avatar assignments
        playerAvatarAssignments.OnValueChanged += OnPlayerAvatarAssignmentsChanged;
        
        // Register for player connection/disconnection events
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        
        // If we're the server, assign random avatars to all existing players
        if (IsServer || IsHost)
        {
            // Assign avatars to any existing players
            AssignAvatarsToPlayers();
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        // Unregister callbacks
        playerAvatarAssignments.OnValueChanged -= OnPlayerAvatarAssignmentsChanged;
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    // Client connected event
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Player connected: {clientId}");
        
        if (IsServer || IsHost)
        {
            // When a new player connects, generate a new list of avatar assignments
            AssignAvatarsToPlayers();
        }
    }
    
    // Client disconnected event
    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Player disconnected: {clientId}");
        
        // Remove player from local registry
        if (playerDataDict.ContainsKey(clientId))
        {
            playerDataDict.Remove(clientId);
        }
        
        if (IsServer || IsHost)
        {
            // Update assignments after player disconnects
            AssignAvatarsToPlayers();
        }
    }
    
    // Server-only method to assign avatars to players
    private void AssignAvatarsToPlayers()
    {
        if (!IsServer && !IsHost) return;
        if (availableAvatars == null || availableAvatars.Length == 0) return;
        
        List<ulong> connectedClients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        List<ulong> assignmentData = new List<ulong>();
        
        // Clear dictionary first
        playerDataDict.Clear();
        
        // Assign avatars to each player
        for (int i = 0; i < connectedClients.Count; i++)
        {
            ulong clientId = connectedClients[i];
            
            // Create player data
            PlayerData playerData = new PlayerData
            {
                clientId = clientId,
                playerName = $"Player {i+1}",
                avatarIndex = i % availableAvatars.Length // Cycle through available avatars
            };
            
            // Add to dictionary
            playerDataDict[clientId] = playerData;
            
            // Add to network variable data
            assignmentData.Add(clientId);              // Client ID
            assignmentData.Add((ulong)playerData.avatarIndex);  // Avatar index (as ulong)
        }
        
        // Update the network variable
        playerAvatarAssignments.Value = assignmentData.ToArray();
    }
    
    // Callback when player avatar assignments change
    private void OnPlayerAvatarAssignmentsChanged(ulong[] previousValue, ulong[] newValue)
    {
        // Clear the dictionary
        playerDataDict.Clear();
        
        // Rebuild the dictionary from the network variable
        for (int i = 0; i < newValue.Length; i += 2)
        {
            if (i + 1 >= newValue.Length) break;
            
            ulong clientId = newValue[i];
            int avatarIndex = (int)newValue[i + 1];
            
            PlayerData playerData = new PlayerData
            {
                clientId = clientId,
                playerName = $"Player {clientId}",
                avatarIndex = avatarIndex
            };
            
            playerDataDict[clientId] = playerData;
        }
    }
    
    // Public methods to get player data
    
    public PlayerData GetPlayerData(ulong clientId)
    {
        if (playerDataDict.TryGetValue(clientId, out PlayerData data))
        {
            return data;
        }
        return null;
    }
    
    public Sprite GetPlayerAvatar(ulong clientId)
    {
        if (playerDataDict.TryGetValue(clientId, out PlayerData data))
        {
            if (availableAvatars != null && data.avatarIndex < availableAvatars.Length)
            {
                return availableAvatars[data.avatarIndex];
            }
        }
        
        // Return default avatar if available
        return availableAvatars != null && availableAvatars.Length > 0 ? availableAvatars[0] : null;
    }
    
    // Get all available avatars for reference
    public Sprite[] GetAvailableAvatars()
    {
        return availableAvatars;
    }
} 