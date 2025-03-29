using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerListEntry : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI playerStatusText;
    [SerializeField] private Image playerIconImage;
    [SerializeField] private GameObject localPlayerIndicator;
    [SerializeField] private GameObject hostIndicator;
    
    private ulong clientId;
    
    public void SetPlayerInfo(string playerName, ulong clientId, bool isLocalPlayer, bool isHost)
    {
        this.clientId = clientId;
        
        if (playerNameText != null)
            playerNameText.text = playerName;
            
        if (playerStatusText != null)
            playerStatusText.text = isHost ? "Host" : "Client";
            
        // Set indicators
        if (localPlayerIndicator != null)
            localPlayerIndicator.SetActive(isLocalPlayer);
            
        if (hostIndicator != null)
            hostIndicator.SetActive(isHost);
            
        // Optional: Set a different color for the local player
        if (isLocalPlayer && playerNameText != null)
            playerNameText.color = Color.green;
    }
    
    public void SetPlayerStatus(string status)
    {
        if (playerStatusText != null)
            playerStatusText.text = status;
    }
    
    public ulong GetClientId()
    {
        return clientId;
    }
} 