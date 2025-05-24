using UnityEngine;
using Unity.Netcode;
using System.Collections;
using UnityEngine.UI; // Required for UI elements

public class PlayerKill : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float killRange = 2f;
    [SerializeField] private LayerMask npcLayerMask;
    [SerializeField] private KeyCode killKey = KeyCode.Space; // Default to space bar
    
    [Header("Ranged Kill Settings")]
    [SerializeField] private KeyCode aimKey = KeyCode.Tab;
    [SerializeField] private float aimRange = 10f; // Max distance for aim point calculation
    [SerializeField] private GameObject crosshairPrefab; // Assign UI crosshair prefab in inspector
    private Canvas uiCanvas;
    [SerializeField] private float shootCooldown = 0.5f; // Time between shots

    [Header("References")]
    [SerializeField] private Animator playerAnimator; // Assign your player's animator

    private NPCController currentTargetNPC;
    private Collider[] nearbyNPCs = new Collider[5]; // Pre-allocate for minor optimization
    private bool isPerformingKill = false; // To prevent kill spam

    // Public flags for PlayerController to check
    public static bool IsKillAnimationPlaying { get; private set; } = false;
    public static bool IsAiming { get; private set; } = false;

    // Ranged kill variables
    private GameObject crosshairInstance;
    private bool isInAimMode = false;
    private float lastShotTime = 0f;
    
    // The world position the crosshair represents, for raycasting
    private Vector3 currentAimWorldPosition;

    void Start()
    {
        IsKillAnimationPlaying = false; // Ensure it's reset on start/spawn
        IsAiming = false;
        
        // Make sure we have the animator
        if (playerAnimator == null && IsOwner)
        {
            playerAnimator = GetComponentInChildren<Animator>();
            if (playerAnimator == null)
            {
                Debug.LogWarning("PlayerKill could not find an Animator component! Kill animations won't play.");
            }
        }
        
        // Try to find the UI Canvas by name if not assigned
        if (uiCanvas == null && IsOwner)
        {
            GameObject canvasObject = GameObject.Find("Canvas"); // Find the Canvas by name
            if (canvasObject != null)
            {
                uiCanvas = canvasObject.GetComponent<Canvas>();
                if (uiCanvas == null)
                {
                    Debug.LogWarning("PlayerKill found GameObject named 'Canvas' but it lacks a Canvas component!");
                }
            }
            else
            {
                Debug.LogWarning("PlayerKill could not find a GameObject named 'Canvas'! Crosshair functionality will be limited.");
            }
        }
        
        // Log the layer mask to verify it's correct
        Debug.Log($"NPC Layer Mask: {LayerMaskToString(npcLayerMask)}");
    }

    void Update()
    {
        if (!IsOwner) return; // Only the owner player can initiate kills

        // Handle aim mode toggle
        if (Input.GetKeyDown(aimKey) && !isPerformingKill)
        {
            ToggleAimMode();
        }

        // Handle aiming
        if (isInAimMode)
        {
            UpdateCrosshairPosition();
            
            // Handle shooting
            if (Input.GetMouseButtonDown(0) && Time.time - lastShotTime >= shootCooldown)
            {
                Shoot();
            }
            
            // Cancel aim mode with right click or pressing aim key again
            if (Input.GetMouseButtonDown(1))
            {
                ExitAimMode();
            }
        }
        else
        {
            // Normal melee kill handling
            FindTargetNPC();
            HandleKillInput();
        }
    }

    private void ToggleAimMode()
    {
        if (isInAimMode)
        {
            ExitAimMode();
        }
        else
        {
            EnterAimMode();
        }
    }

    private void EnterAimMode()
    {
        isInAimMode = true;
        IsAiming = true;
        
        // Hide melee kill prompt if any
        if (currentTargetNPC != null)
        {
            currentTargetNPC.ShowKillPrompt(false);
        }
        
        // Create crosshair
        if (crosshairPrefab != null)
        {
            // Instantiate as a child of the UI Canvas
            if (uiCanvas != null)
            {
                 crosshairInstance = Instantiate(crosshairPrefab, uiCanvas.transform);
                 // Initial position doesn't matter much as it's updated immediately
                 crosshairInstance.transform.localPosition = Vector3.zero;
            }
            else
            {
                Debug.LogWarning("PlayerKill: Cannot instantiate UI crosshair without a UI Canvas reference.");
                 // Fallback to a simple 3D indicator if no canvas is found?
                 // Or maybe just log error and don't show crosshair.
                 // For now, let's just not create it if canvas is missing.
            }
        }
        else
        {
            Debug.LogWarning("PlayerKill: No Crosshair Prefab assigned for ranged kill.");
            // Don't create a default primitive sphere if we want UI crosshair
        }
        
        // Set aiming animation
        if (playerAnimator != null)
        {
            playerAnimator.SetBool("IsAiming", true);
        }
        
        Debug.Log("Entered aim mode");
    }

    private void ExitAimMode()
    {
        isInAimMode = false;
        IsAiming = false;
        
        // Destroy crosshair
        if (crosshairInstance != null)
        {
            Destroy(crosshairInstance);
        }
        
        // Stop aiming animation
        if (playerAnimator != null)
        {
            playerAnimator.SetBool("IsAiming", false);
        }
        
        Debug.Log("Exited aim mode");
    }

    private void UpdateCrosshairPosition()
    {
        if (crosshairInstance == null || Camera.main == null) return;
        
        // Calculate the world position the mouse is aiming at
        Camera mainCamera = Camera.main;
        Plane playerPlane = new Plane(Vector3.up, transform.position);
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        
        float enter = 0f;
        Vector3 targetWorldPosition = transform.position + Vector3.forward * aimRange; // Default if raycast misses

        if (playerPlane.Raycast(ray, out enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            Vector3 direction = (hitPoint - transform.position).normalized;
            
            // Keep the target position at a fixed distance from the player within the aimRange
            targetWorldPosition = transform.position + direction * aimRange;
            // Ensure the target position is at player's height + a small offset
            targetWorldPosition.y = transform.position.y + 1f; 
        }
        
        // Store the calculated world position for shooting
        currentAimWorldPosition = targetWorldPosition;
        
        // Now, convert the world position to screen position to place the UI crosshair
        Vector3 screenPosition = mainCamera.WorldToScreenPoint(targetWorldPosition);
        
        // Position the UI crosshair
        // Assuming the UI crosshair prefab is a RectTransform and the canvas is set up correctly
        if (crosshairInstance.transform is RectTransform)
        {
             RectTransform canvasRect = uiCanvas.GetComponent<RectTransform>();
             Vector2 viewportPosition = mainCamera.WorldToViewportPoint(targetWorldPosition);
             Vector2 canvasPosition = new Vector2(
                 ((viewportPosition.x * canvasRect.sizeDelta.x) - (canvasRect.sizeDelta.x * 0.5f)),
                 ((viewportPosition.y * canvasRect.sizeDelta.y) - (canvasRect.sizeDelta.y * 0.5f)));
             
             crosshairInstance.GetComponent<RectTransform>().anchoredPosition = canvasPosition;
        }
        else
        {
            // Fallback for non-RectTransform UI elements (less common)
            // This might not position it correctly depending on UI setup
            crosshairInstance.transform.position = screenPosition;
        }

        // Make player face the crosshair direction (based on the calculated world position)
        Vector3 lookDirection = (currentAimWorldPosition - transform.position).normalized;
        transform.rotation = Quaternion.LookRotation(new Vector3(lookDirection.x, 0, lookDirection.z));
    }

    private void Shoot()
    {
        lastShotTime = Time.time;
        
        // Trigger shooting animation
        if (playerAnimator != null)
        {
            playerAnimator.SetTrigger("Shoot");
        }
        
        // Check if crosshair is over an NPC
        if (crosshairInstance != null)
        {
            // Cast a ray from the camera through the UI crosshair's screen position
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;

            // Use the stored world position for the raycast origin and direction from player
            Vector3 rayStart = transform.position + Vector3.up; // Start from player's chest height
            Vector3 rayDirection = (currentAimWorldPosition - rayStart).normalized;
            
            RaycastHit hit;
            if (Physics.Raycast(rayStart, rayDirection, out hit, aimRange, npcLayerMask))
            {
                NPCController npc = hit.collider.GetComponent<NPCController>();
                if (npc != null)
                {
                    Debug.Log($"Shot hit NPC: {npc.name}");
                    
                    // Initiate kill on server
                    InitiateRangedKillServerRpc(npc.NetworkObject);
                    
                    // Exit aim mode after successful shot
                    ExitAimMode();
                }
            }
            else
            {
                Debug.Log("Shot missed");
            }
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void InitiateRangedKillServerRpc(NetworkObjectReference targetNpcRef)
    {
        if (targetNpcRef.TryGet(out NetworkObject targetNpcNetworkObject))
        {
            if (targetNpcNetworkObject.TryGetComponent<NPCController>(out NPCController npcToKill))
            {
                // Tell all clients to play shooting animation
                PlayShootAnimationClientRpc();
                
                // Kill the NPC
                npcToKill.KillNPCServerRpc();
            }
        }
    }

    [ClientRpc]
    private void PlayShootAnimationClientRpc()
    {
        if (playerAnimator != null)
        {
            playerAnimator.SetTrigger("Shoot");
        }
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