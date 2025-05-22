using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PlayerKill : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float killRange = 2f;
    [SerializeField] private LayerMask npcLayerMask;
    [SerializeField] private KeyCode killKey = KeyCode.Space; // Default to space bar

    [Header("References")]
    [SerializeField] private Animator playerAnimator; // Assign your player's animator

    private NPCController currentTargetNPC;
    private Collider[] nearbyNPCs = new Collider[5]; // Pre-allocate for minor optimization
    private bool isPerformingKill = false; // To prevent kill spam

    // Public flag for PlayerController to check
    public static bool IsKillAnimationPlaying { get; private set; } = false;

    void Start()
    {
        IsKillAnimationPlaying = false; // Ensure it's reset on start/spawn
        // Make sure we have the animator
        if (playerAnimator == null && IsOwner)
        {
            playerAnimator = GetComponentInChildren<Animator>();
            if (playerAnimator == null)
            {
                Debug.LogWarning("PlayerKill could not find an Animator component! Kill animations won't play.");
            }
        }
        
        // Log the layer mask to verify it's correct
        Debug.Log($"NPC Layer Mask: {LayerMaskToString(npcLayerMask)}");
    }

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

        // Don't find new targets if we're performing a kill
        if (isPerformingKill) return;

        int numFound = Physics.OverlapSphereNonAlloc(transform.position, killRange, nearbyNPCs, npcLayerMask);
        NPCController closestNPC = null;
        float closestDistanceSqr = killRange * killRange + 1; // Start with a value greater than max possible squared distance

        for (int i = 0; i < numFound; i++)
        {
            if (nearbyNPCs[i].TryGetComponent<NPCController>(out NPCController npc))
            {
                // Only consider NPCs that are alive
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

    private string GetAnimatorStateName(Animator anim)
    {
        if (anim == null || !anim.isInitialized || anim.runtimeAnimatorController == null) return "Animator_Not_Ready";
        if (anim.IsInTransition(0))
        {
            AnimatorStateInfo nextState = anim.GetNextAnimatorStateInfo(0);
            return $"Transitioning_To_State"; // Simplified, hash: {nextState.fullPathHash}
        }
        else
        {
            AnimatorClipInfo[] clipInfo = anim.GetCurrentAnimatorClipInfo(0);
            if (clipInfo.Length > 0 && clipInfo[0].clip != null)
            {
                return clipInfo[0].clip.name;
            }
            // Fallback if no clip name (e.g. empty state) orGetCurrentAnimatorStateInfo is needed
            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName("TheManInTheCoatKill")) return "TheManInTheCoatKill";
            if (stateInfo.IsName("TheManInTheCoatDeath")) return "TheManInTheCoatDeath";
            if (stateInfo.IsName("MainInCoatIdle")) return "MainInCoatIdle";
            if (stateInfo.IsName("ManInCoatRun")) return "ManInCoatRun";
            return $"UnknownState_Hash_{stateInfo.shortNameHash}";
        }
    }

    private IEnumerator CheckAnimationStateAfterTrigger(Animator anim, string expectedStateNameFragment, string triggerName)
    {
        if (anim == null) yield break;
        // Wait a frame for the animator to process the trigger
        yield return null; 
        string newState = GetAnimatorStateName(anim);
        Debug.Log($"Player Animator state 1 frame after '{triggerName}' trigger: {newState}. Expected to contain: '{expectedStateNameFragment}'");
        if (!newState.Contains(expectedStateNameFragment))
        {
            Debug.LogWarning($"Player Animator did NOT immediately transition to a state like '{expectedStateNameFragment}'. Current: {newState}");
        }
        
        yield return new WaitForSeconds(0.5f); // Wait a bit longer
        string delayedState = GetAnimatorStateName(anim);
        Debug.Log($"Player Animator state 0.5s after '{triggerName}' trigger: {delayedState}. Expected: '{expectedStateNameFragment}'");
        if (!delayedState.Contains(expectedStateNameFragment))
        {
            Debug.LogWarning($"Player Animator STILL not in a state like '{expectedStateNameFragment}' after 0.5s. Current: {delayedState}");
        }
    }

    void HandleKillInput()
    {
        if (currentTargetNPC != null && Input.GetKeyDown(killKey) && !isPerformingKill)
        {
            Debug.Log($"Kill input detected for target: {currentTargetNPC.name}");
            
            if (playerAnimator != null)
            {
                // Check if the Kill trigger exists
                AnimatorControllerParameter[] parameters = playerAnimator.parameters;
                bool hasKillTrigger = false;
                foreach (AnimatorControllerParameter param in parameters)
                {
                    if (param.name == "Kill" && param.type == AnimatorControllerParameterType.Trigger)
                    {
                        hasKillTrigger = true;
                        break;
                    }
                }
                
                Debug.Log($"Player Animator current state BEFORE 'Kill' trigger: {GetAnimatorStateName(playerAnimator)}. Has Kill trigger: {hasKillTrigger}. Setting trigger now.");
                
                playerAnimator.SetTrigger("Kill");
                StartCoroutine(CheckAnimationStateAfterTrigger(playerAnimator, "TheManInTheCoatKill", "Kill"));

                isPerformingKill = true;
                IsKillAnimationPlaying = true; // Set flag here
                
                float killAnimationDuration = 0f;
                // Attempt to get kill animation duration
                RuntimeAnimatorController ac = playerAnimator.runtimeAnimatorController;
                foreach (AnimationClip clip in ac.animationClips) {
                    if (clip.name.Contains("TheManInTheCoatKill")) { 
                        killAnimationDuration = clip.length;
                        Debug.Log($"PlayerKill: Found Kill animation '{clip.name}' with duration: {killAnimationDuration}");
                        break;
                    }
                }
                if (killAnimationDuration == 0f) {
                    Debug.LogWarning("PlayerKill: Could not find Kill animation clip length, defaulting to 1.5s.");
                    killAnimationDuration = 1.5f; // Fallback
                }
                StartCoroutine(ResetKillState(killAnimationDuration));
            }
            else
            {
                Debug.LogError("Player animator is null! Cannot play kill animation.");
                // Still call server RPC if animator is missing but kill is intended
                isPerformingKill = true; // Prevent spam
                IsKillAnimationPlaying = true; // Set flag here
                StartCoroutine(ResetKillState(1.5f)); // Use a default duration
            }
            
            // Initiate the kill on the server
            InitiateKillServerRpc(currentTargetNPC.NetworkObject);
        }
    }

    private System.Collections.IEnumerator ResetKillState(float delay)
    {
        Debug.Log($"Player kill animation in progress, will complete in {delay} seconds");
        yield return new WaitForSeconds(delay);
        Debug.Log("Player kill animation complete, resetting state");
        isPerformingKill = false;
        IsKillAnimationPlaying = false; // Reset flag here
    }

    [ServerRpc(RequireOwnership = true)]
    private void InitiateKillServerRpc(NetworkObjectReference targetNpcRef)
    {
        if (targetNpcRef.TryGet(out NetworkObject targetNpcNetworkObject))
        {
            if (targetNpcNetworkObject.TryGetComponent<NPCController>(out NPCController npcToKill))
            {
                // Optional: Add a distance check here on the server for security
                float distanceToTarget = Vector3.Distance(transform.position, npcToKill.transform.position);
                if (distanceToTarget > killRange + 0.5f) // Add a small buffer for leniency
                {
                   Debug.LogWarning($"Player {OwnerClientId} tried to kill NPC from too far. Dist: {distanceToTarget}");
                   return;
                }

                // Tell all clients to play player's kill animation
                PlayPlayerKillAnimationClientRpc();

                // Tell the NPC to die
                npcToKill.KillNPCServerRpc();
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

    // Helper method to convert layer mask to readable string
    private string LayerMaskToString(LayerMask mask)
    {
        var layers = "";
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                layers += LayerMask.LayerToName(i) + ", ";
            }
        }
        return layers.TrimEnd(',', ' ');
    }
} 