using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject fallbackPlayerPrefab; // Fallback prefab if no selection
    [SerializeField] private Transform[] spawnPoints;
    
    // Singleton instance
    public static PlayerSpawner Instance { get; private set; }
    
    private Dictionary<ulong, GameObject> spawnedPlayers = new Dictionary<ulong, GameObject>();
    private bool isGameScene = false;
    
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

        // Setup event listeners
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    private void Start()
    {
        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
        {
            // Server setup
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
        
        // Determine if we're in the game scene - but don't spawn players yet
        // We'll wait for the scene load event
        isGameScene = SceneManager.GetActiveScene().name == "Game"; // Update this with your game scene name
        Debug.Log($"PlayerSpawner: Start() - Current scene is {SceneManager.GetActiveScene().name}, isGameScene = {isGameScene}");
    }
    
    private void OnDestroy()
    {
        // Clean up event listeners
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"PlayerSpawner: Scene loaded - {scene.name}");
        
        // Update scene state
        bool wasGameScene = isGameScene;
        isGameScene = scene.name == "Game"; // Update this with your game scene name
        
        // Only spawn players if we've just entered the Game scene
        if (isGameScene && !wasGameScene && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
        {
            Debug.Log("PlayerSpawner: Game scene loaded, spawning players after delay");
            // Delay spawning slightly to ensure everything is ready
            StartCoroutine(SpawnPlayersDelayed());
        }
    }
    
    private IEnumerator SpawnPlayersDelayed()
    {
        // Wait for a frame to ensure everything is initialized
        yield return null;
        
        // Spawn for all connected clients
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            // Skip if already spawned in this scene
            if (!spawnedPlayers.ContainsKey(clientId) || spawnedPlayers[clientId] == null)
            {
                SpawnPlayerForClient(clientId);
            }
        }
    }
    
    private void OnServerStarted()
    {
        Debug.Log("PlayerSpawner: Server started");
        // Don't spawn players here, wait for game scene to load
    }
    
    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
        {
            Debug.Log($"PlayerSpawner: Client connected: {clientId}");
            
            // Only spawn players if we're in the game scene
            if (isGameScene)
            {
                SpawnPlayerForClient(clientId);
            }
        }
    }
    
    private void SpawnPlayerForClient(ulong clientId)
    {
        // Triple-check we're in the game scene
        if (!isGameScene || SceneManager.GetActiveScene().name != "Game") // Update this with your game scene name
        {
            Debug.Log($"PlayerSpawner: Not in game scene, skipping player spawn for client {clientId}");
            return;
        }
        
        if (spawnedPlayers.ContainsKey(clientId) && spawnedPlayers[clientId] != null)
        {
            Debug.Log($"PlayerSpawner: Player for client {clientId} already spawned");
            return;
        }
        
        Transform spawnPoint = GetRandomSpawnPoint();
        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        
        // Get the character selection from NetworkManagerUI
        GameObject characterPrefab = null;
        
        // First try to find NetworkManagerUI if it's not already accessible
        if (NetworkManagerUI.Instance == null)
        {
            // Try to find it in the scene
            NetworkManagerUI foundUI = FindObjectOfType<NetworkManagerUI>();
            if (foundUI != null)
            {
                Debug.Log("PlayerSpawner: Found NetworkManagerUI in scene");
                // This shouldn't be necessary if Singleton pattern works correctly,
                // but just in case there's an issue with the singleton implementation
                DontDestroyOnLoad(foundUI.gameObject);
            }
            else
            {
                Debug.LogWarning("PlayerSpawner: NetworkManagerUI not found in scene. Will use fallback player prefab.");
            }
        }
        
        // Now try to get character selection
        if (NetworkManagerUI.Instance != null)
        {
            // Get the character selection for this client
            int characterIndex = NetworkManagerUI.Instance.GetClientCharacterIndex(clientId);
            Debug.Log($"PlayerSpawner: Character index for client {clientId} is {characterIndex}");
            
            // Get the prefab based on the character index
            GameObject[] availablePrefabs = NetworkManagerUI.Instance.CharacterPrefabs;
            
            if (availablePrefabs != null && characterIndex >= 0 && characterIndex < availablePrefabs.Length)
            {
                characterPrefab = availablePrefabs[characterIndex];
                Debug.Log($"PlayerSpawner: Selected character prefab {characterPrefab.name} for client {clientId}");
            }
            else
            {
                Debug.LogWarning($"PlayerSpawner: Invalid character index ({characterIndex}) or no prefabs available for client {clientId}. Using fallback.");
            }
        }
        else
        {
            Debug.LogError("PlayerSpawner: NetworkManagerUI.Instance is null! Cannot get character selection.");
        }
        
        // Fallback to default prefab if no selection or error occurred
        if (characterPrefab == null)
        {
            // Last resort - try to find any player prefab in Resources
            characterPrefab = Resources.Load<GameObject>("PlayerPrefabs/DefaultPlayer");
            
            if (characterPrefab != null)
            {
                Debug.Log("PlayerSpawner: Found fallback player prefab in Resources folder");
            }
            else if (fallbackPlayerPrefab != null)
            {
                Debug.Log("PlayerSpawner: Using explicitly assigned fallback player prefab");
                characterPrefab = fallbackPlayerPrefab;
            }
            else
            {
                Debug.LogError("PlayerSpawner: No fallback player prefab assigned!");
                return;
            }
        }
        
        Debug.Log($"PlayerSpawner: Spawning character '{characterPrefab.name}' for client {clientId} at {spawnPosition}");
        
        // Instantiate the player
        GameObject playerObj = Instantiate(characterPrefab, spawnPosition, spawnRotation);
        NetworkObject networkObject = playerObj.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Spawn on the network and assign ownership
            networkObject.SpawnAsPlayerObject(clientId);
            spawnedPlayers[clientId] = playerObj;
            Debug.Log($"PlayerSpawner: Player spawned and assigned to client {clientId}");
        }
        else
        {
            Debug.LogError("PlayerSpawner: Player prefab missing NetworkObject component!");
            Destroy(playerObj);
        }
    }
    
    private Transform GetRandomSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("PlayerSpawner: No spawn points assigned!");
            return null;
        }
        
        return spawnPoints[Random.Range(0, spawnPoints.Length)];
    }
} 