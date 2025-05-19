using UnityEngine;
using System.Collections.Generic;

public class ObstructionManager : MonoBehaviour
{
    public Camera mainCamera;
    public LayerMask obstructionLayer; // Assign your "Buildings" layer in the Inspector
    // public float maxDistance = 100f; // Max distance for raycasting - no longer directly used in this way

    private List<CharacterOutline> charactersInScene = new List<CharacterOutline>();

    void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("ObstructionManager: Main Camera not found. Please assign it manually.");
                enabled = false;
                return;
            }
        }
        RefreshCharacterList();
    }

    public void RefreshCharacterList()
    {
        charactersInScene.Clear();
        charactersInScene.AddRange(FindObjectsOfType<CharacterOutline>());
    }

    public void RegisterCharacter(CharacterOutline character)
    {
        if (!charactersInScene.Contains(character))
        {
            charactersInScene.Add(character);
        }
    }

    public void UnregisterCharacter(CharacterOutline character)
    {
        if (charactersInScene.Contains(character))
        {
            charactersInScene.Remove(character);
        }
    }

    void LateUpdate()
    {
        if (mainCamera == null) return;

        for (int i = charactersInScene.Count - 1; i >= 0; i--) // Iterate backwards for safe removal
        {
            CharacterOutline character = charactersInScene[i];
            if (character == null) // Character might have been destroyed
            {
                charactersInScene.RemoveAt(i);
                continue;
            }
            if (!character.gameObject.activeInHierarchy) 
            {
                character.SetOutlineVisibility(false); // Ensure outline is off if character is inactive
                continue;
            }

            Vector3 characterVisualCenter = character.CharacterVisualCenter;
            bool isObstructed = false;

            // Get the character's position in viewport space.
            Vector3 viewportPos = mainCamera.WorldToViewportPoint(characterVisualCenter);

            // Check if the character is within the camera's viewport and in front of the near clip plane.
            // viewportPos.z < 0 means behind the camera.
            if (viewportPos.z > mainCamera.nearClipPlane && 
                viewportPos.x >= 0 && viewportPos.x <= 1 && 
                viewportPos.y >= 0 && viewportPos.y <= 1)
            {
                // Create a ray from the camera that passes through the character's position on the screen.
                // For orthographic, ViewportPointToRay with z=0 for the viewport coord effectively starts the ray on/near the camera's viewing plane.
                Ray ray = mainCamera.ViewportPointToRay(new Vector3(viewportPos.x, viewportPos.y, 0f)); 

                // Calculate the distance from the ray's origin (on the camera's viewing plane) to the character.
                float distanceToCharacter = Vector3.Dot(characterVisualCenter - ray.origin, ray.direction);
                
                // Raycast from camera's viewing path towards the character.
                // Stop the ray just short of the character's actual position to see if anything is in between.
                // Make sure distanceToCharacter is positive before raycasting.
                if (distanceToCharacter > 0.05f) // Ensure character is not too close to ray origin
                {
                    if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, distanceToCharacter - 0.05f, obstructionLayer))
                    {
                        // Check that the hit object is not the character itself or a part of it.
                        // This is a safeguard if character parts could be on the obstructionLayer.
                        if (hit.transform != character.transform && !character.transform.IsChildOf(hit.transform))
                        {
                            isObstructed = true;
                        }
                    }
                }
            }
            
            character.SetOutlineVisibility(isObstructed);
        }
    }
} 