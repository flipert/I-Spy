using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup sceneCanvasGroup;    // Main canvas group for the entire scene
    [SerializeField] private CanvasGroup logoCanvasGroup;     // Canvas group for the game logo
    [SerializeField] private CanvasGroup buttonGroupCanvasGroup; // Canvas group for the button container

    [Header("Animation Settings")]
    [SerializeField] private float initialDelay = 0.5f;       // Delay before starting any animations
    [SerializeField] private float sceneFadeInDuration = 1.0f; // Duration for the entire scene to fade in
    [SerializeField] private float logoDelayAfterScene = 0.3f; // Delay before logo starts fading in
    [SerializeField] private float logoFadeInDuration = 0.8f;  // Duration for the logo to fade in
    [SerializeField] private float buttonDelayAfterLogo = 0.3f; // Delay before buttons start fading in
    [SerializeField] private float buttonFadeInDuration = 0.8f; // Duration for the buttons to fade in

    private void Awake()
    {
        // Initialize all canvas groups to be invisible
        InitializeCanvasGroups();
    }

    private void Start()
    {
        // Start the fade-in sequence
        StartCoroutine(FadeInSequence());
    }

    private void InitializeCanvasGroups()
    {
        // Initialize the scene canvas group if assigned
        if (sceneCanvasGroup != null)
        {
            sceneCanvasGroup.alpha = 0f;
        }
        else
        {
            Debug.LogWarning("Scene CanvasGroup not assigned. Please assign one in the inspector.");
        }

        // Initialize the logo canvas group if assigned
        if (logoCanvasGroup != null)
        {
            logoCanvasGroup.alpha = 0f;
        }
        else
        {
            Debug.LogWarning("Logo CanvasGroup not assigned. Please assign one in the inspector.");
        }

        // Initialize the button group canvas group if assigned
        if (buttonGroupCanvasGroup != null)
        {
            buttonGroupCanvasGroup.alpha = 0f;
        }
        else
        {
            Debug.LogWarning("Button Group CanvasGroup not assigned. Please assign one in the inspector.");
        }
    }

    private IEnumerator FadeInSequence()
    {
        // Initial delay before starting animations
        yield return new WaitForSeconds(initialDelay);

        // 1. Fade in the entire scene first
        yield return StartCoroutine(FadeInCanvasGroup(sceneCanvasGroup, sceneFadeInDuration));

        // Wait before starting logo animation
        yield return new WaitForSeconds(logoDelayAfterScene);

        // 2. Fade in the logo
        yield return StartCoroutine(FadeInCanvasGroup(logoCanvasGroup, logoFadeInDuration));

        // Wait before starting button group animation
        yield return new WaitForSeconds(buttonDelayAfterLogo);

        // 3. Fade in the button group
        yield return StartCoroutine(FadeInCanvasGroup(buttonGroupCanvasGroup, buttonFadeInDuration));
    }

    private IEnumerator FadeInCanvasGroup(CanvasGroup canvasGroup, float duration)
    {
        // Skip if canvas group is not assigned
        if (canvasGroup == null)
            yield break;

        float startTime = Time.time;
        float startAlpha = canvasGroup.alpha;

        while (Time.time < startTime + duration)
        {
            float t = (Time.time - startTime) / duration;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, t);
            yield return null;
        }

        // Ensure we end at exactly 1
        canvasGroup.alpha = 1f;
    }
}
