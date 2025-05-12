using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using System.Collections;

/// <summary>
/// Sets up the character selection system for the lobby.
/// Handles automatic assignment of characters to players.
/// </summary>
public class CharacterSelectSetup : MonoBehaviour
{
    [SerializeField] private ButtonCharacterSelect[] characterButtons;
    [SerializeField] private int[] comingSoonIndices; // Array of character indices that should be marked as "coming soon"
    
    private void Start()
    {
        // Make sure we have a CharacterSelectionManager in the scene
        CharacterSelectionManager selectionManager = FindObjectOfType<CharacterSelectionManager>();
        if (selectionManager == null)
        {
            GameObject managerObj = new GameObject("CharacterSelectionManager");
            selectionManager = managerObj.AddComponent<CharacterSelectionManager>();
            Debug.Log("[CharacterSelectSetup] CharacterSelectionManager created automatically");
        }
        
        // Set up coming soon characters
        if (comingSoonIndices != null && comingSoonIndices.Length > 0)
        {
            foreach (int index in comingSoonIndices)
            {
                if (index >= 0 && index < characterButtons.Length)
                {
                    // First ensure the GameObject is active before calling IsComingSoon
                    ButtonCharacterSelect button = characterButtons[index];
                    if (button != null)
                    {
                        // Make sure the GameObject is active before setting properties that might start coroutines
                        if (!button.gameObject.activeSelf)
                        {
                            button.gameObject.SetActive(true);
                        }
                        
                        button.IsComingSoon = true;
                        Debug.Log($"[CharacterSelectSetup] Marked character {index} as coming soon");
                    }
                }
            }
        }
        
        // Register character buttons with the manager
        if (characterButtons != null && characterButtons.Length > 0)
        {
            foreach (ButtonCharacterSelect button in characterButtons)
            {
                if (button != null)
                {
                    // Register with the manager to make available for auto-assignment
                    selectionManager.RegisterCharacterButton(button);
                    Debug.Log($"[CharacterSelectSetup] Registered character button with index {button.CharacterIndex}");
                }
            }
        }
        else
        {
            // Auto-detect character buttons if not assigned
            ButtonCharacterSelect[] foundButtons = FindObjectsOfType<ButtonCharacterSelect>();
            if (foundButtons != null && foundButtons.Length > 0)
            {
                characterButtons = foundButtons;
                foreach (ButtonCharacterSelect button in foundButtons)
                {
                    if (button != null)
                    {
                        selectionManager.RegisterCharacterButton(button);
                    }
                }
                Debug.Log($"[CharacterSelectSetup] Auto-detected and registered {foundButtons.Length} character buttons");
            }
            else
            {
                Debug.LogWarning("[CharacterSelectSetup] No character buttons found in the scene!");
            }
        }
        
        // If we are the host, trigger auto-assignment for ourselves after a delay
        StartCoroutine(TriggerAutoAssignmentForLocalPlayer());
    }
    
    // Wait a short time, then trigger auto-assignment for the local player
    private IEnumerator TriggerAutoAssignmentForLocalPlayer()
    {
        // Wait a moment to ensure network is initialized
        yield return new WaitForSeconds(0.5f);
        
        // Check if we're connected as host or client
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            Debug.Log($"[CharacterSelectSetup] Local player connected as {(NetworkManager.Singleton.IsHost ? "host" : "client")} with ID {NetworkManager.Singleton.LocalClientId}");
            
            // Force trigger the auto-assignment for the local player
            if (CharacterSelectionManager.Instance != null)
            {
                Debug.Log($"[CharacterSelectSetup] Manually triggering auto-assignment for client {NetworkManager.Singleton.LocalClientId}");
                CharacterSelectionManager.Instance.AutoAssignCharacter(NetworkManager.Singleton.LocalClientId);
            }
        }
    }
}
