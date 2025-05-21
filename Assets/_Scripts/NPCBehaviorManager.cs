using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic; // For lists of NPCs if needed later
using System.Linq; // For LINQ operations like Count()

public class NPCBehaviorManager : NetworkBehaviour
{
    public enum NPCBehaviorState
    {
        Idle,
        Wandering,
        SeekingStaticGroup, // Looking for a spot or others to form a static group
        InStaticGroup,      // Standing in a circle with others
        SeekingWalkingGroup,// Looking for a spot or others to form a walking group
        InWalkingGroup,     // Walking with others
        FormingStaticGroup  // Initial state for an NPC starting a new static group, waiting for others
    }

    [Header("Behavior Timings")]
    [Tooltip("Minimum time NPC will stay in a non-group state before considering a change.")]
    public float minStateTime = 8f;
    [Tooltip("Maximum time NPC will stay in a non-group state before forcing a change.")]
    public float maxStateTime = 20f;
    [Tooltip("How long an NPC will actively seek or try to form a group before giving up.")]
    public float maxSeekingTime = 10f;

    [Header("Group Settings")]
    [Tooltip("Minimum number of NPCs in a group.")]
    public int minGroupSize = 3;
    [Tooltip("Maximum number of NPCs in a group.")]
    public int maxGroupSize = 6;
    [Tooltip("Radius NPCs will search for other NPCs to form or join groups.")]
    public float groupSearchRadius = 15f;
    [Tooltip("Distance NPCs will keep from each other in a static group circle.")]
    public float staticGroupFormationRadius = 2.5f; // Increased slightly for better spacing
    [Tooltip("Distance NPCs will keep from each other while walking in a group.")]
    public float walkingGroupSpacing = 1.5f;
    [Tooltip("Layers to consider as NPCs for group formation (should be the layer your NPCs are on).")]
    public LayerMask npcLayerMask; // Assign this in the Inspector!

