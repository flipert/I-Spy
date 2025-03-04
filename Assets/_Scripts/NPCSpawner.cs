using UnityEngine;
using Unity.Netcode;
using System.Collections;
public class NPCSpawner : MonoBehaviour
{
[Header("Spawner Settings")]
[Tooltip("The NPC prefab to spawn. Make sure it has the NPCController and NetworkObject components.")]
public GameObject npcPrefab;
[Tooltip("Maximum number of NPCs to spawn at game start.")]
public int maxNPCCount = 10;
[Tooltip("Area in which to spawn NPCs. The NPCs will be spawned randomly within this area.")]
public Vector3 spawnAreaCenter = Vector3.zero;
[Tooltip("Size (width, height, depth) of the spawn area.")]
public Vector3 spawnAreaSize = new Vector3(20f, 0, 20f);
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
if (npcPrefab == null)
{
Debug.LogError("NPCSpawner: NPC prefab is not assigned!");
yield break;
}
// Spawn NPCs
for (int i = 0; i < maxNPCCount; i++)
{
Vector3 randomPos = spawnAreaCenter + new Vector3(
Random.Range(-spawnAreaSize.x / 2f, spawnAreaSize.x / 2f),
0,
Random.Range(-spawnAreaSize.z / 2f, spawnAreaSize.z / 2f)
);
Debug.Log($"NPCSpawner: Spawning NPC {i+1} at {randomPos}.");
// Instantiate the NPC prefab at this position
GameObject npcInstance = Instantiate(npcPrefab, randomPos, Quaternion.identity);
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
netObj.Spawn();
Debug.Log("NPCSpawner: Successfully spawned NPC " + (i+1));
}
Debug.Log("NPCSpawner: Finished spawning NPCs.");
}
}
