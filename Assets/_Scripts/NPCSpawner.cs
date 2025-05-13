using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class NPCSpawner : MonoBehaviour
{
[Header("Spawner Settings")]
[Tooltip("The NPC prefabs to spawn. Make sure they have the NPCController and NetworkObject components.")]
public GameObject[] npcPrefabs;
[Tooltip("Maximum number of NPCs to spawn at game start.")]
public int maxNPCCount = 10;
[Tooltip("Area in which to spawn NPCs. The NPCs will be spawned randomly within this area.")]
public Vector3 spawnAreaCenter = Vector3.zero;
[Tooltip("Size (width, height, depth) of the spawn area.")]
public Vector3 spawnAreaSize = new Vector3(20f, 0, 20f);

[Header("Collision Settings")]
[Tooltip("Layer mask for buildings and obstacles to avoid when spawning")]
public LayerMask buildingLayerMask;
[Tooltip("Minimum distance to keep from buildings when spawning")]
public float minDistanceFromBuildings = 2.0f;
[Tooltip("Maximum attempts to find a valid spawn position")]
public int maxSpawnAttempts = 30;

// List to track spawned NPCs
private List<NetworkObject> spawnedNPCs = new List<NetworkObject>();

private IEnumerator Start()
{
Debug.Log("NPCSpawner: Start() called.");
if (NetworkManager.Singleton == null)
{
Debug.LogError("NPCSpawner: NetworkManager not found!");
yield break;
}
else
{
Debug.Log("NPCSpawner: Found NetworkManager.");
}
// Instead of auto-starting the host, wait until NetworkManager is listening.
Debug.Log("NPCSpawner: Waiting for NetworkManager to start listening (i.e., host started)...");
yield return new WaitUntil(() => NetworkManager.Singleton.IsListening);
Debug.Log("NPCSpawner: NetworkManager is now listening. (Running as server/host)");
// Wait until the player is spawned (assuming player GameObjects are tagged as 'Player')
Debug.Log("NPCSpawner: Waiting for the player to spawn...");
yield return new WaitUntil(() => GameObject.FindWithTag("Player") != null);
Debug.Log("NPCSpawner: Player found. Proceeding with NPC spawn.");

// Register for client connection events to handle late joiners
NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

if (npcPrefabs == null || npcPrefabs.Length == 0)
{
Debug.LogError("NPCSpawner: No NPC prefabs are assigned!");
yield break;
}
// Spawn NPCs
for (int i = 0; i < maxNPCCount; i++)
{
// Find a valid spawn position that's not inside or too close to buildings
Vector3 spawnPosition = FindValidSpawnPosition();
            
if (spawnPosition == Vector3.zero)
{
Debug.LogWarning($"NPCSpawner: Failed to find valid spawn position for NPC {i+1} after {maxSpawnAttempts} attempts. Skipping.");
continue;
}
            
Debug.Log($"NPCSpawner: Spawning NPC {i+1} at {spawnPosition}.");
// Select a random NPC prefab from the array
GameObject selectedPrefab = GetRandomNPCPrefab();
if (selectedPrefab == null)
{
Debug.LogError("NPCSpawner: Failed to select a valid NPC prefab.");
continue;
}
// Instantiate the NPC prefab at this position
GameObject npcInstance = Instantiate(selectedPrefab, spawnPosition, Quaternion.identity);
if (npcInstance == null)
{
Debug.LogError("NPCSpawner: Failed to instantiate NPC prefab.");
continue;
}
NetworkObject netObj = npcInstance.GetComponent<NetworkObject>();
if (netObj == null)
{
Debug.LogError("NPCSpawner: The instantiated NPC does not have a NetworkObject component!");
continue;
}

// Spawn with server ownership
netObj.SpawnWithOwnership(NetworkManager.ServerClientId, true);
spawnedNPCs.Add(netObj); // Track this spawned NPC
Debug.Log("NPCSpawner: Successfully spawned NPC " + (i+1));
}
Debug.Log("NPCSpawner: Finished spawning NPCs.");
}

// Handle late-joining clients
private void OnClientConnected(ulong clientId)
{
    // Skip if it's the server/host client
    if (clientId == NetworkManager.ServerClientId)
        return;
        
    Debug.Log($"NPCSpawner: New client connected (ID: {clientId}). Ensuring all NPCs are visible.");
    
    // We don't need to do anything special here as NetworkObjects should be 
    // automatically synchronized to new clients, but we can log for debugging
    Debug.Log($"NPCSpawner: Client {clientId} should see {spawnedNPCs.Count} NPCs");
}

private void OnDestroy()
{
    // Clean up callback when this object is destroyed
    if (NetworkManager.Singleton != null)
    {
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }
}

// Find a valid spawn position that's not inside or too close to buildings
private Vector3 FindValidSpawnPosition()
{
    int attempts = 0;
    
    while (attempts < maxSpawnAttempts)
    {
        // Generate a random position within the spawn area
        Vector3 randomPos = spawnAreaCenter + new Vector3(
            Random.Range(-spawnAreaSize.x / 2f, spawnAreaSize.x / 2f),
            0,
            Random.Range(-spawnAreaSize.z / 2f, spawnAreaSize.z / 2f)
        );
        
        // Check if the position is valid (not inside or too close to buildings)
        if (IsValidSpawnPosition(randomPos))
        {
            return randomPos;
        }
        
        attempts++;
    }
    
    // If we couldn't find a valid position after maximum attempts, return zero vector
    return Vector3.zero;
}

// Check if a position is valid for spawning (not inside or too close to buildings)
private bool IsValidSpawnPosition(Vector3 position)
{
    // Check if position is inside any building collider
    Collider[] buildingColliders = Physics.OverlapSphere(position, 0.5f, buildingLayerMask);
    if (buildingColliders.Length > 0)
    {
        // Position is inside a building
        return false;
    }
    
    // Check if position is too close to any building
    buildingColliders = Physics.OverlapSphere(position, minDistanceFromBuildings, buildingLayerMask);
    if (buildingColliders.Length > 0)
    {
        // Position is too close to a building
        return false;
    }
    
    // Position is valid
    return true;
}

// Returns a random NPC prefab from the array
private GameObject GetRandomNPCPrefab()
{
if (npcPrefabs == null || npcPrefabs.Length == 0)
    return null;
    
// Get a random index within the array bounds
int randomIndex = Random.Range(0, npcPrefabs.Length);
    
// Make sure the selected prefab is valid
if (npcPrefabs[randomIndex] == null)
{
    // If the randomly selected prefab is null, try to find any valid prefab
    for (int i = 0; i < npcPrefabs.Length; i++)
    {
        if (npcPrefabs[i] != null)
            return npcPrefabs[i];
    }
    return null; // No valid prefabs found
}
    
return npcPrefabs[randomIndex];
}
}
