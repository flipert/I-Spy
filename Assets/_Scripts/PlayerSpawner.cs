using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;
    
    // Keep track of spawned players
    private Dictionary<ulong, GameObject> spawnedPlayers = new Dictionary<ulong, GameObject>();
    
    private void Awake()
    {
        // Make sure this object persists
        DontDestroyOnLoad(this.gameObject);
    }
    
    private void Start()
    {
        Debug.Log("PlayerSpawner: Start");
        
        // Subscribe to network events when the NetworkManager is ready
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("PlayerSpawner: NetworkManager found, subscribing to events");
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            
            // Also subscribe to client connected events right away if we're already a server
            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
            {
                Debug.Log("PlayerSpawner: We are already server/host, subscribing to client connections");
                NetworkManager.Singleton.OnClientConnectedCallback += SpawnPlayerForClient;
            }
        }
        else
        {
            Debug.LogError("PlayerSpawner: NetworkManager.Singleton is null!");
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= SpawnPlayerForClient;
        }
    }
    
    private void OnServerStarted()
    {
        // Only the server should handle spawning
        if (NetworkManager.Singleton.IsServer)
        {
            Debug.Log("PlayerSpawner: Server started, subscribing to client connections");
            
            // Subscribe to the client connected event
            NetworkManager.Singleton.OnClientConnectedCallback += SpawnPlayerForClient;
            
            // If we're the host, spawn our own player
            if (NetworkManager.Singleton.IsHost)
            {
                Debug.Log("PlayerSpawner: We are the host, spawning our player");
                SpawnPlayerForClient(NetworkManager.Singleton.LocalClientId);
            }
        }
    }
    
    private void SpawnPlayerForClient(ulong clientId)
    {
        Debug.Log($"PlayerSpawner: SpawnPlayerForClient called for client {clientId}");
        
        // Check if we've already spawned a player for this client
        if (spawnedPlayers.ContainsKey(clientId))
        {
            Debug.LogWarning($"PlayerSpawner: Player already spawned for client {clientId}");
            return;
        }
        
        // Choose a spawn point
        Transform spawnPoint = GetRandomSpawnPoint();
        
        Debug.Log($"PlayerSpawner: Spawning player for client {clientId} at position {spawnPoint.position}");
        
        // Instantiate the player prefab
        GameObject playerInstance = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        
        // Make the player a network object and assign ownership to the client
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            // Spawn the player as a player object owned by this client
            networkObject.SpawnAsPlayerObject(clientId);
            
            // Track this spawned player
            spawnedPlayers[clientId] = playerInstance;
            
            Debug.Log($"PlayerSpawner: Successfully spawned player for client {clientId}");
        }
        else
        {
            Debug.LogError("PlayerSpawner: Player prefab does not have a NetworkObject component!");
            Destroy(playerInstance);
        }
    }
    
    private Transform GetRandomSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            // If no spawn points are set, use a default position
            Debug.LogWarning("PlayerSpawner: No spawn points set, using PlayerSpawner transform");
            return transform;
        }
        
        // Choose a random spawn point
        int randomIndex = Random.Range(0, spawnPoints.Length);
        return spawnPoints[randomIndex];
    }
} 