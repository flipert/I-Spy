using UnityEngine;
using Unity.Netcode;

public class PlayerKill : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float killRange = 2f;
    [SerializeField] private LayerMask npcLayerMask;
    [SerializeField] private KeyCode killKey = KeyCode.Space; // Allow customizing the key

    [Header("References")]
    [SerializeField] private Animator playerAnimator; // Assign your player's animator

    private NPCController currentTargetNPC;
    private Collider[] nearbyNPCs = new Collider[5]; // Pre-allocate for minor optimization

    void Update()
    {
        if (!IsOwner) return; // Only the owner player can initiate kills

        FindTargetNPC();
        HandleKillInput();
    }

    void FindTargetNPC()
    {
        // Clear previous target's prompt if it's no longer the target or out of range
        if (currentTargetNPC != null)
        {
            // Check if currentTargetNPC is still valid and in range
            float distanceToCurrentTarget = Vector3.Distance(transform.position, currentTargetNPC.transform.position);
            if (distanceToCurrentTarget > killRange || !currentTargetNPC.gameObject.activeInHierarchy)
            {
                currentTargetNPC.ShowKillPrompt(false);
                currentTargetNPC = null;
            }
        }

        int numFound = Physics.OverlapSphereNonAlloc(transform.position, killRange, nearbyNPCs, npcLayerMask);
        NPCController closestNPC = null;
        float closestDistanceSqr = killRange * killRange + 1; // Start with a value greater than max possible squared distance

        for (int i = 0; i < numFound; i++)
        {
            if (nearbyNPCs[i].TryGetComponent<NPCController>(out NPCController npc))
            {
                // Potentially add a check here: if (!npc.IsDead()) or similar
                float distanceSqr = (transform.position - npc.transform.position).sqrMagnitude;
                if (distanceSqr < closestDistanceSqr)
                {
                    closestDistanceSqr = distanceSqr;
                    closestNPC = npc;
                }
            }
        }

        if (closestNPC != null)
        {
            if (currentTargetNPC != closestNPC)
            {
                // New target found, or previous one was cleared
                if (currentTargetNPC != null)
                {
                    currentTargetNPC.ShowKillPrompt(false); // Hide prompt on old target
                }
                currentTargetNPC = closestNPC;
                currentTargetNPC.ShowKillPrompt(true); // Show prompt on new target
            }
            // If currentTargetNPC is already closestNPC, its prompt is already (or should be) visible
        }
        else if (currentTargetNPC != null)
        {
            // No NPC in range, but we had a target, so hide its prompt
            currentTargetNPC.ShowKillPrompt(false);
            currentTargetNPC = null;
        }
    }

    void HandleKillInput()
    {
        if (currentTargetNPC != null && Input.GetKeyDown(killKey))
        {
            // Check if we have authority to prevent client directly calling RPC on NPC
            // The player should request its own server-side component to do the kill
            if (playerAnimator != null)
            {
                // Assuming a "Kill" trigger or state in the player's animator
                playerAnimator.SetTrigger("Kill"); 
            }
            InitiateKillServerRpc(currentTargetNPC.NetworkObject);
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void InitiateKillServerRpc(NetworkObjectReference targetNpcRef)
    {
        if (targetNpcRef.TryGet(out NetworkObject targetNpcNetworkObject))
        {
            if (targetNpcNetworkObject.TryGetComponent<NPCController>(out NPCController npcToKill))
            {
                // Optional: Add a distance check here on the server for security
                // float distanceToTarget = Vector3.Distance(transform.position, npcToKill.transform.position);
                // if (distanceToTarget > killRange + 0.5f) // Add a small buffer for leniency
                // {
                //    Debug.LogWarning($"Player {OwnerClientId} tried to kill NPC from too far. Dist: {distanceToTarget}");
                //    return;
                // }

                // Trigger player's animation on server and then on clients
                // This ensures the player animation is also seen by others
                if (playerAnimator != null)
                {
                     // The animator on the server instance might not be the one directly controlling the visual
                     // if it's driven by client-auth movement.
                     // For kill animation, it's safer to trigger it via ClientRpc if player animations are complex.
                     // However, if simple, server can set and it replicates.
                     // Let's trigger a ClientRpc for the player's animation for better sync.
                }
                PlayPlayerKillAnimationClientRpc(); // Tell clients to play player's kill animation

                npcToKill.KillNPCServerRpc(); // Tell the NPC to die
            }
        }
    }

    [ClientRpc]
    private void PlayPlayerKillAnimationClientRpc()
    {
        if (playerAnimator != null)
        {
            playerAnimator.SetTrigger("Kill");
        }
    }
} 