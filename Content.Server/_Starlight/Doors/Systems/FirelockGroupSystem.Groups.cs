using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared._Starlight.Doors.Systems;
using Content.Shared.Doors.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockGroupSystem : SharedFirelockGroupSystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent]
    private void OnFirelockInit(Entity<FirelockComponent> firelock, ref ComponentInit ev)
    {
        if (!IsGroupingEligible(firelock)) return;
        if (HasComp<FirelockGroupMemberComponent>(firelock)) return;
        Log.Debug($"OnFirelockInit {firelock.Owner}");

        JoinOrCreateGroupPassively(firelock);
    }

    [SubscribeLocalEvent]
    private void OnMemberAnchorStateChanged(Entity<FirelockGroupMemberComponent> member, ref AnchorStateChangedEvent ev)
    {
        if (!TryComp<FirelockComponent>(member, out var firelock)) return;
        if (!TryComp<FirelockGroupComponent>(member.Comp.Holder, out var groupComp)) return;
        Log.Debug($"OnMemberAnchorStateChanged {member.Owner}");

        // It *should* be impossible for an unanchored entity to be a group member, but this also happens on map init,
        // so we'll just try to handle it nicely.
        if (ev.Anchored)
        {
            JoinOrCreateGroupPassively((member, firelock));
            return;
        }

        // If the entity was previously anchored, remove it from the group.
        LeaveGroup(member);
        ValidateOrSplitGroup((member.Comp.Holder, groupComp)); // Splits into islands if necessary
    }

    #region Primary logic

    /// <summary>
    /// Scans immediate neighbors for groups to join. If none are found, creates one and joins with the neighbors.
    /// If multiple neighbors have different groups, merges them and joins the group.
    /// </summary>
    private void JoinOrCreateGroupPassively(Entity<FirelockComponent> firelock)
    {
        var adjacent = FindAdjacentFirelocks(firelock, Transform(firelock));

        var neighborGroups = new HashSet<Entity<FirelockGroupComponent>>();
        var unaffiliatedFirelocks = new HashSet<Entity<FirelockComponent>>();

        foreach (var neighbor in adjacent)
            if (TryComp<FirelockGroupMemberComponent>(neighbor, out var member) &&
                TryComp<FirelockGroupComponent>(member.Holder, out var neighborGroup))
                neighborGroups.Add((member.Holder, neighborGroup));
            else
                unaffiliatedFirelocks.Add(neighbor);

        // Merge existing groups, or make a new one if none were found.
        var group = neighborGroups.Count == 0 ? CreateGroup(firelock) : MergeGroups(neighborGroups);

        // Join group ourselves.
        // If we already had a group membership, it's overridden.
        JoinGroup(group, firelock);

        // Add all neighbors to our group.
        foreach (var neighbor in unaffiliatedFirelocks)
            JoinGroup(group, neighbor);

        RebalanceGroup(group);
    }

    #endregion

    #region High-level API

    /// <summary>
    /// Given a single firelock, creates a new group with itself as its sole member.
    /// </summary>
    private Entity<FirelockGroupComponent> CreateGroup(Entity<FirelockComponent> firelock)
    {
        var group = EnsureComp<FirelockGroupComponent>(firelock);
        var membership = EnsureComp<FirelockGroupMemberComponent>(firelock);
        group.Members = new() { firelock };
        membership.Holder = firelock.Owner;
        Dirty(firelock, group);
        Dirty(firelock, membership);
        Log.Debug($"Created firelock group: {firelock}");
        return (firelock, group);
    }

    private void DestroyGroup(Entity<FirelockGroupComponent> group)
    {
        if (group.Comp.Members.Count > 1)
        {
            Log.Error($"Firelock group {group.Owner} cannot be destroyed as it has multiple members.", new StackTrace());
            return;
        }

        if (group.Comp.Members.Count == 1 && !group.Comp.Members.Contains(group.Owner))
        {
            Log.Error($"Firelock group {group.Owner} has one member, but it's not the owner?", new StackTrace());
            return;
        }

        Log.Debug($"Destroying firelock group {group.Owner}");
        RemCompDeferred<FirelockGroupComponent>(group);
        RemCompDeferred<FirelockGroupMemberComponent>(group);
        RaiseGroupDestroyedEvent(group);
    }

    /// <summary>
    /// Joins a member to a group.
    /// </summary>
    private Entity<FirelockGroupMemberComponent> JoinGroup(Entity<FirelockGroupComponent> group,
        Entity<FirelockComponent> firelock)
    {
        var membership = JoinGroupInternal(firelock, group);
        Log.Debug($"Firelock {firelock} has joined group {group}");
        return (firelock, membership);
    }

    /// <summary>
    /// Makes the member leave whatever group it belongs to. May trigger a rebalance if it was the group owner.
    /// </summary>
    /// <param name="member">The member to remove from their group</param>
    private void LeaveGroup(Entity<FirelockGroupMemberComponent> member, bool rebalance = true)
    {
        var groupHolder = member.Comp.Holder;
        Log.Debug($"Firelock {member} is leaving group: {groupHolder}");
        LeaveGroupInternal(member, out var groupEnt, removeMemberComp: true, logErrorOnGroupMissing: true);

        // If the group was not found, there's nothing more we can do.
        if (!groupEnt.HasValue)
            return;

        // If this entity was the only member, the group ceases to exist.
        if (groupEnt.Value.Comp.Members.Count == 0)
        {
            Log.Debug($"Firelock group {groupHolder} is now empty, deleting");

            RemComp<FirelockGroupComponent>(groupHolder);
            Dirty(groupEnt.Value);
            return;
        }

        // We're done if this entity was not the group owner.
        if (groupHolder != member.Owner)
            return;

        // Otherwise, rebalance the group. This must be done after we removed the previous owner from the membership
        // list, but can still reference the old entity.
        if (rebalance)
            RebalanceGroup(groupEnt.Value);
    }

    private Entity<FirelockGroupComponent> MergeGroups(HashSet<Entity<FirelockGroupComponent>> groups)
    {
        if (groups.Count == 0)
            throw new ArgumentException($"{nameof(groups)} must contain at least one group");

        // The first group will be what we merge all members into.
        var resultGroup = groups.First();
        if (groups.Count == 1)
            return resultGroup;

        // Collect all members.
        var collectiveMembers = new List<Entity<TransformComponent, FirelockComponent>>();

        foreach (var group in groups)
        {
            foreach (var groupMember in group.Comp.Members)
            {
                if (!TryComp(groupMember, out FirelockComponent? firelock)) continue;
                collectiveMembers.Add((groupMember, Transform(groupMember), firelock));
            }
        }

        // Update the result group's member list.
        resultGroup.Comp.Members = collectiveMembers.Select(member => member.Owner).ToHashSet();

        // Update all members to be part of that group.
        foreach (var collectiveMember in collectiveMembers)
        {
            var membership = EnsureComp<FirelockGroupMemberComponent>(collectiveMember);
            if (membership.Holder == resultGroup.Owner)
                continue;

            // Only update and dirty if changed.
            membership.Holder = resultGroup.Owner;
            Dirty(collectiveMember, membership);
        }

        return resultGroup;
    }

    /// <summary>
    /// Checks if all firelocks in a group are still connected using a floodfill algorithm. If multiple islands are
    /// detected, they are split off into their own groups.
    /// </summary>
    /// <param name="group">The group to validate and possibly split up</param>
    private void ValidateOrSplitGroup(Entity<FirelockGroupComponent> group)
    {
        var remaining = new HashSet<EntityUid>(group.Comp.Members);
        var firelockIslands = new HashSet<HashSet<Entity<FirelockComponent>>>();

        while (remaining.Count > 0)
        {
            var memberId = remaining.First();
            remaining.Remove(memberId);

            if (!TryComp<FirelockComponent>(memberId, out var firelockComp))
                continue;

            var island = FindIslandFirelocks((memberId, firelockComp));
            foreach (var islandEnt in island)
                remaining.Remove(islandEnt.Owner);
            firelockIslands.Add(island);
        }

        // If all remaining firelocks still form one consecutive group we won't have to split.
        if (firelockIslands.Count <= 1)
            return;

        // All islands force-create new groups.
        foreach (var firelockIsland in firelockIslands)
        {
            var memberIsland = ToMemberEntities(firelockIsland);
            var islandGroup = SplitGroup(memberIsland);
            RebalanceGroup(islandGroup, raiseEvents: false);
        }
    }

    /// <summary>
    /// Given an island, appoint a new group leader, assign all members to this group, and return the new group.
    /// Note: This does NOT raise any events about a new group being formed.
    /// </summary>
    private Entity<FirelockGroupComponent> SplitGroup(HashSet<Entity<FirelockGroupMemberComponent>> island)
    {
        // Whichever entity we find first becomes the leader. (Rebalancing happens later).
        var newLeader = island.First();
        LeaveGroupInternal(newLeader, out var formerGroupEnt, removeMemberComp: false);
        RaiseMemberLeaveEvent(newLeader, formerGroupEnt);

        // Create the new group, announce it.
        var newGroupComp = EnsureComp<FirelockGroupComponent>(newLeader);
        var newGroupEnt = (newLeader, newGroupComp);
        RaiseGroupCreatedEvent(newGroupEnt);

        // Join each member to the new island, raise event for each join.
        foreach (var islandMemberEnt in island)
        {
            RaiseMemberLeaveEvent(islandMemberEnt, formerGroupEnt);
            JoinGroupInternal(islandMemberEnt, newGroupEnt);
            RaiseMemberJoinEvent(islandMemberEnt, newGroupEnt);
        }

        foreach (var firelock in island)
        {
            if (TryComp<FirelockGroupMemberComponent>(firelock, out var member))
            {
                // Raise a leave event for each member.
                LeaveGroup((firelock, member));
            }

            member ??= EnsureComp<FirelockGroupMemberComponent>(firelock);
            member.Holder = newLeader;
        }

        return (newLeader, newGroupComp);
    }

    /// <summary>
    /// Triggers a group rebalance. This means a new group leader entity will be elected, given the <see cref="FirelockGroupComponent"/>,
    /// and all other members will have their membership component updated to point to the new owner.
    /// Note that this method does *not* require the given entity to actually be a member, meaning it's compatible
    /// with rebalancing after a member leaves, provided the group membership list has had the previous owner removed.
    /// </summary>
    /// <param name="groupEnt">The current or former group owner</param>
    /// <param name="raiseEvents">Whether to raise the leader-changed event.</param>
    private Entity<FirelockGroupComponent> RebalanceGroup(Entity<FirelockGroupComponent> groupEnt,
        bool raiseEvents = true)
    {
        var members = new List<Entity<TransformComponent, FirelockGroupMemberComponent>>();
        var positionSum = Vector2.Zero;
        Log.Debug($"Rebalancing firelock group ${ToPrettyString(groupEnt)}");

        foreach (var groupMember in groupEnt.Comp.Members)
        {
            if (!TryComp(groupMember, out FirelockGroupMemberComponent? firelockGroupMember)) continue;
            var xform = Transform(groupMember);
            positionSum += _transform.GetWorldPosition(xform);
            members.Add((groupMember, xform, firelockGroupMember));
        }

        if (members.Count < 2)
            return groupEnt;

        var center = positionSum / members.Count;
        var closestEntity = members[0];
        var closestDistanceSq = float.MaxValue;

        foreach (var collectiveMember in members)
        {
            var distanceSq = Vector2.DistanceSquared(_transform.GetWorldPosition(collectiveMember.Comp1), center);
            if (distanceSq >= closestDistanceSq)
                continue;

            closestDistanceSq = distanceSq;
            closestEntity = collectiveMember;
        }

        // If the previous owner is still the closest, no need to move anything.
        if (closestEntity.Owner == groupEnt.Owner)
        {
            Log.Debug($"Best firelock group leader candidate is current owner, no action taken");
            return groupEnt;
        }

        // Create the new group.
        var newGroup = EnsureComp<FirelockGroupComponent>(closestEntity);
        Entity<FirelockGroupComponent> newGroupEnt = (closestEntity, newGroup);
        newGroup.Members = members.Select(member => member.Owner).ToHashSet();
        Dirty(closestEntity, newGroup);

        // Update all members to reference the new group leader.
        foreach (var member in members)
        {
            member.Comp2.Holder = closestEntity;
            DirtyField(member, member.Comp2, nameof(FirelockGroupMemberComponent.Holder));
        }

        // Raise the event.
        if (raiseEvents)
        {
            var leaderChangeEv = new FirelockGroupLeaderChangedEvent(groupEnt);
            RaiseLocalEvent(newGroupEnt, ref leaderChangeEv);
        }

        // Remove the group comp from the old group holder.
        RemComp<FirelockGroupComponent>(groupEnt);
        Dirty(groupEnt);
        Log.Debug($"New group leader chosen: was {groupEnt}, is now {closestEntity}");
        return (closestEntity, newGroup);
    }

    #endregion
    #region Internal API

    private Entity<FirelockGroupMemberComponent> JoinGroupInternal(
        Entity<FirelockComponent> firelock,
        Entity<FirelockGroupComponent> group)
    {
        var membership = EnsureComp<FirelockGroupMemberComponent>(firelock);
        return JoinGroupInternal((firelock, membership), group);
    }

    private Entity<FirelockGroupMemberComponent> JoinGroupInternal(
        Entity<FirelockGroupMemberComponent> membership,
        Entity<FirelockGroupComponent> group)
    {
        membership.Comp.Holder = group.Owner;
        Dirty(membership);

        group.Comp.Members.Add(membership);
        DirtyField(group, group.Comp, nameof(FirelockGroupComponent.Members));
        return membership;
    }

    /// <summary>
    /// Removes a member from the member list, removes the membership comp. If they were the group leader and members
    /// remain, the first found member is assigned group leader.
    /// </summary>
    /// <param name="memberEnt">The member to make leave their group</param>
    /// <param name="groupEnt">The resolved group, if any</param>
    /// <param name="removeMemberComp">Whether to remove the membership component after leaving</param>
    /// <param name="logErrorOnGroupMissing">Whether to log an error when the group owner cannot be found</param>
    private void LeaveGroupInternal(
        Entity<FirelockGroupMemberComponent> memberEnt,
        out Entity<FirelockGroupComponent>? groupEnt,
        bool removeMemberComp = true,
        bool logErrorOnGroupMissing = true)
    {
        // Remove this member from the groups' member list if possible.
        if (LifeStage(memberEnt.Comp.Holder) < EntityLifeStage.Terminating &&
            TryComp<FirelockGroupComponent>(memberEnt.Comp.Holder, out var groupComp))
        {
            groupEnt = (memberEnt.Comp.Holder, groupComp);
            groupEnt.Value.Comp.Members.Remove(memberEnt.Owner);
            DirtyField(groupEnt.Value, groupEnt.Value.Comp, nameof(FirelockGroupComponent.Members));
        }
        else if (logErrorOnGroupMissing)
            Log.Error(
                $"Tried to remove {memberEnt} from group {memberEnt.Comp.Holder} but group owner could not be found!");

        groupEnt = null;
        if (!removeMemberComp)
            return;

        // Remove membership comp.
        RemComp<FirelockGroupMemberComponent>(memberEnt);
        Dirty(memberEnt);
    }

    /// <summary>
    /// Tests whether an individual firelock is eligible for grouping.
    /// </summary>
    private bool IsGroupingEligible(Entity<FirelockComponent> firelock, TransformComponent? xform = null) =>
        // Must be anchored to be relevant.
        Resolve(firelock, ref xform) && xform.Anchored;

    /// <summary>
    /// Tests whether an individual firelock is eligible for grouping.
    /// </summary>
    private bool IsGroupMember(Entity<FirelockComponent> firelock)
    {
        if (!TryComp<FirelockGroupMemberComponent>(firelock, out var membership))
            return false;

        var holderLife = LifeStage(membership.Holder);
        return holderLife < EntityLifeStage.Terminating;
    }

    #endregion

    #region Event helpers

    private void RaiseGroupCreatedEvent(Entity<FirelockGroupComponent> groupEnt)
    {
        var ev = new FirelockGroupCreatedEvent();
        RaiseLocalEvent(groupEnt, ref ev);
    }

    private void RaiseGroupDestroyedEvent(Entity<FirelockGroupComponent> group)
    {
        var ev = new FirelockGroupDestroyedEvent();
        RaiseLocalEvent(group, ref ev);
    }

    private void RaiseMemberJoinEvent(
        Entity<FirelockGroupMemberComponent> memberEnt,
        Entity<FirelockGroupComponent> groupEnt)
    {
        var ev = new FirelockGroupMemberJoinEvent(groupEnt);
        RaiseLocalEvent(memberEnt, ref ev);
    }

    private void RaiseMemberLeaveEvent(
        Entity<FirelockGroupMemberComponent> memberEnt,
        Entity<FirelockGroupComponent>? groupEnt)
    {
        var ev = new FirelockGroupMemberLeaveEvent(groupEnt);
        RaiseLocalEvent(memberEnt, ref ev);
    }

    #endregion

    #region Helpers

    private HashSet<Entity<FirelockGroupMemberComponent>> ToMemberEntities(HashSet<Entity<FirelockComponent>> entities)
    {
        var result = new HashSet<Entity<FirelockGroupMemberComponent>>();
        foreach (var firelockEnt in entities)
            if (TryComp<FirelockGroupMemberComponent>(firelockEnt, out var memberComp))
                result.Add((firelockEnt, memberComp));
        return result;
    }

    #endregion

    #region Map related helpers

    private HashSet<Entity<FirelockComponent>> FindIslandFirelocks(Entity<FirelockComponent> firelock)
    {
        // Flood-fill across directly-connected firelocks to discover the whole group starting from this one.
        var members = new HashSet<Entity<FirelockComponent>>();
        var pending = new Queue<Entity<FirelockComponent>>();

        members.Add(firelock);
        pending.Enqueue(firelock);

        while (pending.TryDequeue(out var current))
        {
            foreach (var neighbor in FindAdjacentFirelocks(current, Transform(current)))
            {
                if (members.Add(neighbor))
                    pending.Enqueue(neighbor);
            }
        }

        return members;
    }

    /// <summary>
    /// Returns the firelocks anchored on the four cardinal tiles directly adjacent to
    /// <paramref name="firelock"/>.
    /// </summary>
    private List<Entity<FirelockComponent>> FindAdjacentFirelocks(Entity<FirelockComponent> firelock,
        TransformComponent xform)
    {
        var result = new List<Entity<FirelockComponent>>();

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return result;

        foreach (var uid in _map.GetCardinalNeighborCells(gridUid, grid, xform.Coordinates))
        {
            if (uid == firelock.Owner)
                continue;

            if (TryComp<FirelockComponent>(uid, out var comp))
                result.Add((uid, comp));
        }

        return result;
    }

    #endregion
}
