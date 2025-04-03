using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Services.Lobbies.Models;
using System;

public class LobbyEntryUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI lobbyNameText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private Button joinButton;

    private Lobby lobby;
    private Action onJoinPressed;

    public void Initialize(Lobby lobby, Action onJoinPressed)
    {
        this.lobby = lobby;
        this.onJoinPressed = onJoinPressed;

        // Set up UI elements
        if (lobbyNameText)
        {
            lobbyNameText.text = lobby.Name;
        }

        if (playerCountText)
        {
            playerCountText.text = $"Players: {lobby.Players.Count}/{lobby.MaxPlayers}";
        }

        if (joinButton)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => onJoinPressed?.Invoke());
        }
    }
} 