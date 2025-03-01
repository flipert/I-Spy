using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("The target to follow")]
    public Transform target;
    
    [Tooltip("Offset from the target position")]
    public Vector3 offset = new Vector3(0f, 5f, -10f);
    
    [Header("Follow Settings")]
    [Tooltip("How quickly the camera follows the target")]
    [Range(0.01f, 1f)]
    public float smoothSpeed = 0.125f;
    
    [Tooltip("If true, camera will look at the target")]
    public bool lookAtTarget = true;
    
    [Tooltip("Follow style to use")]
    public FollowStyle followStyle = FollowStyle.SmoothFollow;
    
    [Header("Advanced Settings")]
    [Tooltip("Damping for position changes")]
    public float positionDamping = 1f;
    
    [Tooltip("Damping for rotation changes")]
    public float rotationDamping = 1f;
    
    [Tooltip("If true, camera will maintain a fixed height")]
    public bool fixedHeight = true;
    
    [Tooltip("Height to maintain if fixed height is enabled")]
    public float fixedHeightValue = 10f;
    
    [Tooltip("If true, camera will only follow on X and Z axes")]
    public bool topDownFollow = true;
    
    // Current velocity for SmoothDamp
    private Vector3 currentVelocity = Vector3.zero;
    
    // Enum for different follow styles
    public enum FollowStyle
    {
        Instant,           // Camera instantly moves to target
        SmoothFollow,      // Camera smoothly follows with Lerp
        SmoothDamp,        // Camera follows with SmoothDamp (more natural)
        FixedOffset        // Camera maintains exact offset from target
    }
    
    private void Start()
    {
        // If no target is set, try to find the player
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
                Debug.Log("CameraFollow: Automatically found player target");
            }
            else
            {
                Debug.LogWarning("CameraFollow: No target assigned and no Player tag found");
            }
        }
        
        // Initialize camera position
        if (target != null && followStyle == FollowStyle.Instant)
        {
            UpdateCameraPosition(1f);
        }
    }
    
    private void LateUpdate()
    {
        if (target == null)
            return;
        
        // Update camera position based on selected follow style
        switch (followStyle)
        {
            case FollowStyle.Instant:
                UpdateCameraPosition(1f);
                break;
                
            case FollowStyle.SmoothFollow:
                UpdateCameraPosition(smoothSpeed);
                break;
                
            case FollowStyle.SmoothDamp:
                Vector3 targetPosition = GetTargetPosition();
                transform.position = Vector3.SmoothDamp(
                    transform.position, 
                    targetPosition, 
                    ref currentVelocity, 
                    positionDamping);
                break;
                
            case FollowStyle.FixedOffset:
                transform.position = target.position + offset;
                break;
        }
        
        // Make the camera look at the target if enabled
        if (lookAtTarget)
        {
            if (rotationDamping > 0)
            {
                // Smooth rotation
                Quaternion targetRotation = Quaternion.LookRotation(target.position - transform.position);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, 
                    targetRotation, 
                    Time.deltaTime / rotationDamping);
            }
            else
            {
                // Instant rotation
                transform.LookAt(target);
            }
        }
    }
    
    private void UpdateCameraPosition(float speed)
    {
        Vector3 targetPosition = GetTargetPosition();
        transform.position = Vector3.Lerp(transform.position, targetPosition, speed * Time.deltaTime * 10f);
    }
    
    private Vector3 GetTargetPosition()
    {
        Vector3 targetPos = target.position + offset;
        
        // Maintain fixed height if enabled
        if (fixedHeight)
        {
            targetPos.y = fixedHeightValue;
        }
        
        // For top-down view, only follow on X and Z
        if (topDownFollow)
        {
            targetPos.y = transform.position.y;
        }
        
        return targetPos;
    }
    
    // Public method to change the target at runtime
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    // Public method to reset camera position instantly
    public void ResetCameraPosition()
    {
        if (target != null)
        {
            transform.position = GetTargetPosition();
            if (lookAtTarget)
            {
                transform.LookAt(target);
            }
        }
    }
    
    // Draw a visual representation of the camera follow in the editor
    private void OnDrawGizmosSelected()
    {
        if (target == null)
            return;
            
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, target.position);
        
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(target.position, 0.5f);
        
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(GetTargetPosition(), 0.5f);
    }
} 