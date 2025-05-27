using UnityEngine;
using System.Collections;

public class CinematicEffectsController : MonoBehaviour
{
    public static CinematicEffectsController Instance { get; private set; }

    [Header("References")]
    [Tooltip("The GameObject containing the cinematic frame UI and its Animator.")]
    [SerializeField] private GameObject cinematicFrameObject;

    [Header("Animator Settings")]
    [Tooltip("The name of the trigger parameter in the Animator to play the 'Out' animation.")]
    [SerializeField] private string outTriggerName = "PlayOut";
    [Tooltip("The exact name of the 'Out' animation state in the Animator (e.g., 'CinematicFrameOut').")]
    [SerializeField] private string outAnimationStateName = "CinematicFrameOut"; // Make sure this matches your state name

    private Animator cinematicFrameAnimator;
    private Coroutine activeCoroutine = null;
    private CameraFollow mainCameraFollow;

    void Awake() // Changed from Start to Awake for Singleton initialization
    {
        // Singleton pattern implementation
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("CinematicEffectsController: More than one instance found! Destroying duplicate.", this.gameObject);
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
        // Optional: DontDestroyOnLoad(gameObject); // if you need it to persist across scene loads

        if (cinematicFrameObject == null)
        {
            Debug.LogError("CinematicEffectsController: CinematicFrameObject not assigned in the Inspector! Disabling component.", this);
            enabled = false;
            return;
        }

        cinematicFrameAnimator = cinematicFrameObject.GetComponent<Animator>();
        if (cinematicFrameAnimator == null)
        {
            Debug.LogError($"CinematicEffectsController: No Animator component found on '{cinematicFrameObject.name}'. Disabling component.", this);
            enabled = false;
            return;
        }

        // Ensure the cinematic frame is disabled at the start
        cinematicFrameObject.SetActive(false);
        Debug.Log("CinematicEffectsController initialized. Frame is initially inactive.");

        // Get CameraFollow component from the main camera
        if (Camera.main != null)
        {
            mainCameraFollow = Camera.main.GetComponent<CameraFollow>();
            if (mainCameraFollow == null)
            {
                Debug.LogWarning("CinematicEffectsController: No CameraFollow script found on the Main Camera. Cinematic zoom will not work.", this);
            }
        }
        else
        {
            Debug.LogWarning("CinematicEffectsController: No Main Camera found. Cinematic zoom will not work.", this);
        }
    }

    /// <summary>
    /// Activates the cinematic frame and plays the 'In' animation.
    /// This version does NOT handle camera zoom. Use ShowCinematicBarsAndZoom for that.
    /// </summary>
    public void ShowCinematicBars()
    {
        if (cinematicFrameObject == null || cinematicFrameAnimator == null)
        {
            Debug.LogWarning("CinematicEffectsController: Cannot show bars, essential references are missing.", this);
            return;
        }
        
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }

