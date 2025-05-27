using UnityEngine;
using System.Collections;

public class CameraShakeController : MonoBehaviour
{
    public static CameraShakeController Instance { get; private set; }

    public Vector3 CurrentShakeOffset { get; private set; } = Vector3.zero;

    private Coroutine activeShakeCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void TriggerShake(float duration, float magnitude)
    {
        if (activeShakeCoroutine != null)
        {
            StopCoroutine(activeShakeCoroutine);
        }
        activeShakeCoroutine = StartCoroutine(ShakeCoroutine(duration, magnitude));
    }

    private IEnumerator ShakeCoroutine(float duration, float magnitude)
    {
        float elapsed = 0f;
        float currentMagnitude = magnitude;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * currentMagnitude;
            float y = Random.Range(-1f, 1f) * currentMagnitude;
            // You can add z for depth shake if desired:
            // float z = Random.Range(-1f, 1f) * currentMagnitude;
            CurrentShakeOffset = new Vector3(x, y, 0f); // Shake on XY plane for typical 2.5D/top-down

            // Optionally, reduce magnitude over time
            currentMagnitude = Mathf.Lerp(magnitude, 0f, elapsed / duration);
            
            elapsed += Time.deltaTime;
            yield return null;
        }

        CurrentShakeOffset = Vector3.zero; // Reset offset when shake is done
        activeShakeCoroutine = null;
    }
} 