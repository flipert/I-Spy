using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ArsenalIconController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Image cooldownOverlay; // The dark overlay image with fill method
    [SerializeField] private GameObject cooldownContainer; // The parent object of the overlay, to be enabled/disabled

    private Coroutine cooldownCoroutine;

    private void Awake()
    {
        // Ensure cooldown is not visible at start
        if (cooldownContainer != null)
        {
            cooldownContainer.SetActive(false);
        }
        if (cooldownOverlay != null)
        {
            cooldownOverlay.fillAmount = 0;
        }
    }

    public void SetIcon(Sprite newIcon)
    {
        if (iconImage != null)
        {
            iconImage.sprite = newIcon;
            iconImage.enabled = true;
        }
        else
        {
            Debug.LogWarning("ArsenalIconController: Icon Image reference not set.", this);
        }
    }

    public void StartCooldown(float duration)
    {
        if (cooldownOverlay == null || cooldownContainer == null)
        {
            Debug.LogWarning("ArsenalIconController: Cooldown UI references not set. Cannot start cooldown.", this);
            return;
        }
        
        if (duration <= 0) return;

        Debug.Log($"ArsenalIconController: Starting cooldown with duration {duration}.", this);

        if (cooldownCoroutine != null)
        {
            StopCoroutine(cooldownCoroutine);
        }
        cooldownCoroutine = StartCoroutine(CooldownCoroutine(duration));
    }

    private IEnumerator CooldownCoroutine(float duration)
    {
        cooldownContainer.SetActive(true);
        cooldownOverlay.fillAmount = 1f;

        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            cooldownOverlay.fillAmount = 1f - (timer / duration);
            yield return null;
        }

        cooldownOverlay.fillAmount = 0f;
        cooldownContainer.SetActive(false);
        cooldownCoroutine = null;
    }
} 