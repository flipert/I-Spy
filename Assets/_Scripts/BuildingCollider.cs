using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class BuildingCollider : MonoBehaviour
{
    [Header("Collision Settings")]
    [Tooltip("Tag to identify this as a building for NPCs to avoid")]
    public string buildingTag = "Building";
    
    [Tooltip("Should this building block player movement")]
    public bool blockPlayer = true;
    
    [Tooltip("Should this building block NPC movement")]
    public bool blockNPC = true;
    
    private void Awake()
    {
        // Make sure the collider is not a trigger
        Collider collider = GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = false;
            
            // Tag this object for NPC pathfinding
            gameObject.tag = buildingTag;
            
            // Log setup for debugging
            Debug.Log($"BuildingCollider initialized on {gameObject.name}. isTrigger: {collider.isTrigger}");
        }
        else
        {
            Debug.LogError($"BuildingCollider on {gameObject.name} requires a Collider component!");
        }
    }
    
    private void OnValidate()
    {
        // This ensures the collider is not a trigger in the editor
        Collider collider = GetComponent<Collider>();
        if (collider != null && collider.isTrigger)
        {
            collider.isTrigger = false;
            Debug.Log("BuildingCollider: Collider set to non-trigger mode");
        }
    }
}
