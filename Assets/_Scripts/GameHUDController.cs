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
    [SerializeField] private float targetDisplayDuration = 3f;
    [SerializeField] private AnimationCurve targetDisplayCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    [Header("Hunter Indicators")]
    [SerializeField] private GameObject[] hunterIndicators; // Array of up to 4 indicators
    
    [Header("Player Info")]
    [SerializeField] private Sprite defaultAvatar; // Fallback avatar if PlayerRegistry not available
    
    private Coroutine targetDisplayCoroutine;
    
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
        }
        
        // Update hunter indicators
        UpdateHunterIndicators(localPlayerID);
    }
    
    private void ShowTargetPortrait(ulong targetPlayerID)
    {
        if (targetPanel == null || targetPortrait == null) return;
        
        // Get target player avatar from registry
        Sprite targetAvatar = null;
        
        if (PlayerRegistry.Instance != null)
        {
            targetAvatar = PlayerRegistry.Instance.GetPlayerAvatar(targetPlayerID);
            
            // Also get player name if we want to show it
            PlayerRegistry.PlayerData playerData = PlayerRegistry.Instance.GetPlayerData(targetPlayerID);
            string playerName = playerData != null ? playerData.playerName : $"Player {targetPlayerID}";
            
            // Could show player name in addition to "TARGET" label
            Debug.Log($"Target is {playerName}");
        }
        
        // If no avatar from registry, use default
        if (targetAvatar == null)
        {
            targetAvatar = defaultAvatar;
        }
        
        // Set the avatar sprite
        targetPortrait.sprite = targetAvatar;
        
        // Set target label text
        if (targetLabel != null)
        {
            targetLabel.text = "TARGET";
        }
        
        // Stop any existing display coroutine
        if (targetDisplayCoroutine != null)
        {
            StopCoroutine(targetDisplayCoroutine);
        }
        
        // Start the display coroutine
        targetDisplayCoroutine = StartCoroutine(ShowTargetPortraitCoroutine());
    }
    
    private IEnumerator ShowTargetPortraitCoroutine()
    {
        // Show the panel
        targetPanel.SetActive(true);
        
        // Animate in
        float startTime = Time.time;
        CanvasGroup canvasGroup = targetPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = targetPanel.AddComponent<CanvasGroup>();
        }
        
        // Fade in
        canvasGroup.alpha = 0f;
        while (Time.time < startTime + 0.5f)
        {
            float t = (Time.time - startTime) / 0.5f;
            canvasGroup.alpha = targetDisplayCurve.Evaluate(t);
            yield return null;
        }
        canvasGroup.alpha = 1f;
        
        // Wait for display duration
        yield return new WaitForSeconds(targetDisplayDuration);
        
        // Fade out
        startTime = Time.time;
        while (Time.time < startTime + 0.5f)
        {
            float t = (Time.time - startTime) / 0.5f;
            canvasGroup.alpha = 1f - targetDisplayCurve.Evaluate(t);
            yield return null;
        }
        canvasGroup.alpha = 0f;
        
        // Hide the panel
        targetPanel.SetActive(false);
        
        targetDisplayCoroutine = null;
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
} 