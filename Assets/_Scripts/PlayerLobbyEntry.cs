using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerLobbyEntry : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI playerStatusText;
    [SerializeField] private Image characterIconImage;
    [SerializeField] private GameObject hostBadge;
    [SerializeField] private GameObject readyIndicator;
    
    [Header("Character Icons")]
    [SerializeField] private Sprite[] characterIcons; // Icons corresponding to each character
    
    private ulong clientId;
    private bool isReady = false;
    private int characterIndex = -1;
    
    public void SetPlayerInfo(string playerName, ulong clientId, bool isLocalPlayer, bool isHost)
    {
        this.clientId = clientId;
        
        // Set player name
        if (playerNameText != null)
        {
            playerNameText.text = playerName;
            
            // Highlight local player's name
            if (isLocalPlayer)
            {
                playerNameText.color = Color.green;
            }
            else
            {
                playerNameText.color = Color.white;
            }
        }
        
        // Set player status
        if (playerStatusText != null)
        {
            playerStatusText.text = isReady ? "Ready" : "Not Ready";
        }
        
        // Show/hide host badge
        if (hostBadge != null)
        {
            hostBadge.SetActive(isHost);
        }
    }
    
    public void SetReady(bool ready)
    {
        isReady = ready;
        
        if (playerStatusText != null)
        {
            playerStatusText.text = isReady ? "Ready" : "Not Ready";
        }
        
        if (readyIndicator != null)
        {
            readyIndicator.SetActive(isReady);
        }
    }
    
    public void SetCharacterSelection(int index)
    {
        characterIndex = index;
        
        if (characterIconImage != null && characterIcons != null)
        {
            if (index >= 0 && index < characterIcons.Length)
            {
                characterIconImage.sprite = characterIcons[index];
                characterIconImage.gameObject.SetActive(true);
                
                // Also mark as ready when character is selected
                SetReady(true);
            }
            else
            {
                characterIconImage.gameObject.SetActive(false);
                SetReady(false);
            }
        }
    }
    
    public ulong GetClientId()
    {
        return clientId;
    }
    
    public int GetCharacterIndex()
    {
        return characterIndex;
    }
    
    public bool IsReady()
    {
        return isReady;
    }
} 