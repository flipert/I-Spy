using UnityEngine;
using Unity.Netcode;
using System.Collections;
using UnityEngine.UI; // Required for UI elements

public class PlayerKill : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float killRange = 2f;
    [SerializeField] private LayerMask meleeTargetLayerMask; // Renamed for clarity
    [SerializeField] private LayerMask rangedTargetLayerMask; // New mask for ranged attacks
    [SerializeField] private KeyCode killKey = KeyCode.Space; // Default to space bar
    
    [Header("Ranged Kill Settings")]
    [SerializeField] private KeyCode aimKey = KeyCode.Tab;
    [SerializeField] private float aimRange = 10f; // Max distance for aim point calculation
    [SerializeField] private GameObject crosshairPrefab; // Assign UI crosshair prefab in inspector
    private Canvas uiCanvas;
    [SerializeField] private float shootCooldown = 0.5f; // Time between shots
    [SerializeField] private float defaultShootAnimationDuration = 0.5f; // Fallback duration
    [SerializeField] private float mouseSensitivity = 3.0f; // Increased sensitivity (might not be needed for direct follow)

    [Header("References")]
    [SerializeField] private Animator playerAnimator; // Assign your player's animator
    private PlayerController playerController; // Reference to PlayerController

    private NPCController currentTargetNPC;
    private Collider[] nearbyNPCs = new Collider[5]; // Pre-allocate for minor optimization
    private bool isPerformingKill = false; // To prevent kill spam

    // Public flags for PlayerController to check
    public static bool IsKillAnimationPlaying { get; private set; } = false;
    public static bool IsAiming { get; private set; } = false;
    public static bool IsShootingAnimationPlaying { get; private set; } = false;

    // Ranged kill variables
    private GameObject crosshairInstance;
    private Animator crosshairAnimator;
    private bool isInAimMode = false;
    private float lastShotTime = 0f;
    private bool isShooting = false;
    
    // The world position the crosshair represents, for raycasting
    private Vector3 currentAimWorldPosition;
    private float currentAimAngle = 0f; // Angle on XZ plane

    void Start()
    {
        Debug.Log("PlayerKill Start called.");

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
        
        // Get PlayerController reference
        playerController = GetComponent<PlayerController>();
        if (playerController == null)
        {
            Debug.LogError("PlayerKill could not find PlayerController component!");
        }

        // Attempt to find Canvas in Start (will also try in Update if needed)
        FindUICanvas();
        
        // Automatically set rangedTargetLayerMask to include NPC and Default layers if not set
        if (rangedTargetLayerMask.value == 0)
        {
             rangedTargetLayerMask = LayerMask.GetMask("NPC", "Default");
             Debug.Log($"PlayerKill: Automatically set Ranged Target Layer Mask to: {LayerMaskToString(rangedTargetLayerMask)}");
        }

        Debug.Log($"Melee Target Layer Mask: {LayerMaskToString(meleeTargetLayerMask)}"); // Log renamed mask
        Debug.Log($"Ranged Target Layer Mask: {LayerMaskToString(rangedTargetLayerMask)}"); // Log new mask
        Debug.Log($"Crosshair Prefab assigned: {crosshairPrefab != null}");
        Debug.Log($"UI Canvas found in Start: {uiCanvas != null}");
    }

    void Update()
    {
        if (!IsOwner) return; // Only the owner player can initiate kills

        // Try finding canvas in Update if Start failed
        if (uiCanvas == null)
        {
            FindUICanvas();
        }

        // Handle aim mode toggle
        // Allow toggling as long as a kill animation is not performing
        if (Input.GetKeyDown(aimKey) && !isPerformingKill)
        {
            ToggleAimMode();
        }

        // If kill animation is playing OR if aiming, handle differently
        if (IsOwner)
        {
            if (PlayerKill.IsKillAnimationPlaying)
            {
                return; // Skip normal movement and input processing
            }
            
            // Check if player is aiming
            if (PlayerKill.IsAiming)
            {
                // Root player in place handled in PlayerController
                // The rooting logic is in PlayerController's Update method already
                
                // Handle aiming, shooting, and exiting aim mode is handled by PlayerKill
                if (!isShooting)
                {
                    UpdateCrosshairPosition();
                }
                
                // Handle shooting
                if (Input.GetMouseButtonDown(0) && Time.time - lastShotTime >= shootCooldown)
                {
                    Shoot();
                }
                
                // Cancel aim mode with right click
                if (Input.GetMouseButtonDown(1))
                {
                    ExitAimMode();
                }
                
                // DON'T return here, allow camera facing logic and networking to run
            }
        }

        // Normal melee kill handling
        // Only do melee logic if not in aim mode and not performing any kill animation
        if (!isInAimMode && !isPerformingKill) 
        {
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
            // Only enter aim mode if cooldown has passed
            if (Time.time - lastShotTime >= shootCooldown)
            {
                EnterAimMode();
            } else {
                 Debug.Log($"PlayerKill: Cannot enter aim mode, shoot cooldown still active. Time remaining: {shootCooldown - (Time.time - lastShotTime):F2}s");
            }
        }
    }

    private void EnterAimMode()
    {
        Debug.Log("PlayerKill EnterAimMode called.");

        isInAimMode = true;
        IsAiming = true; Debug.Log($"PlayerKill: Setting IsAiming = {IsAiming}");
        
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
                 Debug.Log($"Crosshair instantiated successfully: {crosshairInstance != null}. Parent: {(crosshairInstance.transform.parent != null ? crosshairInstance.transform.parent.name : "None")}");
                 
                 // Get the Animator component from the instantiated crosshair
                 crosshairAnimator = crosshairInstance.GetComponent<Animator>();
                 if (crosshairAnimator == null)
                 {
                     Debug.LogWarning("PlayerKill: Crosshair prefab does not have an Animator component. Cannot play animations.");
                 }
            }
            else
            {
                Debug.LogWarning("PlayerKill: Cannot instantiate UI crosshair without a UI Canvas reference (uiCanvas is null before instantiate).");
            }
        }
        else
        {
            Debug.LogWarning("PlayerKill: No Crosshair Prefab assigned for ranged kill. Cannot show crosshair.");
        }
        
        // Set aiming animation
        if (playerAnimator != null)
        {
            playerAnimator.SetBool("isAiming", true);
        }
        
        // Show cinematic bars
        if (CinematicEffectsController.Instance != null) // Use Singleton instance
        {
            CinematicEffectsController.Instance.ShowCinematicBars(); // Use Singleton instance
        }
        
        Debug.Log("Entered aim mode");
    }

    private void ExitAimMode()
    {
        isInAimMode = false;
        IsAiming = false; Debug.Log($"PlayerKill: Setting IsAiming = {IsAiming}");
        
        // Destroy crosshair
        if (crosshairInstance != null)
        {
            Destroy(crosshairInstance);
        }
        // Clear crosshair animator reference
        crosshairAnimator = null;
        
        // Ensure isShooting flag is reset when exiting aim mode
        isShooting = false;
        IsShootingAnimationPlaying = false; // Ensure this is also reset
        Debug.Log("PlayerKill: ExitAimMode called. Setting isShooting = false and IsShootingAnimationPlaying = false.");
        
        // Stop aiming animation
        if (playerAnimator != null)
        {
            playerAnimator.SetBool("isAiming", false);
        }
        
        // Hide cinematic bars
        if (CinematicEffectsController.Instance != null) // Use Singleton instance
        {
            CinematicEffectsController.Instance.HideCinematicBars(); // Use Singleton instance
        }
        
        Debug.Log("Exited aim mode");
    }

    private void UpdateCrosshairPosition()
    {
        if (crosshairInstance == null || Camera.main == null || uiCanvas == null) return;
        
        Camera mainCamera = Camera.main;
        RectTransform canvasRect = uiCanvas.GetComponent<RectTransform>();
        RectTransform crosshairRect = crosshairInstance.GetComponent<RectTransform>();
        
        Debug.Log($"Crosshair Rect - Pivot: {crosshairRect.pivot}, Anchors: {crosshairRect.anchorMin} to {crosshairRect.anchorMax}");

        // Get player position in screen space (use base position for screen point origin)
        Vector3 screenPointPlayerBase = mainCamera.WorldToScreenPoint(transform.position); 
        // Vector3 screenPointPlayer = mainCamera.WorldToScreenPoint(transform.position + Vector3.up * 1f); // Slightly above player - might not be needed for origin
        Debug.Log($"Screen Point Player Base: {screenPointPlayerBase}");

        // Get mouse position in screen space
        Vector3 screenPointMouse = Input.mousePosition;
        Debug.Log($"Screen Point Mouse (Raw): {screenPointMouse}");

        // Calculate vector from player screen base point to mouse screen point
        Vector3 screenVectorPlayerToMouse = screenPointMouse - screenPointPlayerBase;
        Debug.Log($"Screen Vector Player to Mouse: {screenVectorPlayerToMouse}");

        // Calculate the screen space radius corresponding to aimRange
        // Project a point 'aimRange' units to the right of the player in world space at the player's base height
        Vector3 worldPointAtAimRangeRightBase = transform.position + transform.right * aimRange;
        Vector3 screenPointAtAimRangeRightBase = mainCamera.WorldToScreenPoint(worldPointAtAimRangeRightBase);
        float screenAimRadius = Vector3.Distance(screenPointPlayerBase, screenPointAtAimRangeRightBase);
        Debug.Log($"Screen Aim Radius (calculated from {aimRange} world units): {screenAimRadius}");

        // Clamp the screen vector to the calculated radius
        Vector3 clampedScreenVector = Vector3.ClampMagnitude(screenVectorPlayerToMouse, screenAimRadius);
        Debug.Log($"Clamped Screen Vector: {clampedScreenVector}");
        
        // Calculate the desired screen position for the crosshair by adding the clamped vector to the player's screen base position
        Vector3 screenPositionCrosshair = screenPointPlayerBase + clampedScreenVector;
        Debug.Log($"Screen Position Crosshair (calculated): {screenPositionCrosshair}");

        // Position the UI crosshair using RectTransformUtility
        Vector2 canvasPosition;
        // Use null camera for Screen Space - Overlay, or mainCamera for Screen Space - Camera
        // Assuming Screen Space - Overlay for now based on previous logs
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPositionCrosshair, null, out canvasPosition);
        Debug.Log($"Canvas Position (calculated): {canvasPosition}");
        crosshairInstance.GetComponent<RectTransform>().anchoredPosition = canvasPosition;

        // Calculate the corresponding world position for raycasting
        // Cast a ray from the camera through the clamped screen position onto a plane at player height
        Ray rayThroughCrosshair = mainCamera.ScreenPointToRay(screenPositionCrosshair);
        Plane playerPlane = new Plane(Vector3.up, transform.position.y + 1f);
        float hitdist;
        if (playerPlane.Raycast(rayThroughCrosshair, out hitdist))
        {
            currentAimWorldPosition = rayThroughCrosshair.GetPoint(hitdist);
        } else {
            // Fallback: If raycast fails (e.g., plane is behind camera), use a point in front of player
             currentAimWorldPosition = transform.position + transform.forward * aimRange + Vector3.up * 1f;
        }

        // Handle player sprite flipping based on aim direction (relative to camera)
        if (playerController != null && Camera.main != null)
        {
            // Determine if aim point is left or right of the player in screen space
            // Use the clamped screen position for flipping logic
            bool shouldFaceLeft = (screenPositionCrosshair.x < screenPointPlayerBase.x);
            playerController.SetFacingLeft(shouldFaceLeft); // Call method in PlayerController
        }
    }

    private void Shoot()
    {
        lastShotTime = Time.time;
        isShooting = true; // Set flag when shooting starts
        IsShootingAnimationPlaying = true; // Set flag for PlayerController
        Debug.Log("PlayerKill: Shoot initiated. Setting isShooting = true and IsShootingAnimationPlaying = true.");
        
        // Trigger shooting animation
        if (playerAnimator != null)
        {
            playerAnimator.SetTrigger("Shoot");
            // Start coroutine to reset state after animation
            StartCoroutine(ResetAimStateAfterShoot());
        }
        
        // Trigger crosshair animation if animator exists
        if (crosshairAnimator != null)
        {
            // Assuming you have a trigger parameter named "Break" in your crosshair animator
            crosshairAnimator.SetTrigger("Break");
        }
        
        // Check what is under the mouse cursor by raycasting from the camera
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // Use a large distance for the camera raycast to ensure it passes through objects
        float cameraRaycastDistance = 1000f; // Use a large number or Mathf.Infinity if appropriate

        Debug.DrawRay(ray.origin, ray.direction * cameraRaycastDistance, Color.blue, 2f); // Draw ray from camera
        Debug.Log($"Shooting ray from camera through mouse position. Ray: {ray.origin} -> {ray.direction}. Distance: {cameraRaycastDistance}. Targeting Layers: {LayerMaskToString(rangedTargetLayerMask)}");

        // Raycast and check for objects on the rangedTargetLayerMask with a large distance
        if (Physics.Raycast(ray, out hit, cameraRaycastDistance, rangedTargetLayerMask))
        {
            Debug.Log($"Camera Raycast hit: {hit.collider.name} at {hit.point}. Hit object tag: {hit.collider.gameObject.tag}. Hit object layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
            
            // Now check if the hit object is within the player's defined aimRange
            float distanceToHit = Vector3.Distance(transform.position, hit.point);
            if (distanceToHit <= aimRange)
            {
                Debug.Log($"Hit object {hit.collider.name} is within aim range ({distanceToHit.ToString("F2")} <= {aimRange.ToString("F2")}).");

                // Check if the hit object has the "NPC" tag
                if (hit.collider.CompareTag("NPC"))
                {
                    NPCController npc = hit.collider.GetComponent<NPCController>();
                    if (npc != null)
                    {
                        Debug.Log($"Shot hit NPC: {npc.name}. Initiating ranged kill on NPC.");
                        
                        // Initiate kill on server via NPCController's method
                        InitiateRangedKillServerRpc(npc.NetworkObject);
                    }
                }
                // Check if the hit object has the "Player" tag
                else if (hit.collider.CompareTag("Player"))
                {
                    // Find the PlayerController of the hit object
                    PlayerController hitPlayerController = hit.collider.GetComponent<PlayerController>();
                    if (hitPlayerController != null)
                    {
                        Debug.Log($"Shot hit Player: {hitPlayerController.name}. Initiating ranged kill on Player.");
                        // Initiate kill on server via PlayerController's method
                        InitiateRangedPlayerKillServerRpc(hitPlayerController.NetworkObject);
                    }
                }
            }
            else
            {
                 Debug.Log($"Hit object {hit.collider.name} is OUTSIDE aim range ({distanceToHit.ToString("F2")} > {aimRange.ToString("F2")}). No kill initiated.");
            }
        }
        else
        {
            Debug.Log($"Shot missed: Camera Raycast did not hit anything on the {LayerMaskToString(rangedTargetLayerMask)} layers within {cameraRaycastDistance} distance.");
        }
        
        // Aim mode exit is handled by ResetAimStateAfterShoot coroutine
    }

    private IEnumerator ResetAimStateAfterShoot()
    {
        // Attempt to get player shoot animation duration
        float playerShootAnimationDuration = defaultShootAnimationDuration;
        if (playerAnimator != null)
        {
            RuntimeAnimatorController ac = playerAnimator.runtimeAnimatorController;
            if (ac != null)
            {
                foreach (AnimationClip clip in ac.animationClips) {
                    if (clip.name.Contains("Shoot")) { 
                        playerShootAnimationDuration = clip.length;
                        Debug.Log($"PlayerKill: Found Shoot animation '{clip.name}' with duration: {playerShootAnimationDuration}");
                        break;
                    }
                }
                if (playerShootAnimationDuration == 0f) {
                    Debug.LogWarning("PlayerKill: Could not find Shoot animation clip length, defaulting to 0.5s.");
                    playerShootAnimationDuration = 0.5f; // Fallback
                }
            }
        }
        
        Debug.Log($"Player shoot animation in progress, will complete in {playerShootAnimationDuration} seconds");
        yield return new WaitForSeconds(playerShootAnimationDuration); // Wait for player anim first
        Debug.Log("Player shoot animation complete, resetting state");

        // Reset player shooting animation flag after player animation finishes
        IsShootingAnimationPlaying = false; // Reset flag here
        Debug.Log("PlayerKill: Player shoot animation finished. Setting IsShootingAnimationPlaying = false.");
        
        // Add these lines to ensure animation state is reset after shooting
        isPerformingKill = false;
        IsKillAnimationPlaying = false;

        // Now wait for the crosshair "Broken" animation to finish, if an animator exists
        if (crosshairAnimator != null)
        {
            // Find the duration of the "Broken" state/clip
            float crosshairBreakAnimationDuration = 0f;
            RuntimeAnimatorController ac = crosshairAnimator.runtimeAnimatorController;
            if (ac != null)
            {
                foreach (AnimationClip clip in ac.animationClips) {
                    // Assuming the clip name contains "Broken"
                    if (clip.name.Contains("Broken")) { 
                        crosshairBreakAnimationDuration = clip.length;
                        Debug.Log($"PlayerKill: Found Crosshair Break animation '{clip.name}' with duration: {crosshairBreakAnimationDuration}");
                        break;
                    }
                }
                 if (crosshairBreakAnimationDuration == 0f) {
                    Debug.LogWarning("PlayerKill: Could not find Crosshair Broken animation clip length, defaulting to 0s. Crosshair will disappear immediately after player anim.");
                }
            }

            Debug.Log($"Waiting for crosshair break animation to complete in {crosshairBreakAnimationDuration} seconds");
            yield return new WaitForSeconds(crosshairBreakAnimationDuration);
            Debug.Log("Crosshair break animation complete.");
        }

        // Exit aim mode after the shoot animation and crosshair animation
        ExitAimMode(); // This will also reset IsAiming = false, destroy crosshair, and reset isShooting and IsShootingAnimationPlaying
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

    [ServerRpc(RequireOwnership = true)]
    private void InitiateRangedPlayerKillServerRpc(NetworkObjectReference targetPlayerRef)
    {
        if (targetPlayerRef.TryGet(out NetworkObject targetPlayerNetworkObject))
        {
            if (targetPlayerNetworkObject.TryGetComponent<PlayerController>(out PlayerController playerToKill))
            {
                // Optional: Add a distance check here on the server for security
                float distanceToTarget = Vector3.Distance(transform.position, playerToKill.transform.position);
                if (distanceToTarget > aimRange + 1f) // Add a small buffer for leniency
                {
                   Debug.LogWarning($"Player {OwnerClientId} tried to kill Player from too far with ranged attack. Dist: {distanceToTarget}");
                   return;
                }

                // Tell all clients to play shooting animation (if needed, or handle in PlayerController death)
                PlayShootAnimationClientRpc(); // This plays on the killer, consider if hit player needs anim

                // Call the KillTarget method on the killer's PlayerController (on the server)
                // The PlayerController's KillTarget method should handle notifying the GameManager and the target player
                if (playerController != null)
                {
                    playerController.KillTarget(playerToKill);
                }
                else
                {
                    Debug.LogError("PlayerKill: Cannot initiate player kill because PlayerController reference is null on server.");
                }
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

        // Use meleeTargetLayerMask for melee detection
        int numFound = Physics.OverlapSphereNonAlloc(transform.position, killRange, nearbyNPCs, meleeTargetLayerMask);
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

    private void FindUICanvas()
    {
         if (uiCanvas == null)
         {
            uiCanvas = FindObjectOfType<Canvas>();
            if (uiCanvas == null)
            {
                Debug.LogWarning("PlayerKill: Still could not find a UI Canvas.");
            } else {
                Debug.Log($"PlayerKill: Successfully found UI Canvas in FindUICanvas: {uiCanvas.name}. RenderMode: {uiCanvas.renderMode}, ScaleFactor: {uiCanvas.scaleFactor}");
            }
         }
    }
} 