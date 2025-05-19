using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AI; // Added for NavMesh

public class NPCSpawner : MonoBehaviour
{
[Header("Spawner Settings")]
[Tooltip("The NPC prefabs to spawn. Make sure they have the NPCController and NetworkObject components.")]
public GameObject[] npcPrefabs;
[Tooltip("Maximum number of NPCs to spawn at game start.")]
public int maxNPCCount = 10;
[Tooltip("The maximum distance to search for a valid NavMesh point when spawning NPCs. Adjust this based on your NavMesh density and map size.")]
public float navMeshSampleDistance = 1000f; // Increased default for larger maps

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
Vector3 randomPos = GetRandomNavMeshPoint();
if (randomPos == Vector3.zero) // Check if a valid point was found
{
Debug.LogWarning($"NPCSpawner: Could not find a valid NavMesh point for NPC {i+1}. Skipping this NPC.");
continue;
}

Debug.Log($"NPCSpawner: Spawning NPC {i+1} at {randomPos}.");
// Select a random NPC prefab from the array
GameObject selectedPrefab = GetRandomNPCPrefab();
if (selectedPrefab == null)
{
Debug.LogError("NPCSpawner: Failed to select a valid NPC prefab.");
continue;
}
// Instantiate the NPC prefab at this position
GameObject npcInstance = Instantiate(selectedPrefab, randomPos, Quaternion.identity);
if (npcInstance == null)
{
Debug.LogError("NPCSpawner: Failed to instantiate NPC prefab.");
continue;
}
NetworkObject netObj = npcInstance.GetComponent<NetworkObject>();
if (netObj == null)
{
Debug.LogError("NPCSpawner: The instantiated NPC does not have a NetworkObject component!");
Destroy(npcInstance); // Clean up the instantiated object if it's not valid
continue;
}

// Spawn with server ownership
netObj.SpawnWithOwnership(NetworkManager.ServerClientId, true);
spawnedNPCs.Add(netObj); // Track this spawned NPC
Debug.Log("NPCSpawner: Successfully spawned NPC " + (i+1));
}
Debug.Log("NPCSpawner: Finished spawning NPCs.");
}

private Vector3 GetRandomNavMeshPoint()
{
// Define a very large area to sample from, effectively covering the whole map.
// You might want to adjust the center and extents if your map has a specific origin or bounds.
// For simplicity, we'll sample around the spawner's position with a large radius.
Vector3 randomDirection = Random.insideUnitSphere * navMeshSampleDistance;
randomDirection += transform.position; // Center the search around the spawner object

NavMeshHit navHit;
if (NavMesh.SamplePosition(randomDirection, out navHit, navMeshSampleDistance, NavMesh.AllAreas))
{
return navHit.position;
}
else
{
Debug.LogWarning("NPCSpawner: Failed to find a random point on the NavMesh. Returning Vector3.zero.");
return Vector3.zero; // Return zero vector if no point is found
}
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
