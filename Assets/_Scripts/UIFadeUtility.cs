using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Utility class for handling smooth fade transitions on UI elements with CanvasGroup components.
/// </summary>
public static class UIFadeUtility
{
    /// <summary>
    /// Fades a CanvasGroup to the target alpha over duration.
    /// </summary>
    /// <param name="monoBehaviour">MonoBehaviour instance to run the coroutine</param>
    /// <param name="canvasGroup">CanvasGroup to fade</param>
    /// <param name="targetAlpha">Target alpha value (0 = transparent, 1 = opaque)</param>
    /// <param name="duration">Duration of the fade in seconds</param>
    /// <param name="onComplete">Optional callback when fade completes</param>
    /// <returns>The coroutine handle</returns>
    public static Coroutine FadeCanvasGroup(
        MonoBehaviour monoBehaviour, 
        CanvasGroup canvasGroup, 
        float targetAlpha, 
        float duration, 
        UnityAction onComplete = null)
    {
        if (canvasGroup == null || monoBehaviour == null)
            return null;
            
        return monoBehaviour.StartCoroutine(FadeCanvasGroupRoutine(canvasGroup, targetAlpha, duration, onComplete));
    }

    /// <summary>
    /// Shows a GameObject with a CanvasGroup by fading it in.
    /// </summary>
    /// <param name="monoBehaviour">MonoBehaviour instance to run the coroutine</param>
    /// <param name="gameObject">GameObject to show and fade in</param>
    /// <param name="duration">Duration of the fade in seconds</param>
    /// <param name="onComplete">Optional callback when fade completes</param>
    /// <returns>The coroutine handle</returns>
    public static Coroutine FadeIn(
        MonoBehaviour monoBehaviour, 
        GameObject gameObject, 
        float duration = 0.25f, 
        UnityAction onComplete = null)
    {
        if (gameObject == null || monoBehaviour == null)
            return null;
            
        var canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        
        // Ensure the object is active before fading in
        gameObject.SetActive(true);
        
        // Start with alpha at 0
        canvasGroup.alpha = 0f;
        
        // Enable interaction
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        
        return FadeCanvasGroup(monoBehaviour, canvasGroup, 1f, duration, onComplete);
    }

    /// <summary>
    /// Hides a GameObject with a CanvasGroup by fading it out, then deactivates it.
    /// </summary>
    /// <param name="monoBehaviour">MonoBehaviour instance to run the coroutine</param>
    /// <param name="gameObject">GameObject to fade out and hide</param>
    /// <param name="duration">Duration of the fade in seconds</param>
    /// <param name="onComplete">Optional callback when fade completes</param>
    /// <returns>The coroutine handle</returns>
    public static Coroutine FadeOut(
        MonoBehaviour monoBehaviour, 
        GameObject gameObject, 
        float duration = 0.25f, 
        UnityAction onComplete = null)
    {
        if (gameObject == null || monoBehaviour == null)
            return null;
            
        var canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        
        // Disable interaction immediately when starting fade out
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        
        // Create a wrapped callback that deactivates the GameObject
        UnityAction wrappedCallback = () => 
        {
            gameObject.SetActive(false);
            onComplete?.Invoke();
        };
        
        return FadeCanvasGroup(monoBehaviour, canvasGroup, 0f, duration, wrappedCallback);
    }

    /// <summary>
    /// Coroutine that handles the actual fading of a CanvasGroup.
    /// </summary>
    private static IEnumerator FadeCanvasGroupRoutine(
        CanvasGroup canvasGroup, 
        float targetAlpha, 
        float duration, 
        UnityAction onComplete)
    {
        float startAlpha = canvasGroup.alpha;
        float startTime = Time.time;
        float elapsedTime = 0;
        
        // Ensure target alpha is valid
        targetAlpha = Mathf.Clamp01(targetAlpha);
        
        while (elapsedTime < duration)
        {
            elapsedTime = Time.time - startTime;
            float normalizedTime = Mathf.Clamp01(elapsedTime / duration);
            
            // Use smoothstep for a more pleasant easing
            float smoothProgress = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);
            
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smoothProgress);
            
            yield return null;
        }
        
        // Ensure we end at the exact target alpha
        canvasGroup.alpha = targetAlpha;
        
        // Call the completion callback if provided
        onComplete?.Invoke();
    }
}
