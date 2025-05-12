using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple script to attach to a button that will force character assignment.
/// This is a fallback in case automatic assignment doesn't work.
/// </summary>
[RequireComponent(typeof(Button))]
public class AssignCharacterButton : MonoBehaviour
{
    private Button button;
    
    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnButtonClick);
    }
    
    private void OnButtonClick()
    {
        Debug.Log("[AssignCharacterButton] Manual character assignment button clicked");
        
        if (CharacterSelectionManager.Instance != null)
        {
            CharacterSelectionManager.Instance.ForceCharacterAssignment();
        }
        else
        {
            Debug.LogError("[AssignCharacterButton] CharacterSelectionManager.Instance is null!");
        }
    }
    
    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnButtonClick);
        }
    }
}