        Debug.Log("CinematicEffectsController: Showing cinematic bars (no zoom).");
        cinematicFrameObject.SetActive(true);
        // The Animator's default state or an 'Entry' transition should handle playing 'CinematicFrameIn'
    }

    /// <summary>
    /// Activates the cinematic frame, plays the 'In' animation, and initiates a camera zoom.
    /// </summary>
    /// <param name="targetZoomSize">The orthographic size the camera should zoom to.</param>
    /// <param name="customZoomSpeed">The speed at which the camera should zoom for this specific effect.</param>
    public void ShowCinematicBarsAndZoom(float targetZoomSize, float customZoomSpeed)
    {
        if (cinematicFrameObject == null || cinematicFrameAnimator == null)
        {
            Debug.LogWarning("CinematicEffectsController: Cannot show bars and zoom, essential references are missing.", this);
            return;
        }

        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
            // If an active coroutine was stopped (likely from a previous HideCinematicBars), 
            // ensure the zoom is reset before starting a new one.
            if (mainCameraFollow != null) mainCameraFollow.ResetCinematicZoom();
        }

        Debug.Log($"CinematicEffectsController: Showing cinematic bars and zooming to {targetZoomSize} with speed {customZoomSpeed}.");
        cinematicFrameObject.SetActive(true);

        // Start camera zoom
        if (mainCameraFollow != null)
        {
            mainCameraFollow.StartCinematicZoom(targetZoomSize, customZoomSpeed);
        }
        // The Animator's default state or an 'Entry' transition should handle playing 'CinematicFrameIn'
    }

    /// <summary>
    /// Triggers the 'Out' animation for the cinematic frame and disables it after completion.
    /// </summary>
    public void HideCinematicBars()
    {
        if (cinematicFrameObject == null || cinematicFrameAnimator == null)
        {
            Debug.LogWarning("CinematicEffectsController: Cannot hide bars, essential references are missing.", this);
            return;
        }

        if (!cinematicFrameObject.activeSelf)
        {
            Debug.Log("CinematicEffectsController: HideCinematicBars called, but frame is already inactive.", this);
            return; // Already inactive or in the process of hiding
        }
        
        // Start camera zoom out *before* triggering the animation
        if (mainCameraFollow != null)
        {
            mainCameraFollow.ResetCinematicZoom();
        }

        if (activeCoroutine != null)
        {
            // If already in the process of hiding, don't restart
            Debug.Log("CinematicEffectsController: HideCinematicBars called, but already processing an animation. Will not restart.", this);
            return;
        }

        Debug.Log($"CinematicEffectsController: Attempting to trigger '{outTriggerName}' to hide cinematic bars.");
        
        bool triggerExists = false;
        foreach(var param in cinematicFrameAnimator.parameters)
        {
            if(param.name == outTriggerName && param.type == AnimatorControllerParameterType.Trigger)
            {
                triggerExists = true;
                break;
            }
        }

        if(triggerExists)
        {
            cinematicFrameAnimator.SetTrigger(outTriggerName);
            activeCoroutine = StartCoroutine(WaitForAnimationAndDisable(outAnimationStateName));
        }
        else
        {
            Debug.LogWarning($"CinematicEffectsController: Animator on '{cinematicFrameObject.name}' does not have a trigger parameter named '{outTriggerName}'. Disabling object directly.", this);
            cinematicFrameObject.SetActive(false);
        }
    }

    private IEnumerator WaitForAnimationAndDisable(string animationStateToWaitFor)
    {
        Debug.Log($"CinematicEffectsController: Coroutine started. Waiting for animator to enter state '{animationStateToWaitFor}'.", this);
        
        // Wait a frame to ensure animator processes the trigger
        yield return null; 

        // Wait until the animator is in the target 'Out' state and not transitioning
        // This can take a few frames if there are conditions or delays on the transition
        int safetyCounter = 0; // To prevent infinite loops if state is never reached
        while (!cinematicFrameAnimator.GetCurrentAnimatorStateInfo(0).IsName(animationStateToWaitFor) && safetyCounter < 120) // Approx 2 seconds at 60fps
        {
            if (cinematicFrameAnimator.IsInTransition(0))
            {
                 // If transitioning, check if the next state is the one we want
                if (cinematicFrameAnimator.GetNextAnimatorStateInfo(0).IsName(animationStateToWaitFor))
                {
                    // Wait for transition to complete
                    while(cinematicFrameAnimator.IsInTransition(0)) yield return null;
                    break; 
                }
            }
            safetyCounter++;
            yield return null;
        }

        if (!cinematicFrameAnimator.GetCurrentAnimatorStateInfo(0).IsName(animationStateToWaitFor))
        {
            Debug.LogWarning($"CinematicEffectsController: Animator did not enter state '{animationStateToWaitFor}' after trigger. Disabling frame directly.", this);
            cinematicFrameObject.SetActive(false);
            activeCoroutine = null;
            yield break;
        }
        
        Debug.Log($"CinematicEffectsController: Animator is in state '{animationStateToWaitFor}'. Waiting for it to complete.", this);

        // Wait for the duration of the current state (which should be the 'Out' animation)
        // This ensures we wait for the actual length of the playing animation clip in that state
        // It's important that the 'Out' animation state does not loop.
        float currentClipLength = cinematicFrameAnimator.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(currentClipLength);

        Debug.Log($"CinematicEffectsController: Animation '{animationStateToWaitFor}' (length: {currentClipLength}s) assumed complete. Disabling cinematic frame.", this);
        cinematicFrameObject.SetActive(false);
        activeCoroutine = null;
    }
} 