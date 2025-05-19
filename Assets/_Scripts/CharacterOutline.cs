using UnityEngine;
using System.Collections; // Required for Coroutines

[RequireComponent(typeof(SpriteRenderer))]
public class CharacterOutline : MonoBehaviour
{
    public Color outlineColor = Color.green;
    public float outlineThicknessPixels = 2f; // Thickness of the outline in pixels
    public float fadeDuration = 0.25f;      // Duration of the fade in/out effect

    private SpriteRenderer spriteRenderer;
    private GameObject outlineObject;
    private SpriteRenderer outlineSpriteRenderer;
    private Coroutine activeFadeCoroutine;
    private float baseOutlineAlpha; // Target alpha when fully visible

    private static readonly int MainTex = Shader.PropertyToID("_MainTex");

    public Vector3 CharacterVisualCenter 
    {
        get 
        {
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                return spriteRenderer.bounds.center;
            }
            // Fallback if sprite or renderer is not available
            return transform.position;
        }
    }

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        outlineObject = new GameObject("Outline");
        outlineObject.transform.SetParent(transform);
        outlineObject.transform.localPosition = new Vector3(0, 0, 0.05f); // Slightly offset Z for sorting, if needed
        outlineObject.transform.localRotation = Quaternion.identity;
        outlineObject.transform.localScale = Vector3.one;

        outlineSpriteRenderer = outlineObject.AddComponent<SpriteRenderer>();
        
        outlineSpriteRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        outlineSpriteRenderer.sortingOrder = spriteRenderer.sortingOrder - 1; // Render outline behind main sprite
        
        Shader alwaysOnTopShader = Shader.Find("Sprites/AlwaysOnTop");
        if (alwaysOnTopShader == null)
        {
            Debug.LogError("CharacterOutline: Sprites/AlwaysOnTop shader not found. Please ensure it exists.", this);
            outlineSpriteRenderer.material = new Material(Shader.Find("Sprites/Default"));
        }
        else
        {
            outlineSpriteRenderer.material = new Material(alwaysOnTopShader);
        }
        
        // Set material base color (RGB from outlineColor, alpha will be controlled by SpriteRenderer.color)
        outlineSpriteRenderer.material.color = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 1f);

        // Store target alpha and initialize current alpha for fading
        baseOutlineAlpha = outlineColor.a > 0.001f ? outlineColor.a : 1f; // Use outlineColor's alpha, or 1 if it's 0.
        Color initialSpriteColor = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0f);
        outlineSpriteRenderer.color = initialSpriteColor; // Start fully transparent

        outlineObject.SetActive(true); // Keep outline object active, visibility controlled by alpha
    }

    void LateUpdate()
    {
        if (spriteRenderer.sprite == null)
        {
            // If main sprite is gone, ensure outline is hidden (e.g. fade out or set alpha to 0)
            if (outlineSpriteRenderer.color.a > 0) SetOutlineVisibility(false);
            return;
        }

        if (outlineSpriteRenderer.sprite != spriteRenderer.sprite)
        {
            outlineSpriteRenderer.sprite = spriteRenderer.sprite;
        }

        // Sync flip state with parent sprite renderer
        outlineSpriteRenderer.flipX = spriteRenderer.flipX;
        outlineSpriteRenderer.flipY = spriteRenderer.flipY;
        
        // Adjust scale for outline effect
        // This calculation aims for an outline `outlineThicknessPixels` thick, irrespective of sprite's PPU or transform scale.
        if (spriteRenderer.sprite.rect.width > 0 && spriteRenderer.sprite.rect.height > 0)
        {
            float parentAbsScaleX = Mathf.Abs(transform.lossyScale.x);
            if (parentAbsScaleX < 0.0001f) parentAbsScaleX = 0.0001f; // Prevent division by zero or extremely small scale issues
            
            float parentAbsScaleY = Mathf.Abs(transform.lossyScale.y);
            if (parentAbsScaleY < 0.0001f) parentAbsScaleY = 0.0001f;

            // Calculate local scale factor for the outline object
            float scaleFactorX = 1f + (2f * outlineThicknessPixels) / (spriteRenderer.sprite.rect.width * parentAbsScaleX);
            float scaleFactorY = 1f + (2f * outlineThicknessPixels) / (spriteRenderer.sprite.rect.height * parentAbsScaleY);
            
            outlineObject.transform.localScale = new Vector3(scaleFactorX, scaleFactorY, 1f);
        }
    }

    public void SetOutlineVisibility(bool visible)
    {
        if (outlineObject == null || outlineSpriteRenderer == null) return;

        if (activeFadeCoroutine != null)
        {
            StopCoroutine(activeFadeCoroutine);
        }
        
        float targetAlpha = visible ? baseOutlineAlpha : 0f;
        
        // Don't start fade if already at target alpha (and object active state matches)
        if (outlineObject.activeInHierarchy && Mathf.Approximately(outlineSpriteRenderer.color.a, targetAlpha))
        {
             return;
        }

        activeFadeCoroutine = StartCoroutine(FadeOutlineCoroutine(targetAlpha));
    }

    private IEnumerator FadeOutlineCoroutine(float targetAlpha)
    {
        float currentAlpha = outlineSpriteRenderer.color.a;
        float timer = 0f;

        Color newColor = outlineSpriteRenderer.color;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            newColor.a = Mathf.Lerp(currentAlpha, targetAlpha, timer / fadeDuration);
            outlineSpriteRenderer.color = newColor;
            yield return null;
        }

        newColor.a = targetAlpha;
        outlineSpriteRenderer.color = newColor;
        activeFadeCoroutine = null;
    }

    void OnDestroy()
    {
        if (outlineObject != null)
        {
            Destroy(outlineObject);
        }
        if (activeFadeCoroutine != null)
        {
            StopCoroutine(activeFadeCoroutine);
        }
    }
} 