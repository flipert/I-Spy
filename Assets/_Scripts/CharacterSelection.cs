using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Helper script for character selection UI. Attach to a character selection container object.
/// </summary>
public class CharacterSelection : MonoBehaviour
{
    [Header("Character References")]
    [SerializeField] private GameObject[] characterModels;
    [SerializeField] private string[] characterNames;
    [SerializeField] private int defaultCharacterIndex = 0;
    
    [Header("UI Elements")]
    [SerializeField] private Button nextButton;
    [SerializeField] private Button prevButton;
    [SerializeField] private TextMeshProUGUI characterNameText;
    [SerializeField] private Transform characterPreviewParent;
    
    private int currentCharacterIndex = 0;
    private GameObject currentCharacterPreview;
    
    private void Start()
    {
        // Setup UI buttons
        if (nextButton != null)
            nextButton.onClick.AddListener(NextCharacter);
        
        if (prevButton != null)
            prevButton.onClick.AddListener(PreviousCharacter);
        
        // Initialize with default character
        currentCharacterIndex = defaultCharacterIndex;
        ShowCharacter(currentCharacterIndex);
        
        // Notify NetworkManagerUI of initial selection
        if (NetworkManagerUI.Instance != null)
        {
            NetworkManagerUI.Instance.SelectCharacter(currentCharacterIndex);
        }
    }
    
    public void NextCharacter()
    {
        currentCharacterIndex++;
        if (currentCharacterIndex >= characterModels.Length)
            currentCharacterIndex = 0;
        
        ShowCharacter(currentCharacterIndex);
        
        // Update selection in NetworkManagerUI
        if (NetworkManagerUI.Instance != null)
        {
            NetworkManagerUI.Instance.SelectCharacter(currentCharacterIndex);
        }
    }
    
    public void PreviousCharacter()
    {
        currentCharacterIndex--;
        if (currentCharacterIndex < 0)
            currentCharacterIndex = characterModels.Length - 1;
        
        ShowCharacter(currentCharacterIndex);
        
        // Update selection in NetworkManagerUI
        if (NetworkManagerUI.Instance != null)
        {
            NetworkManagerUI.Instance.SelectCharacter(currentCharacterIndex);
        }
    }
    
    private void ShowCharacter(int index)
    {
        // Destroy previous preview if it exists
        if (currentCharacterPreview != null)
            Destroy(currentCharacterPreview);
        
        // Validate index
        if (index < 0 || index >= characterModels.Length || characterModels[index] == null)
            return;
        
        // Update character name if available
        if (characterNameText != null && characterNames != null && index < characterNames.Length)
        {
            characterNameText.text = characterNames[index];
        }
        
        // Create new preview
        Transform previewParent = characterPreviewParent != null ? characterPreviewParent : transform;
        currentCharacterPreview = Instantiate(characterModels[index], previewParent);
        
        // Setup preview for UI display (face camera, etc.)
        SetupCharacterPreview(currentCharacterPreview);
    }
    
    private void SetupCharacterPreview(GameObject preview)
    {
        // Reset position and rotation for clean preview
        if (preview != null)
        {
            preview.transform.localPosition = Vector3.zero;
            preview.transform.localRotation = Quaternion.identity;
            
            // Additional setup like disabling components, adjusting scale, etc.
            // Disable any scripts that might interfere with preview
            MonoBehaviour[] scripts = preview.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour script in scripts)
            {
                if (script != null && !(script is Animator)) // Keep animator enabled
                {
                    script.enabled = false;
                }
            }
            
            // Make sure Animator is playing the idle animation
            Animator animator = preview.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetBool("Running", false);
            }
        }
    }
}
