using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public class AuthenticationManager : MonoBehaviour
{
    async void Awake()
    {
        try
        {
            await UnityServices.InitializeAsync();
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

            if (AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log($"Player signed in anonymously. PlayerID: {AuthenticationService.Instance.PlayerId}");
            }
            else
            {
                Debug.LogError("Player anonymous sign-in failed.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize or sign in: {e.Message}");
        }
    }
} 