using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class GameHUDController : MonoBehaviour
{
    [Header("Target Display")]
    [SerializeField] private GameObject targetPanel;
    [SerializeField] private Image targetPortrait;
    [SerializeField] private TextMeshProUGUI targetLabel;
    
    [Header("Hunter Indicators")]
    [SerializeField] private GameObject[] hunterIndicators; // Array of up to 4 indicators
    
    private ulong currentTargetId;
    
    private void Start()
    {
        // Hide target panel initially
        if (targetPanel != null)
        {
            targetPanel.SetActive(false);
        }
        
        // Hide all hunter indicators initially
        if (hunterIndicators != null)
        {
            foreach (var indicator in hunterIndicators)
            {
                if (indicator != null)
                {
                    indicator.SetActive(false);
                }
            }
        }
        
        // Subscribe to the GameManager's target assigned event
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TargetsAssigned += OnTargetsAssigned;
        }
        else
        {
            Debug.LogError("GameManager.Instance is null. Make sure it exists in the scene.");
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TargetsAssigned -= OnTargetsAssigned;
        }
    }
    
    private void OnTargetsAssigned()
    {
        if (NetworkManager.Singleton == null) return;
        
        // Get the local player's assigned target
        ulong localPlayerID = NetworkManager.Singleton.LocalClientId;
        ulong targetPlayerID = GameManager.Instance.GetTargetForPlayer(localPlayerID);
        
        if (targetPlayerID != 0)
        {
            // Show the target portrait
            ShowTargetPortrait(targetPlayerID);
            currentTargetId = targetPlayerID;
        }
        
        // Update hunter indicators
        UpdateHunterIndicators(localPlayerID);
    }
    
    private void ShowTargetPortrait(ulong targetPlayerID)
    {
        if (targetPanel == null || targetPortrait == null) return;
        
        // Get target player avatar from registry
        Sprite targetAvatar = null;
        string playerName = $"Player {targetPlayerID}";
        
        if (PlayerRegistry.Instance != null)
        {
            targetAvatar = PlayerRegistry.Instance.GetPlayerAvatar(targetPlayerID);
            
            // Also get player name 
            PlayerRegistry.PlayerData playerData = PlayerRegistry.Instance.GetPlayerData(targetPlayerID);
            if (playerData != null)
            {
                playerName = playerData.playerName;
            }
            
            Debug.Log($"Target is {playerName}");
        }
        
        // Set the avatar sprite if available
        if (targetAvatar != null)
        {
            targetPortrait.sprite = targetAvatar;
        }
        
        // Set target label text
        if (targetLabel != null)
        {
            targetLabel.text = "TARGET: " + playerName;
        }
        
        // Show the panel immediately
        targetPanel.SetActive(true);
        
        // Make sure it's fully visible
        CanvasGroup canvasGroup = targetPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = targetPanel.AddComponent<CanvasGroup>();
        }
        canvasGroup.alpha = 1f;
    }
    
    public void HideTargetPortrait()
    {
        if (targetPanel != null)
        {
            targetPanel.SetActive(false);
        }
    }
    
    // Call this when the player kills their target
    public void OnTargetKilled()
    {
        HideTargetPortrait();
        currentTargetId = 0;
    }
    
    private void UpdateHunterIndicators(ulong playerId)
    {
        if (hunterIndicators == null || hunterIndicators.Length == 0) return;
        
        // Get the number of hunters targeting the player
        int hunterCount = GameManager.Instance.GetHunterCountForPlayer(playerId);
        
        // Limit the hunter count to the number of available indicators
        hunterCount = Mathf.Min(hunterCount, hunterIndicators.Length);
        
        // Update the indicators
        for (int i = 0; i < hunterIndicators.Length; i++)
        {
            if (hunterIndicators[i] != null)
            {
                // Show indicators up to the hunter count
                hunterIndicators[i].SetActive(i < hunterCount);
            }
        }
        
        Debug.Log($"Updated hunter indicators: {hunterCount} hunters");
    }
    
    // Public method to manually update the HUD
    public void UpdateHUD()
    {
        if (NetworkManager.Singleton == null) return;
        
        ulong localPlayerID = NetworkManager.Singleton.LocalClientId;
        UpdateHunterIndicators(localPlayerID);
    }
    
    // Public method to get current target ID
    public ulong GetCurrentTargetId()
    {
        return currentTargetId;
    }
} 