// SpriteOutline.cs
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SpriteOutline : MonoBehaviour
{
    [Tooltip("Colour of the outline")] public Color outlineColor = Color.green;
    [Tooltip("Pixel offset for the outline")] [Range(0.01f, 0.1f)]
    public float thickness = 0.03f;

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
        // 1) Always keep correct sprite (animations swap sprites every frame)
        if (outlineParts[0].sprite != body.sprite) UpdateOutlineSprite();

        // 2) Check if a building blocks the view
        Vector3 characterCenter = body.bounds.center;
        Vector3 camForward = mainCam.transform.forward;
        // Define a start point for the linecast far along the negative camera forward vector from the character
        // This effectively casts from "behind" the character towards it, along the camera's view lines.
        Vector3 linecastStartPoint = characterCenter - camForward * (mainCam.farClipPlane * 0.9f); // Start slightly within far clip plane

        bool blocked = Physics.Linecast(linecastStartPoint, characterCenter, buildingsMask);
        
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
        if (outlineParts == null) return;
        for (int i = 0; i < offsets.Length; i++)
            outlineParts[i].transform.localPosition = offsets[i] * thickness;
    }

    void Start() => OnValidate(); // apply initial offsets
} 