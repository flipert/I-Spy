using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
/// <summary>
/// Debug tool to check Animator Controller transition settings that might cause animation delays.
/// Attach this to your player GameObject and it will analyze transitions in the editor.
/// </summary>
public class AnimatorTransitionDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    [SerializeField] private bool analyzeOnStart = true;
    [SerializeField] private Animator targetAnimator;
    
    void Start()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>();
            
        if (analyzeOnStart && targetAnimator != null)
        {
            AnalyzeAnimatorTransitions();
        }
    }
    
    [ContextMenu("Analyze Animator Transitions")]
    public void AnalyzeAnimatorTransitions()
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null)
        {
            Debug.LogError("AnimatorTransitionDebugger: No animator or controller found!");
            return;
        }
        
        Debug.Log("=== ANIMATOR TRANSITION ANALYSIS ===");
        Debug.Log($"Analyzing: {targetAnimator.runtimeAnimatorController.name}");
        
        // Get the animator controller
        var controller = targetAnimator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        if (controller == null)
        {
            Debug.LogWarning("Could not cast to AnimatorController. Make sure you're using an AnimatorController, not an AnimatorOverrideController.");
            return;
        }
        
        // Analyze all layers
        for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
        {
            var layer = controller.layers[layerIndex];
            Debug.Log($"\nLayer {layerIndex}: {layer.name}");
            
            var stateMachine = layer.stateMachine;
            
            // Check all states
            foreach (var state in stateMachine.states)
            {
                Debug.Log($"\n  State: {state.state.name}");
                
                // Check all transitions from this state
                foreach (var transition in state.state.transitions)
                {
                    string destName = transition.destinationState != null ? transition.destinationState.name : "Exit";
                    Debug.Log($"    → Transition to: {destName}");
                    
                    // Check for potential delay-causing settings
                    if (transition.hasExitTime)
                    {
                        Debug.LogWarning($"      ⚠ Has Exit Time: {transition.exitTime} (This causes delays!)");
                    }
                    
                    if (transition.duration > 0.1f)
                    {
                        Debug.LogWarning($"      ⚠ Long Duration: {transition.duration}s (Consider reducing for snappier transitions)");
                    }
                    
                    if (transition.offset > 0)
                    {
                        Debug.LogWarning($"      ⚠ Has Offset: {transition.offset} (This delays the start of the animation)");
                    }
                    
                    // Check conditions
                    if (transition.conditions.Length > 0)
                    {
                        Debug.Log($"      Conditions:");
                        foreach (var condition in transition.conditions)
                        {
                            Debug.Log($"        - {condition.parameter} {condition.mode} {condition.threshold}");
                        }
                    }
                }
            }
            
            // Check Any State transitions
            Debug.Log($"\n  Any State Transitions:");
            foreach (var transition in stateMachine.anyStateTransitions)
            {
                string destName = transition.destinationState != null ? transition.destinationState.name : "Unknown";
                Debug.Log($"    → Any State to: {destName}");
                
                if (transition.hasExitTime)
                {
                    Debug.LogWarning($"      ⚠ Has Exit Time: {transition.exitTime}");
                }
                
                if (transition.duration > 0.1f)
                {
                    Debug.LogWarning($"      ⚠ Long Duration: {transition.duration}s");
                }
            }
        }
        
        Debug.Log("\n=== RECOMMENDATIONS ===");
        Debug.Log("For immediate animation response:");
        Debug.Log("1. Disable 'Has Exit Time' on transitions triggered by user input");
        Debug.Log("2. Set 'Transition Duration' to 0 or very low (0.1s max)");
        Debug.Log("3. Set 'Transition Offset' to 0");
        Debug.Log("4. Use 'Any State' transitions for actions that can interrupt other animations");
        Debug.Log("===============================");
    }
}
#endif 