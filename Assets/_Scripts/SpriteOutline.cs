// SpriteOutline.cs
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SpriteOutline : MonoBehaviour
{
    [Tooltip("Colour of the outline")] public Color outlineColor = Color.green;
    [Tooltip("Pixel offset for the outline")] [Range(0.01f, 0.1f)]
    public float thickness = 0.03f;

    [Tooltip("Force outline for debugging")] public bool forceOutlineForDebug = false;

    // Layer with the building colliders
    [SerializeField] private LayerMask buildingsMask;

    // Private
    SpriteRenderer body;
    SpriteRenderer[] outlineParts;
    Camera mainCam;

    readonly Vector3[] offsets =
    {
        Vector3.up, Vector3.down, Vector3.left, Vector3.right
    };

    void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        mainCam = Camera.main;

        // Build outline children
        outlineParts = new SpriteRenderer[offsets.Length];
        for (int i = 0; i < offsets.Length; i++)
        {
            var go = new GameObject("OutlinePart");
            go.transform.SetParent(transform, false);
            outlineParts[i] = go.AddComponent<SpriteRenderer>();

            // Copy rendering settings
            CopySettings(body, outlineParts[i]);
            outlineParts[i].color = outlineColor;
            outlineParts[i].sortingOrder = body.sortingOrder;   // doesn't matter, OutlineCam draws last
            go.layer = LayerMask.NameToLayer("OutlineFx");
        }
        UpdateOutlineSprite(); // initial sprite
        ToggleOutline(false);  // start disabled
    }

    void LateUpdate()
    {
        // 0) Debug force toggle
        if (forceOutlineForDebug)
        {
            ToggleOutline(true);
            if (outlineParts.Length > 0 && outlineParts[0].sprite != body.sprite) UpdateOutlineSprite(); // Keep sprite updated even when forced
            return;
        }

        // 1) Always keep correct sprite (animations swap sprites every frame)
        if (outlineParts[0].sprite != body.sprite) UpdateOutlineSprite();

        // 2) Check if a building blocks the view
        Vector3 characterCenter = body.bounds.center;
        Vector3 screenPoint = mainCam.WorldToScreenPoint(characterCenter);
        Ray ray = mainCam.ScreenPointToRay(screenPoint);

        bool blocked = Physics.Linecast(ray.origin, characterCenter, buildingsMask);

        // Visualize the Linecast in the Scene view
        Color lineColor = blocked ? Color.red : Color.green; // Red if blocked, green if not
        Debug.DrawLine(ray.origin, characterCenter, lineColor, 0.01f);

        ToggleOutline(blocked);
    }

    void UpdateOutlineSprite()
    {
        foreach (var part in outlineParts) part.sprite = body.sprite;
    }

    void ToggleOutline(bool on)
    {
        if (outlineParts[0].enabled == on) return;
        foreach (var part in outlineParts) part.enabled = on;
    }

    // Small helper
    void CopySettings(SpriteRenderer from, SpriteRenderer to)
    {
        to.sprite = from.sprite;
        to.sortingLayerID = from.sortingLayerID;
        to.flipX = from.flipX;
        to.flipY = from.flipY;
    }

    void OnValidate()
    {
        // Ensure outlineParts is initialized and has elements before proceeding.
        // This prevents errors when the script is added or recompiled in the editor.
        if (outlineParts == null || outlineParts.Length == 0) return;

        // Also ensure that the number of outline parts matches the expected number of offsets.
        // This can prevent errors if something unexpected changes the array size.
        if (outlineParts.Length != offsets.Length) return; 

        for (int i = 0; i < offsets.Length; i++)
        {
            // Additional check for individual null elements, though less likely if Awake logic is sound.
            if (outlineParts[i] != null && outlineParts[i].transform != null)
            {
                outlineParts[i].transform.localPosition = offsets[i] * thickness;
            }
        }
    }

    void Start() => OnValidate(); // apply initial offsets
} 