    // Networked State
    private NetworkVariable<NPCBehaviorState> networkBehaviorState = new NetworkVariable<NPCBehaviorState>(NPCBehaviorState.Idle,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> networkCurrentGroupId = new NetworkVariable<ulong>(0, // 0 means no group
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Internal State & References
    private NPCController npcController;
    private float currentStateTimer;
    private float seekingTimer; // Timer for how long an NPC actively seeks/forms a group

    // Server-side Group Management
    public class NPCGroup
    {
        public ulong GroupId { get; private set; }
        public NPCBehaviorState GroupType { get; set; } // Changed private set; to set;
        public List<NPCBehaviorManager> Members { get; private set; }
        public Vector3 GroupCenter { get; set; } // For static groups, calculated dynamically or set by leader
        public bool IsAcceptingMembers => Members.Count < MaxSize;
        public int MaxSize { get; private set; }
        public int MinSize { get; private set; }
        public NetworkObject GroupLeader { get; set; } // Optional: could be the first member or a designated leader

        public NPCGroup(ulong id, NPCBehaviorState type, int minSize, int maxSize, NPCBehaviorManager initialMember)
        {
            GroupId = id;
            GroupType = type;
            Members = new List<NPCBehaviorManager> { initialMember };
            GroupCenter = initialMember.transform.position; // Initial center
            MinSize = minSize;
            MaxSize = maxSize;
            GroupLeader = initialMember.NetworkObject; // First member is initial leader
        }

        public void AddMember(NPCBehaviorManager member)
        {
            if (!Members.Contains(member) && IsAcceptingMembers)
            {
                Members.Add(member);
                RecalculateGroupCenter(); // Optional: recalculate center when members join
            }
        }

        public void RemoveMember(NPCBehaviorManager member)
        {
            Members.Remove(member);
            if (Members.Count > 0)
            {
                RecalculateGroupCenter();
                if (GroupLeader == member.NetworkObject) // If leader leaves, pick a new one
                {
                    GroupLeader = Members[0].NetworkObject;
                }
            }
        }
        
        public void RecalculateGroupCenter()
        {
            if (GroupType == NPCBehaviorState.InStaticGroup && Members.Count > 0)
            {
                Vector3 newCenter = Vector3.zero;
                foreach (var m in Members) newCenter += m.transform.position;
                GroupCenter = newCenter / Members.Count;
            }
            // For walking groups, center might be leader's position or dynamically updated
        }

        public Vector3 GetTargetPositionForMember(NPCBehaviorManager member, float formationRadius)
        {
            int memberIndex = Members.IndexOf(member);
            if (memberIndex == -1 || Members.Count == 0) return member.transform.position; // Should not happen if member is in list

            if (Members.Count == 1) return GroupCenter; // Single member stands at center

            float angle = (2 * Mathf.PI / Members.Count) * memberIndex;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * formationRadius;
            return GroupCenter + offset;
        }
    }

    private static List<NPCGroup> ServerActiveGroups = new List<NPCGroup>();
    private static ulong _nextGroupId = 1;
    private NPCGroup _server_currentGroupMembership = null; // Server-side reference to the group this NPC is in


    private static ulong GetNextGroupId()
    {
        return _nextGroupId++;
    }

    void Awake()
    {
        npcController = GetComponent<NPCController>();
        if (npcController == null)
        {
            Debug.LogError($"NPCBehaviorManager on {gameObject.name} requires an NPCController component.", this);
        }
        if (npcLayerMask.value == 0) Debug.LogWarning($"NPC Layer Mask not set on {gameObject.name}. Group formation may not work correctly.", this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
        {
            networkBehaviorState.OnValueChanged += OnBehaviorStateChangedClient;
            networkCurrentGroupId.OnValueChanged += OnGroupIdChangedClient;
            HandleStateChange(networkBehaviorState.Value, networkBehaviorState.Value, networkCurrentGroupId.Value);
        }
        else
        {
            // Server initializes the first state
            // For now, let's start with Idling or Wandering, similar to NPCController's initial logic
            // We'll refine this later with group logic
            SetRandomNonGroupState();
        }
    }

    void Update()
    {
        if (IsServer)
        {
            currentStateTimer -= Time.deltaTime;
            if (currentStateTimer <= 0f)
            {
                DecideNextBehavior();
            }
            ExecuteCurrentBehaviorServer();
        }
        else
        {
            // Client-side logic for current state (mostly visual, handled by NPCController or here if needed)
            ExecuteCurrentBehaviorClient();
        }
    }

    private void OnBehaviorStateChangedClient(NPCBehaviorState previousState, NPCBehaviorState newState)
    {
        HandleStateChange(previousState, newState, networkCurrentGroupId.Value);
    }
    private void OnGroupIdChangedClient(ulong previousGroupId, ulong newGroupId)
    {
        // If group ID changes, it might affect how the client perceives the NPC's state or actions
        // For now, primarily handled by state changes.
        // Debug.Log($"Client: NPC {gameObject.name} Group ID changed from {previousGroupId} to {newGroupId}");
    }

    private void HandleStateChange(NPCBehaviorState previousState, NPCBehaviorState newState, ulong currentGroupId)
    {
        // This method will be called on clients when the state changes.
        // It can trigger visual updates or local logic adjustments based on the new state.
        // For example, if NPCController needs to be explicitly told about high-level behavior changes.
        // Debug.Log($"NPC {gameObject.name} changed state from {previousState} to {newState}");

        // Example: If NPCController has methods to stop current actions based on high-level state
        if (npcController != null)
        {
            // If transitioning away from a group state, npcController might need to reset some targets
            // If entering a specific state, npcController might prepare for new types of commands
        }
    }

    private void SetRandomNonGroupState()
    {
        if (!IsServer) return;

        // Randomly choose between Idle and Wandering for now
        if (Random.value < 0.5f)
        {
            TransitionToState(NPCBehaviorState.Idle);
        }
        else
        {
            TransitionToState(NPCBehaviorState.Wandering);
        }
    }
    
    private void DecideNextBehavior()
    {
        if (!IsServer) return;

        // If in a group, specific logic applies (e.g., group disbands, member leaves)
        if (_server_currentGroupMembership != null)
        {
            // Check if group should dissolve or if this member should leave
            if (_server_currentGroupMembership.Members.Count < _server_currentGroupMembership.MinSize && 
                networkBehaviorState.Value == NPCBehaviorState.InStaticGroup) // Only auto-dissolve/leave from stable group state
            {
                // Debug.Log($"NPC {gameObject.name} leaving group {networkCurrentGroupId.Value} due to low member count.");
                Server_LeaveCurrentGroup();
                SetRandomNonGroupState(); // Find something else to do
                return;
            }
            // If timer for being in a group expires, leave and do something else
            if (networkBehaviorState.Value == NPCBehaviorState.InStaticGroup || networkBehaviorState.Value == NPCBehaviorState.InWalkingGroup)
            {
                 // Let group state execution handle timed leaving or state changes within group
            }
        }
        
        // Default behavior choices if not in a group or if group logic doesn't dictate a change yet
        float decision = Random.value;
        if (decision < 0.4f) // 40% chance to Idle
        {
            TransitionToState(NPCBehaviorState.Idle);
        }
        else if (decision < 0.8f) // 40% chance to Wander
        {
            TransitionToState(NPCBehaviorState.Wandering);
        }
        else // 20% chance to try and seek a static group
        {
            TransitionToState(NPCBehaviorState.SeekingStaticGroup);
        }
    }

    private void TransitionToState(NPCBehaviorState newState)
    {
        if (!IsServer) return;

        NPCBehaviorState oldState = networkBehaviorState.Value;
        if (oldState == newState && currentStateTimer > 0) return; // Avoid re-transition unless timer forces it

        // Debug.Log($"NPC {gameObject.name} SERVER Transitioning: {oldState} -> {newState} (Group: {networkCurrentGroupId.Value})");
        networkBehaviorState.Value = newState;
        
        // Reset appropriate timers
        if (newState == NPCBehaviorState.SeekingStaticGroup || newState == NPCBehaviorState.SeekingWalkingGroup || newState == NPCBehaviorState.FormingStaticGroup)
        {
            seekingTimer = maxSeekingTime; // Give dedicated time for seeking/forming
            currentStateTimer = maxSeekingTime + Random.Range(0, 2f); // Ensure seeking timer is primary
        }
        else
        {
            currentStateTimer = Random.Range(minStateTime, maxStateTime);
        }

        // Handle leaving a group if transitioning to a non-group or different group-seeking state
        if (_server_currentGroupMembership != null && 
            (newState == NPCBehaviorState.Idle || newState == NPCBehaviorState.Wandering || 
             (newState == NPCBehaviorState.SeekingStaticGroup && _server_currentGroupMembership.GroupType != NPCBehaviorState.InStaticGroup) ||
             (newState == NPCBehaviorState.SeekingWalkingGroup && _server_currentGroupMembership.GroupType != NPCBehaviorState.InWalkingGroup) ))
        {
            Server_LeaveCurrentGroup();
        }

        switch (newState)
        {
            case NPCBehaviorState.Idle:
                npcController?.Server_ForceIdle();
                break;
            case NPCBehaviorState.Wandering:
                npcController?.Server_StartWandering();
                break;
            case NPCBehaviorState.SeekingStaticGroup:
                // Action taken in ExecuteCurrentBehaviorServer to attempt joining/forming
                npcController?.Server_ForceIdle(); // Be idle while seeking initially
                break;
            case NPCBehaviorState.FormingStaticGroup:
                // This state is entered by Server_TryFindOrCreateStaticGroup if a new group is made.
                // NPCController will be idle, waiting for others or timeout.
                npcController?.Server_ForceIdle();
                break;
            case NPCBehaviorState.InStaticGroup:
                // Position will be set by ExecuteCurrentBehaviorServer
                // If just joined, might need an immediate position update.
                if (_server_currentGroupMembership != null && npcController != null)
                {
                    Vector3 targetPos = _server_currentGroupMembership.GetTargetPositionForMember(this, staticGroupFormationRadius);
                    npcController.Server_MoveToTarget(targetPos);
                } else if (npcController != null) {
                    npcController.Server_ForceIdle(); // Should have a group, if not, idle.
                }
                break;
            case NPCBehaviorState.InWalkingGroup:
                // Logic to behave as part of a walking group (e.g., follow leader, maintain formation)
                // Debug.Log($"NPC {gameObject.name} is now InWalkingGroup. (Not Implemented)");
                // npcController.Server_FollowTarget(groupLeaderTransform, offset);
                break;
            default:
                npcController?.Server_ForceIdle();
                break;
        }
    }

    private void ExecuteCurrentBehaviorServer()
    {
        if (!IsServer) return;

        seekingTimer -= Time.deltaTime; // Decrement seeking timer regardless of state, for when it's used

        switch (networkBehaviorState.Value)
        {
            case NPCBehaviorState.Idle:
            case NPCBehaviorState.Wandering:
                // NPCController handles its actions. BehaviorManager just waits for timer.
                break;

            case NPCBehaviorState.SeekingStaticGroup:
                if (seekingTimer <= 0f)
                {
                    // Debug.Log($"NPC {gameObject.name} timed out seeking static group.");
                    SetRandomNonGroupState(); // Give up and do something else
                    return;
                }
                Server_TryFindOrCreateStaticGroup(); // Attempt to find/form a group
                // This method might change the state to InStaticGroup or FormingStaticGroup
                break;

            case NPCBehaviorState.FormingStaticGroup:
                if (seekingTimer <= 0f && _server_currentGroupMembership != null && _server_currentGroupMembership.Members.Count < minGroupSize)
                {
                    // Debug.Log($"NPC {gameObject.name} (leader) timed out forming group {networkCurrentGroupId.Value}, not enough members. Disbanding.");
                    Server_DisbandCurrentGroup(); // Leader disbands the group
                    SetRandomNonGroupState();
                    return;
                }
                if (_server_currentGroupMembership != null && _server_currentGroupMembership.Members.Count >= minGroupSize)
                {
                    // Debug.Log($"NPC {gameObject.name} (leader) formed group {networkCurrentGroupId.Value} successfully! Transitioning to InStaticGroup.");
                    // The group is viable, transition all members. The leader initiates this.
                    _server_currentGroupMembership.GroupType = NPCBehaviorState.InStaticGroup; // Solidify group type
                    foreach (var member in _server_currentGroupMembership.Members.ToList()) // ToList to avoid modification issues
                    {
                        member.TransitionToState(NPCBehaviorState.InStaticGroup);
                    }
                }
                // Else, just wait, NPC is idle.
                break;

            case NPCBehaviorState.InStaticGroup:
                if (_server_currentGroupMembership == null) // Should not happen if state is correct
                {
                    // Debug.LogWarning($"NPC {gameObject.name} in InStaticGroup but has no group! Returning to Idle.");
                    SetRandomNonGroupState();
                    return;
                }
                // Periodically update position in case group center or member count changed
                // Or if NPC drifted.
                Vector3 targetPos = _server_currentGroupMembership.GetTargetPositionForMember(this, staticGroupFormationRadius);
                 // Only command move if not already very close to target to avoid jitter
                if (Vector3.Distance(transform.position, targetPos) > 0.2f)
                {
                    if (!npcController.IsPathfindingComplete() && networkCurrentGroupId.Value == _server_currentGroupMembership.GroupId ) {
                        // If already moving towards a point for this group, let it continue unless target changed significantly
                        // This simple check might need refinement if targetPos changes frequently due to GroupCenter recalculation
                    } else {
                         npcController.Server_MoveToTarget(targetPos);
                    }
                } else {
                    // Close enough, ensure idle if was moving
                    if(!npcController.IsPathfindingComplete()) npcController.Server_ForceIdle();
                }

                // Check if needs to leave (e.g. if group size drops, handled by DecideNextBehavior or here)
                if (currentStateTimer <= 0f) // Time to leave the group
                {
                    // Debug.Log($"NPC {gameObject.name} timer for InStaticGroup {networkCurrentGroupId.Value} expired. Leaving.");
                    Server_LeaveCurrentGroup();
                    SetRandomNonGroupState();
                }
                break;
        }
    }

    private void Server_TryFindOrCreateStaticGroup()
    {
        if (!IsServer || _server_currentGroupMembership != null) return; // Already in a group or not server

        // 1. Try to join an existing, accepting static group
        foreach (var group in ServerActiveGroups.ToList()) // ToList if modifying ServerActiveGroups inside
        {
            if (group.GroupType == NPCBehaviorState.InStaticGroup || group.GroupType == NPCBehaviorState.FormingStaticGroup)
            {
                if (group.IsAcceptingMembers && Vector3.Distance(transform.position, group.GroupCenter) < groupSearchRadius)
                {
                    // Check if leader/group is still valid
                    if (group.GroupLeader == null || !group.GroupLeader.IsSpawned) {
                        // Stale group, potentially remove it (or let group logic handle it)
                        // ServerActiveGroups.Remove(group); // Risky if multiple NPCs do this
                        continue;
                    }

                    // Join this group
                    // Debug.Log($"NPC {gameObject.name} found and joining static group {group.GroupId}.");
                    Server_JoinGroup(group);
                    TransitionToState(NPCBehaviorState.InStaticGroup); // Directly transition to InStaticGroup
                    return;
                }
            }
        }

        // 2. If no group to join, try to form a new one with other lone NPCs
        // This NPC will be the initial leader of the new group.
        List<NPCBehaviorManager> potentialMembers = new List<NPCBehaviorManager>();
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, groupSearchRadius, npcLayerMask);
        foreach (var hitCollider in hitColliders)
        {
            NPCBehaviorManager otherNpc = hitCollider.GetComponent<NPCBehaviorManager>();
            if (otherNpc != null && otherNpc != this && otherNpc._server_currentGroupMembership == null &&
                (otherNpc.networkBehaviorState.Value == NPCBehaviorState.Idle || otherNpc.networkBehaviorState.Value == NPCBehaviorState.Wandering))
            {
                potentialMembers.Add(otherNpc);
                if (potentialMembers.Count >= minGroupSize -1) break; // Found enough to potentially start (this + others)
            }
        }

        if (potentialMembers.Count + 1 >= minGroupSize) // This NPC + found members
        {
            // Create a new group
            NPCGroup newGroup = new NPCGroup(GetNextGroupId(), NPCBehaviorState.FormingStaticGroup, minGroupSize, maxGroupSize, this);
            ServerActiveGroups.Add(newGroup);
            _server_currentGroupMembership = newGroup;
            networkCurrentGroupId.Value = newGroup.GroupId;
            // Debug.Log($"NPC {gameObject.name} forming new static group {newGroup.GroupId} as leader.");
            TransitionToState(NPCBehaviorState.FormingStaticGroup); // Leader enters forming state

            // // Invite other found members (they will attempt to join if they are also seeking)
            // // For now, a simpler model: they don't auto-join yet. The group is formed, and others can find it.
            // // Or, if we want them to auto-join:
            // foreach(var memberToInvite in potentialMembers.Take(maxGroupSize -1)) // Take up to max size
            // {
            //    // This part is tricky. Direct state change for others can be complex.
            //    // Better: They should also be in SeekingStaticGroup and find this newly formed group.
            //    // For now, the group exists. If other NPCs are in SeekingStaticGroup, they will find it in their next Server_TryFindOrCreateStaticGroup call.
            // }
            return;
        }
        // If cannot join or form, SeekingStaticGroup continues until its timer runs out.
    }

    private void Server_JoinGroup(NPCGroup group)
    {
        if (!IsServer || group == null) return;
        group.AddMember(this);
        _server_currentGroupMembership = group;
        networkCurrentGroupId.Value = group.GroupId;
        // Debug.Log($"NPC {gameObject.name} successfully joined group {group.GroupId}. Members: {group.Members.Count}");
    }

    private void Server_LeaveCurrentGroup()
    {
        if (!IsServer || _server_currentGroupMembership == null) return;
        // Debug.Log($"NPC {gameObject.name} leaving group {networkCurrentGroupId.Value}.");
        _server_currentGroupMembership.RemoveMember(this);
        if (_server_currentGroupMembership.Members.Count == 0) // If last member leaves, disband group
        {
            // Debug.Log($"Group {_server_currentGroupMembership.GroupId} is now empty and disbanding.");
            ServerActiveGroups.Remove(_server_currentGroupMembership);
        }
        _server_currentGroupMembership = null;
        
        // Only try to update NetworkVariable if the object is still fully spawned and not in the process of despawning.
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            networkCurrentGroupId.Value = 0;
        }
    }
    
    private void Server_DisbandCurrentGroup() // Typically called by leader if forming fails
    {
        if (!IsServer || _server_currentGroupMembership == null) return;
        ulong groupId = _server_currentGroupMembership.GroupId;
        // Debug.Log($"NPC {gameObject.name} (leader) disbanding group {groupId}.");

        // Notify other members they are no longer in this group
        foreach (var member in _server_currentGroupMembership.Members.ToList()) // ToList because RemoveMember modifies list
        {
            if (member != this) // Leader already knows
            {
                member._server_currentGroupMembership = null;
                if (member.NetworkObject != null && member.NetworkObject.IsSpawned)
                {
                    member.networkCurrentGroupId.Value = 0;
                }
                member.SetRandomNonGroupState(); // Make them do something else
            }
        }
        ServerActiveGroups.Remove(_server_currentGroupMembership);
        _server_currentGroupMembership = null;
        // Leader also sets its own group ID to 0, if still spawned.
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            networkCurrentGroupId.Value = 0;
        }
    }

    private void ExecuteCurrentBehaviorClient()
    {
        // Client-side execution primarily involves reacting to state changes
        // (handled by OnBehaviorStateChangedClient and HandleStateChange)
        // and allowing NPCController to manage animations and local visual interpolation.
        // Specific client-side logic for group behaviors could go here if needed,
        // e.g., special animations or effects when in a group.
        
        // Example:
        // switch (networkBehaviorState.Value)
        // {
        //    case NPCBehaviorState.InStaticGroup:
        //        // Play a specific "socializing" animation if not handled by NPCController's Running bool
        //        break;
        // }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer && _server_currentGroupMembership != null)
        {
            Server_LeaveCurrentGroup(); // Ensure NPC is removed from group on despawn
        }
        if (!IsServer)
        {
            networkBehaviorState.OnValueChanged -= OnBehaviorStateChangedClient;
            networkCurrentGroupId.OnValueChanged -= OnGroupIdChangedClient;
        }
        // If this NPC was part of a group, it should notify the group on despawn (server-side)
        // RemoveFromServerGroupList();
    }
} 