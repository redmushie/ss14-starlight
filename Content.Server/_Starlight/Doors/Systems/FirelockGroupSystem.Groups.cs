using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared._Starlight.Doors.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Examine;
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

        // It *should* be impossible for an unanchored entity to be a group member.
        // We try to make it gracefully leave the group if possible.
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
    #region Interal API

    /// <summary>
    /// Given a single firelock, creates a new group with itself as its sole member.
    /// </summary>
    private Entity<FirelockGroupComponent> CreateGroup(Entity<FirelockComponent> firelock)
    {
        var group = EnsureComp<FirelockGroupComponent>(firelock);
        var membership = EnsureComp<FirelockGroupMemberComponent>(firelock);
        group.Members =  new() { firelock };
        membership.Holder = firelock.Owner;
        Dirty(firelock, group);
        Dirty(firelock, membership);
        Log.Debug($"Created firelock group: {firelock}");
        return (firelock, group);
    }

    /// <summary>
    /// Joins a member to a group. Note that the member cannot already be part of a group!
    /// </summary>
    private Entity<FirelockGroupMemberComponent> JoinGroup(Entity<FirelockGroupComponent> group, Entity<FirelockComponent> firelock)
    {
        var membership = EnsureComp<FirelockGroupMemberComponent>(firelock);
        membership.Holder = group.Owner;
        Dirty(firelock, membership);

        group.Comp.Members.Add(firelock);
        DirtyField(group, group.Comp, nameof(FirelockGroupComponent.Members));

        Log.Debug($"Firelock {firelock} has joined group {group}");

        return (firelock, membership);
    }

    /// <summary>
    /// Makes the member leave whatever group it belongs to. May trigger a rebalance if it was the group owner.
    /// </summary>
    /// <param name="member">The member to remove from their group</param>
    private void LeaveGroup(Entity<FirelockGroupMemberComponent> member)
    {
        var groupHolder = member.Comp.Holder;
        Log.Debug($"Firelock {member} is leaving group: {groupHolder}");

        // If the group cannot be found, just delete the comp.
        if (!TryComp<FirelockGroupComponent>(groupHolder, out var groupComp))
        {
            Log.Warning($"Firelock {member} has membership comp, but group owner does not exist: {groupHolder}");
            RemComp<FirelockGroupMemberComponent>(member);
            Dirty(member);
            return;
        }

        // Remove this entity from the group membership.
        groupComp.Members.Remove(member.Owner);
        DirtyField(groupHolder, groupComp, nameof(FirelockGroupComponent.Members));
        RemComp<FirelockGroupMemberComponent>(member);
        Dirty(member);

        // We're done if this entity was not the group owner.
        if (groupHolder != member.Owner)
            return;

        // If this entity was the only member, the group ceases to exist.
        if (groupComp.Members.Count == 0)
        {
            Log.Debug($"Firelock group {groupHolder} is now empty, deleting");
            RemComp<FirelockGroupComponent>(groupHolder);
            Dirty(groupHolder, groupComp);
            return;
        }

        // Otherwise, rebalance the group. This must be done after we removed the previous owner from the membership
        // list, but can still reference the old entity.
        RebalanceGroup((groupHolder, groupComp));
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

    private void ValidateOrSplitGroup(Entity<FirelockGroupComponent> group)
    {
        var remaining = new HashSet<EntityUid>(group.Comp.Members);
        var islands = new HashSet<HashSet<Entity<FirelockComponent>>>();

        while (remaining.Count > 0)
        {
            var memberId = remaining.First();
            remaining.Remove(memberId);

            if (!TryComp<FirelockComponent>(memberId, out var firelockComp))
                continue;

            Entity<FirelockComponent> memberFirelock = (memberId, firelockComp);
            var island = FindIslandFirelocks(memberFirelock);
            foreach (var islandEnt in island)
                remaining.Remove(islandEnt.Owner);
            islands.Add(island);
        }

        // If all remaining firelocks still form one consecutive group we won't have to split.
        if (islands.Count <= 1)
            return;

        // All islands force-create new groups.
        var newGroups = new HashSet<Entity<FirelockGroupComponent>>();
        foreach (var island in islands)
        {
            var islandGroup = AppointIslandLeader(island);
            RebalanceGroup(islandGroup);
            newGroups.Add(islandGroup);
        }

        // Raise a single split event.
        var islandSplitEv = new FirelockGroupSplitEvent(group, newGroups);
        RaiseLocalEvent(group, ref islandSplitEv);
    }

    /// <summary>
    /// Given an island, appoint a new group leader, assign all members to this group, and return the new group.
    /// </summary>
    private Entity<FirelockGroupComponent> AppointIslandLeader(HashSet<Entity<FirelockComponent>> island)
    {
        // Whichever first entity we find becomes the leader. (Rebalancing happens later).
        var newLeader = island.First();

        var groupComp = EnsureComp<FirelockGroupComponent>(newLeader);
        groupComp.Members = island.Select(ent => ent.Owner).ToHashSet();

        foreach (var firelock in island)
        {
            var member = EnsureComp<FirelockGroupMemberComponent>(firelock);
            member.Holder = newLeader;
        }

        return (newLeader, groupComp);
    }

    /// <summary>
    /// Triggers a group rebalance. This means a new group leader entity will be elected, given the <see cref="FirelockGroupComponent"/>,
    /// and all other members will have their membership component updated to point to the new owner.
    /// Note that this method does *not* require the given entity to actually be a member, meaning it's compatible
    /// with rebalancing after a member leaves, provided the group membership list has had the previous owner removed.
    /// </summary>
    /// <param name="groupEnt">The current or former group owner</param>
    private void RebalanceGroup(Entity<FirelockGroupComponent> groupEnt)
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
            return;

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
            return;
        }

        // Create the new group.
        var newGroup = EnsureComp<FirelockGroupComponent>(closestEntity);
        newGroup.Members = members.Select(member => member.Owner).ToHashSet();
        Dirty(closestEntity, newGroup);

        // Update all members to reference the new group leader.
        foreach (var member in members)
        {
            member.Comp2.Holder = closestEntity;
            DirtyField(member, member.Comp2, nameof(FirelockGroupMemberComponent.Holder));
        }

        // Remove the group comp from the old group holder.
        RemComp<FirelockGroupComponent>(groupEnt);
        Dirty(groupEnt);
        Log.Debug($"New group leader chosen: was {groupEnt}, is now {closestEntity}");
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

    #region Helpers

    private bool GetGroup(EntityUid uid, [NotNullWhen(true)] out Entity<FirelockGroupComponent>? group)
    {
        if (TryComp<FirelockGroupMemberComponent>(uid, out var groupMember))
            return GetGroup((uid, groupMember), out group);

        if (TryComp<FirelockGroupComponent>(uid, out var groupComponent))
        {
            group = (uid, groupComponent);
            return true;
        }

        group = null;
        return false;
    }

    private bool GetGroup(Entity<FirelockComponent> firelock, [NotNullWhen(true)] out Entity<FirelockGroupComponent>? group)
    {
        if (TryComp<FirelockGroupComponent>(firelock, out var groupComponent))
        {
            group = (firelock.Owner, groupComponent);
            return true;
        }

        if (TryComp<FirelockGroupMemberComponent>(firelock, out var groupMember))
            return GetGroup((firelock, groupMember), out group);

        group = null;
        return false;
    }

    private bool GetGroup(
        Entity<FirelockGroupMemberComponent> member,
        [NotNullWhen(true)] out Entity<FirelockGroupComponent>? group)
    {
        if (TryComp<FirelockGroupComponent>(member.Comp.Holder, out var groupComp))
        {
            group = (member, groupComp);
            return true;
        }

        group = null;
        RemComp<FirelockGroupMemberComponent>(member);
        return false;
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
    private List<Entity<FirelockComponent>> FindAdjacentFirelocks(Entity<FirelockComponent> firelock, TransformComponent xform)
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
