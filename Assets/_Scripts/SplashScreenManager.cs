using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SplashScreenManager : MonoBehaviour
{
    [Header("Splash Screen Settings")]
    [SerializeField] private float backgroundFadeInDuration = 1.0f;
    [SerializeField] private float displayDuration = 5.0f;
    [SerializeField] private float fadeOutDuration = 1.0f;
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    
    [Header("Logo Animation Settings")]
    [SerializeField] private float logoDelayAfterBackgroundFade = 0.3f;
    [SerializeField] private float logoFadeInDuration = 0.8f;
    
    [Header("References")]
    [SerializeField] private CanvasGroup backgroundCanvasGroup;
    [SerializeField] private CanvasGroup logoCanvasGroup;
    [SerializeField] private RectTransform logoRectTransform;
    
    private Vector3 logoOriginalScale;
    
    private void Awake()
    {
        // If backgroundCanvasGroup wasn't assigned in the inspector, try to get it
        if (backgroundCanvasGroup == null)
        {
            backgroundCanvasGroup = GetComponentInChildren<CanvasGroup>();
            
            if (backgroundCanvasGroup == null)
            {
                Debug.LogError("SplashScreenManager: No background CanvasGroup found. Please assign one in the inspector.");
                return;
            }
        }
        
        // If logoCanvasGroup wasn't assigned, try to find it
        if (logoCanvasGroup == null && logoRectTransform != null)
        {
            logoCanvasGroup = logoRectTransform.GetComponent<CanvasGroup>();
            
            if (logoCanvasGroup == null)
            {
                // Try to add a CanvasGroup to the logo
                logoCanvasGroup = logoRectTransform.gameObject.AddComponent<CanvasGroup>();
            }
        }
        
        // Initialize the canvas groups to be invisible
        backgroundCanvasGroup.alpha = 0f;
        
        if (logoCanvasGroup != null)
        {
            logoCanvasGroup.alpha = 0f;
            
            // Store the original logo scale
            if (logoRectTransform != null)
            {
                logoOriginalScale = logoRectTransform.localScale;
            }
        }
    }
    
    private void Start()
    {
        // Start the splash screen sequence
        StartCoroutine(SplashScreenSequence());
    }
    
    private IEnumerator SplashScreenSequence()
    {
        // Fade in background
        yield return StartCoroutine(FadeInBackground());
        
        // Wait a bit before starting logo animation
        yield return new WaitForSeconds(logoDelayAfterBackgroundFade);
        
        // Animate the logo (fade in + bump)
        StartCoroutine(AnimateLogo());
        
        // Wait for the display duration
        yield return new WaitForSeconds(displayDuration);
        
        // Fade out everything
        yield return StartCoroutine(FadeOut());
        
        // Load the main menu scene
        SceneManager.LoadScene(mainMenuSceneName);
    }
    
    private IEnumerator FadeInBackground()
    {
        float startTime = Time.time;
        
        while (Time.time < startTime + backgroundFadeInDuration)
        {
            float t = (Time.time - startTime) / backgroundFadeInDuration;
            backgroundCanvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }
        
        backgroundCanvasGroup.alpha = 1f;
    }
    
    private IEnumerator AnimateLogo()
    {
        if (logoCanvasGroup == null || logoRectTransform == null)
            yield break;
            
        // Fade in logo
        float fadeStartTime = Time.time;
        
        while (Time.time < fadeStartTime + logoFadeInDuration)
        {
            float t = (Time.time - fadeStartTime) / logoFadeInDuration;
            logoCanvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }
        
        logoCanvasGroup.alpha = 1f;
    }
    
    private IEnumerator FadeOut()
    {
        float startTime = Time.time;
        float backgroundStartAlpha = backgroundCanvasGroup.alpha;
        float logoStartAlpha = logoCanvasGroup != null ? logoCanvasGroup.alpha : 0f;
        
        while (Time.time < startTime + fadeOutDuration)
        {
            float t = (Time.time - startTime) / fadeOutDuration;
            backgroundCanvasGroup.alpha = Mathf.Lerp(backgroundStartAlpha, 0f, t);
            
            if (logoCanvasGroup != null)
            {
                logoCanvasGroup.alpha = Mathf.Lerp(logoStartAlpha, 0f, t);
            }
            
            yield return null;
        }
        
        backgroundCanvasGroup.alpha = 0f;
        if (logoCanvasGroup != null)
        {
            logoCanvasGroup.alpha = 0f;
        }
    }
}
