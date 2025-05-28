using UnityEngine;
using Unity.Netcode;
using System.Collections;
using UnityEngine.UI; // Required for UI elements

/*
 * PlayerKill - Handles player kill mechanics for both melee and ranged attacks
 * 
 * IMPORTANT: Animation Timing
 * This script uses AnimatorStateInfo monitoring instead of fixed durations to properly
 * track when animations finish. This accounts for:
 * - Animator transition times between states
 * - Animation startup delays
 * - Actual animation completion
 * 
 * If you experience delays between pressing kill buttons and animations starting:
 * 1. Check your Animator Controller transition settings
 * 2. Reduce "Exit Time" on transitions (try 0 or very low values)
 * 3. Disable "Has Exit Time" for immediate transitions
 * 4. Reduce "Transition Duration" for snappier transitions
 * 5. Check "Transition Offset" is 0
 */
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

    [Header("Cinematic Effect Settings")] // New Header
    [Tooltip("Target orthographic camera size for ranged kill cinematic.")]
    [SerializeField] private float rangedKillZoomTargetSize = 3f;
    [Tooltip("Zoom speed for ranged kill cinematic.")]
    [SerializeField] private float rangedKillZoomSpeed = 2f;
    [Tooltip("Target orthographic camera size for melee kill cinematic.")]
    [SerializeField] private float meleeKillZoomTargetSize = 7f;
    [Tooltip("Zoom speed for melee kill cinematic.")]
    [SerializeField] private float meleeKillZoomSpeed = 2f;

    [Header("Camera Shake Settings (Local Player)")] // New Header
    [Tooltip("Duration of camera shake when shooting.")]
    [SerializeField] private float shootShakeDuration = 0.2f;
    [Tooltip("Magnitude of camera shake when shooting.")]
    [SerializeField] private float shootShakeMagnitude = 0.1f;
    [Tooltip("Duration of camera shake during melee attack.")]
    [SerializeField] private float meleeImpactShakeDuration = 0.25f;
    [Tooltip("Magnitude of camera shake during melee attack.")]
    [SerializeField] private float meleeImpactShakeMagnitude = 0.15f;

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
        
        // Show cinematic bars and zoom in for ranged
        if (CinematicEffectsController.Instance != null)
        {
            if (Camera.main != null)
            {
                CameraFollow cameraFollow = Camera.main.GetComponent<CameraFollow>();
                if (cameraFollow != null)
                {
                    CinematicEffectsController.Instance.ShowCinematicBarsAndZoom(rangedKillZoomTargetSize, rangedKillZoomSpeed);
                }
                else
                {
                    CinematicEffectsController.Instance.ShowCinematicBars(); // Fallback if no CameraFollow
                    Debug.LogWarning("PlayerKill: CameraFollow not found on Main Camera when entering aim mode. Showing bars without zoom.");
                }
            }
            else
            {
                CinematicEffectsController.Instance.ShowCinematicBars(); // Fallback if no Main Camera
                 Debug.LogWarning("PlayerKill: Main Camera not found when entering aim mode. Showing bars without zoom.");
            }
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
        
        // Log animator state before triggering
        LogAnimatorTransitionInfo("Before Shoot Trigger");
        
        // Trigger shooting animation
        if (playerAnimator != null)
        {
            playerAnimator.SetTrigger("Shoot");
            // Start coroutine to monitor animation state instead of using fixed duration
            StartCoroutine(MonitorShootAnimationState());
            
            // Log state after trigger
            StartCoroutine(LogTransitionAfterDelay("After Shoot Trigger", 0.1f));
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
        
        // Aim mode exit is handled by MonitorShootAnimationState
    }

    // New coroutine that monitors the actual animation state
    private IEnumerator MonitorShootAnimationState()
    {
        if (playerAnimator == null) yield break;
        
        // Wait for the animator to process the trigger and start transitioning
        yield return null; // Wait one frame
        
        // Wait for the shoot animation to actually start playing
        float timeout = 2f; // Safety timeout
        float elapsed = 0f;
        bool shootAnimationStarted = false;
        
        while (!shootAnimationStarted && elapsed < timeout)
        {
            AnimatorStateInfo stateInfo = playerAnimator.GetCurrentAnimatorStateInfo(0);
            
            // Check if we're in a shoot animation state (adjust the state name to match your animator)
            if (stateInfo.IsName("Shoot") || stateInfo.IsName("TheManInTheCoatShoot") || 
                playerAnimator.GetCurrentAnimatorClipInfo(0).Length > 0 && 
                playerAnimator.GetCurrentAnimatorClipInfo(0)[0].clip != null &&
                playerAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name.Contains("Shoot"))
            {
                shootAnimationStarted = true;
                Debug.Log($"PlayerKill: Shoot animation started. State: {GetAnimatorStateName(playerAnimator)}");
            }
            else if (playerAnimator.IsInTransition(0))
            {
                // We're transitioning, which is expected
                Debug.Log("PlayerKill: Animator is transitioning to shoot state...");
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (!shootAnimationStarted)
        {
            Debug.LogWarning("PlayerKill: Shoot animation did not start within timeout!");
        }
        
        // Now wait for the animation to actually finish
        bool animationFinished = false;
        elapsed = 0f;
        timeout = 5f; // Longer timeout for animation completion
        
        while (!animationFinished && elapsed < timeout)
        {
            AnimatorStateInfo stateInfo = playerAnimator.GetCurrentAnimatorStateInfo(0);
            
            // Check if we're back to idle or another non-shoot state
            if (!playerAnimator.IsInTransition(0) && 
                !stateInfo.IsName("Shoot") && 
                !stateInfo.IsName("TheManInTheCoatShoot") &&
                (stateInfo.IsName("Idle") || stateInfo.IsName("MainInCoatIdle") || stateInfo.IsName("Aiming")))
            {
                animationFinished = true;
                Debug.Log($"PlayerKill: Shoot animation finished. Now in state: {GetAnimatorStateName(playerAnimator)}");
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Reset shooting state
        IsShootingAnimationPlaying = false;
        isPerformingKill = false;
        IsKillAnimationPlaying = false;
        Debug.Log("PlayerKill: Shoot animation monitoring complete. Movement re-enabled.");
        
        // Handle crosshair animation if needed
        if (crosshairAnimator != null)
        {
            // Wait a bit for crosshair animation
            yield return new WaitForSeconds(0.5f);
        }
        
        // Exit aim mode
        ExitAimMode();
    }

    private IEnumerator ResetAimStateAfterShoot()
    {
        // This coroutine is now replaced by MonitorShootAnimationState
        // Keeping it for backwards compatibility but it should not be called
        Debug.LogWarning("PlayerKill: ResetAimStateAfterShoot called but should use MonitorShootAnimationState instead");
        yield return null;
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
            
            // Show cinematic bars and zoom out for melee
            if (CinematicEffectsController.Instance != null)
            {
                if (Camera.main != null)
                {
                    CameraFollow cameraFollow = Camera.main.GetComponent<CameraFollow>();
                    if (cameraFollow != null)
                    {
                        CinematicEffectsController.Instance.ShowCinematicBarsAndZoom(meleeKillZoomTargetSize, meleeKillZoomSpeed);
                    }
                    else
                    {
                        CinematicEffectsController.Instance.ShowCinematicBars(); // Fallback if no CameraFollow
                        Debug.LogWarning("PlayerKill: CameraFollow not found on Main Camera for melee. Showing bars without zoom.");
                    }
                }
                else
                {
                    CinematicEffectsController.Instance.ShowCinematicBars(); // Fallback if no Main Camera
                    Debug.LogWarning("PlayerKill: Main Camera not found for melee. Showing bars without zoom.");
                }
            }
            
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
                
                // Log animator state before triggering
                LogAnimatorTransitionInfo("Before Kill Trigger");
                
                playerAnimator.SetTrigger("Kill");
                StartCoroutine(CheckAnimationStateAfterTrigger(playerAnimator, "TheManInTheCoatKill", "Kill"));
                
                // Log state after trigger
                StartCoroutine(LogTransitionAfterDelay("After Kill Trigger", 0.1f));

                isPerformingKill = true;
                IsKillAnimationPlaying = true; // Set flag here
                
                // Use the new monitoring coroutine instead of fixed duration
                StartCoroutine(MonitorMeleeKillAnimationState());
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

    // New coroutine that monitors the actual melee kill animation state
    private IEnumerator MonitorMeleeKillAnimationState()
    {
        if (playerAnimator == null) yield break;
        
        // Wait for the animator to process the trigger and start transitioning
        yield return null; // Wait one frame
        
        // Wait for the kill animation to actually start playing
        float timeout = 2f; // Safety timeout
        float elapsed = 0f;
        bool killAnimationStarted = false;
        
        while (!killAnimationStarted && elapsed < timeout)
        {
            AnimatorStateInfo stateInfo = playerAnimator.GetCurrentAnimatorStateInfo(0);
            
            // Check if we're in a kill animation state
            if (stateInfo.IsName("Kill") || stateInfo.IsName("TheManInTheCoatKill") || 
                playerAnimator.GetCurrentAnimatorClipInfo(0).Length > 0 && 
                playerAnimator.GetCurrentAnimatorClipInfo(0)[0].clip != null &&
                playerAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name.Contains("Kill"))
            {
                killAnimationStarted = true;
                Debug.Log($"PlayerKill: Melee kill animation started. State: {GetAnimatorStateName(playerAnimator)}");
            }
            else if (playerAnimator.IsInTransition(0))
            {
                // We're transitioning, which is expected
                Debug.Log("PlayerKill: Animator is transitioning to kill state...");
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (!killAnimationStarted)
        {
            Debug.LogWarning("PlayerKill: Kill animation did not start within timeout!");
        }
        
        // Now wait for the animation to actually finish
        bool animationFinished = false;
        elapsed = 0f;
        timeout = 5f; // Longer timeout for animation completion
        
        while (!animationFinished && elapsed < timeout)
        {
            AnimatorStateInfo stateInfo = playerAnimator.GetCurrentAnimatorStateInfo(0);
            
            // Check if we're back to idle or running state
            if (!playerAnimator.IsInTransition(0) && 
                !stateInfo.IsName("Kill") && 
                !stateInfo.IsName("TheManInTheCoatKill") &&
                (stateInfo.IsName("Idle") || stateInfo.IsName("MainInCoatIdle") || 
                 stateInfo.IsName("Run") || stateInfo.IsName("ManInCoatRun")))
            {
                animationFinished = true;
                Debug.Log($"PlayerKill: Melee kill animation finished. Now in state: {GetAnimatorStateName(playerAnimator)}");
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Reset kill state
        Debug.Log("PlayerKill: Melee kill animation complete, resetting state");
        isPerformingKill = false;
        IsKillAnimationPlaying = false;
        
        // Hide cinematic bars and reset zoom after melee kill animation
        if (CinematicEffectsController.Instance != null)
        {
            CinematicEffectsController.Instance.HideCinematicBars();
        }
    }

    private System.Collections.IEnumerator ResetKillState(float delay)
    {
        // This is now only used as a fallback when animator is null
        Debug.Log($"Player kill animation fallback timer, will complete in {delay} seconds");
        yield return new WaitForSeconds(delay);
        Debug.Log("Player kill animation fallback complete, resetting state");
        isPerformingKill = false;
        IsKillAnimationPlaying = false; // Reset flag here

        // Hide cinematic bars and reset zoom after melee kill animation
        if (CinematicEffectsController.Instance != null)
        {
            CinematicEffectsController.Instance.HideCinematicBars();
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

    // --- Public methods for Animation Events to trigger camera shake ---
    public void AnimationEvent_TriggerShootShake()
    {
        if (IsOwner && CameraShakeController.Instance != null)
        {
            Debug.Log("PlayerKill: AnimationEvent_TriggerShootShake called by owner.");
            CameraShakeController.Instance.TriggerShake(shootShakeDuration, shootShakeMagnitude);
        }
    }

    public void AnimationEvent_TriggerMeleeImpactShake()
    {
        if (IsOwner && CameraShakeController.Instance != null)
        {
            Debug.Log("PlayerKill: AnimationEvent_TriggerMeleeImpactShake called by owner.");
            CameraShakeController.Instance.TriggerShake(meleeImpactShakeDuration, meleeImpactShakeMagnitude);
        }
    }
    
    // Debug method to log animator transition info
    private void LogAnimatorTransitionInfo(string context)
    {
        if (playerAnimator == null) return;
        
        AnimatorStateInfo currentState = playerAnimator.GetCurrentAnimatorStateInfo(0);
        AnimatorTransitionInfo transitionInfo = playerAnimator.GetAnimatorTransitionInfo(0);
        
        if (playerAnimator.IsInTransition(0))
        {
            Debug.Log($"[{context}] Animator Transition Info:");
            Debug.Log($"  - Duration: {transitionInfo.duration}");
            Debug.Log($"  - Normalized Time: {transitionInfo.normalizedTime}");
            Debug.Log($"  - Current State: {GetAnimatorStateName(playerAnimator)}");
            Debug.Log($"  - Has Fixed Duration: {transitionInfo.hasFixedDuration}");
        }
        else
        {
            Debug.Log($"[{context}] Current State: {GetAnimatorStateName(playerAnimator)} (No active transition)");
        }
    }

    // Helper coroutine to log transition info after a delay
    private IEnumerator LogTransitionAfterDelay(string context, float delay)
    {
        yield return new WaitForSeconds(delay);
        LogAnimatorTransitionInfo(context);
    }
} 