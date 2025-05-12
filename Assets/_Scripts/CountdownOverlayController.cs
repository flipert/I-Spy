using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

/// <summary>
/// Dedicated controller for the countdown overlay to prevent double activation issues
/// </summary>
public class CountdownOverlayController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup overlayCanvasGroup;
    [SerializeField] private TextMeshProUGUI countdownText;
    [SerializeField] private Button cancelButton;
    
    [Header("Settings")]
    [SerializeField] private float fadeInDuration = 0.3f;
    [SerializeField] private float fadeOutDuration = 0.3f;
    
    private Coroutine activeCountdownCoroutine = null;
    private Coroutine activeFadeCoroutine = null;
    private bool isCountdownRunning = false;
    
    // Singleton pattern
    private static CountdownOverlayController _instance;
    public static CountdownOverlayController Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<CountdownOverlayController>();
            }
            return _instance;
        }
    }
    
    private void Awake()
    {
        // Singleton setup
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // Make sure the overlay starts hidden
        if (overlayCanvasGroup == null)
        {
            overlayCanvasGroup = GetComponent<CanvasGroup>();
            if (overlayCanvasGroup == null)
            {
                overlayCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
        
        // Initialize state
        overlayCanvasGroup.alpha = 0f;
        overlayCanvasGroup.interactable = false;
        overlayCanvasGroup.blocksRaycasts = false;
        gameObject.SetActive(false);
    }
    
    /// <summary>
    /// Start the countdown sequence with smooth fade-in
    /// </summary>
    /// <param name="seconds">Number of seconds to count down</param>
    /// <param name="showCancelButton">Whether to show the cancel button</param>
    /// <param name="cancelAction">Action to perform if cancel is clicked</param>
    /// <param name="completeAction">Action to perform when countdown completes</param>
    public void StartCountdown(int seconds, bool showCancelButton, UnityAction cancelAction, UnityAction completeAction)
    {
        // If a countdown is already running, don't start another one
        if (isCountdownRunning)
        {
            Debug.Log("Countdown already running, ignoring additional start request");
            return;
        }
        
        // Stop any active coroutines
        StopAllCoroutines();
        
        // Set up cancel button if provided
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            if (cancelAction != null)
            {
                cancelButton.onClick.AddListener(() => {
                    StopCountdown();
                    cancelAction.Invoke();
                });
            }
            cancelButton.gameObject.SetActive(showCancelButton);
        }
        
        // Start new countdown
        activeCountdownCoroutine = StartCoroutine(CountdownRoutine(seconds, completeAction));
    }
    
    /// <summary>
    /// Stop the current countdown and hide the overlay
    /// </summary>
    public void StopCountdown()
    {
        if (activeCountdownCoroutine != null)
        {
            StopCoroutine(activeCountdownCoroutine);
            activeCountdownCoroutine = null;
        }
        
        // Fade out the overlay
        FadeOut();
    }
    
    /// <summary>
    /// Show an error message on the overlay (in red)
    /// </summary>
    public void ShowError(string errorMessage, UnityAction dismissAction = null)
    {
        // Stop any active countdown
        if (activeCountdownCoroutine != null)
        {
            StopCoroutine(activeCountdownCoroutine);
            activeCountdownCoroutine = null;
        }
        
        // Update the text
        if (countdownText != null)
        {
            countdownText.text = errorMessage;
            countdownText.color = Color.red;
        }
        
        // Set up dismiss button
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() => {
                FadeOut();
                dismissAction?.Invoke();
            });
            cancelButton.gameObject.SetActive(true);
        }
        
        // Make sure overlay is visible
        FadeIn();
    }
    
    /// <summary>
    /// Show a normal status message on the overlay (in white)
    /// </summary>
    public void ShowMessage(string message, UnityAction dismissAction = null)
    {
        // Stop any active countdown
        if (activeCountdownCoroutine != null)
        {
            StopCoroutine(activeCountdownCoroutine);
            activeCountdownCoroutine = null;
        }
        
        // Update the text
        if (countdownText != null)
        {
            countdownText.text = message;
            countdownText.color = Color.white;
        }
        
        // Set up dismiss button
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            if (dismissAction != null)
            {
                cancelButton.onClick.AddListener(() => {
                    FadeOut();
                    dismissAction.Invoke();
                });
                cancelButton.gameObject.SetActive(true);
            }
            else
            {
                cancelButton.gameObject.SetActive(false);
            }
        }
        
        // Make sure overlay is visible
        FadeIn();
    }
    
    /// <summary>
    /// Fade in the overlay
    /// </summary>
    private void FadeIn()
    {
        // Ensure GameObject is active
        gameObject.SetActive(true);
        
        // Stop any active fade
        if (activeFadeCoroutine != null)
        {
            StopCoroutine(activeFadeCoroutine);
        }
        
        // Start fade in
        activeFadeCoroutine = StartCoroutine(FadeCanvasGroupRoutine(overlayCanvasGroup, 1f, fadeInDuration, () => {
            overlayCanvasGroup.interactable = true;
            overlayCanvasGroup.blocksRaycasts = true;
        }));
    }
    
    /// <summary>
    /// Fade out the overlay
    /// </summary>
    private void FadeOut()
    {
        // Stop any active fade
        if (activeFadeCoroutine != null)
        {
            StopCoroutine(activeFadeCoroutine);
        }
        
        // Start fade out
        overlayCanvasGroup.interactable = false;
        overlayCanvasGroup.blocksRaycasts = false;
        
        activeFadeCoroutine = StartCoroutine(FadeCanvasGroupRoutine(overlayCanvasGroup, 0f, fadeOutDuration, () => {
            gameObject.SetActive(false);
            isCountdownRunning = false;
        }));
    }
    
    /// <summary>
    /// The main countdown routine
    /// </summary>
    private IEnumerator CountdownRoutine(int seconds, UnityAction onComplete)
    {
        isCountdownRunning = true;
        
        // Fade in the overlay
        FadeIn();
        
        // Reset text color
        if (countdownText != null)
        {
            countdownText.color = Color.white;
        }
        
        // Count down from the specified number to 1
        for (int i = seconds; i >= 1; i--)
        {
            if (countdownText != null)
            {
                countdownText.text = i.ToString();
            }
            
            Debug.Log($"Countdown: {i}");
            yield return new WaitForSeconds(1f);
        }
        
        // Show "Starting..." message
        if (countdownText != null)
        {
            countdownText.text = "Starting...";
        }
        
        // Wait a moment before calling completion callback
        yield return new WaitForSeconds(1f);
        
        // Fade out the overlay
        FadeOut();
        
        // Call the completion callback after a short delay
        onComplete?.Invoke();
        
        // Clear the coroutine reference
        activeCountdownCoroutine = null;
    }
    
    /// <summary>
    /// Fade a CanvasGroup to a target alpha over time
    /// </summary>
    private IEnumerator FadeCanvasGroupRoutine(CanvasGroup canvasGroup, float targetAlpha, float duration, UnityAction onComplete)
    {
        float startAlpha = canvasGroup.alpha;
        float elapsedTime = 0;
        
        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsedTime / duration);
            
            // Use smooth step for nicer easing
            float smoothProgress = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smoothProgress);
            
            yield return null;
        }
        
        // Ensure we end at exactly the target alpha
        canvasGroup.alpha = targetAlpha;
        
        // Call the completion callback
        onComplete?.Invoke();
        
        // Clear coroutine reference
        activeFadeCoroutine = null;
    }
}
