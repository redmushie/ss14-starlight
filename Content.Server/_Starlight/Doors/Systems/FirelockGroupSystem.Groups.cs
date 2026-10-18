using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared._Starlight.Doors.Systems;
using Content.Shared.Doors.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockGroupSystem : SharedFirelockGroupSystem
{
    [Robust.Shared.IoC.Dependency] private SharedMapSystem _map = default!;
    [Robust.Shared.IoC.Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent]
    private void OnFirelockInit(Entity<FirelockComponent> firelock, ref ComponentInit ev)
    {
        if (!IsValid(firelock)) return;
        Log.Debug($"OnFirelockInit {firelock.Owner}");

        var initials = GetInitials(firelock.Owner);
        var islandScans = ScanIslands(GetFirelocks(initials));
        RebuildIslands(islandScans);
    }

    [SubscribeLocalEvent]
    private void OnMemberAnchorStateChanged(Entity<FirelockComponent> firelock, ref AnchorStateChangedEvent ev)
    {
        var initials = GetInitials(firelock.Owner);
        var islandScans = ScanIslands(GetFirelocks(initials));
        RebuildIslands(islandScans);
    }

    [SubscribeLocalEvent]
    private void OnMemberEntityDeleted(Entity<FirelockFollowerComponent> follower, ref EntityTerminatingEvent ev)
    {
        var initials = GetInitials(follower.Owner);
        var islandScans = ScanIslands(GetFirelocks(initials));
        RebuildIslands(islandScans);
    }

    #region Internal API


    /// <summary>
    /// Tests whether an individual firelock is eligible for grouping.
    /// </summary>
    private bool IsValid(EntityUid firelock, TransformComponent? xform = null)
        => firelock != EntityUid.Invalid &&
           LifeStage(firelock) < EntityLifeStage.Terminating &&
           Resolve(firelock, ref xform) && xform.Anchored;

    #endregion
    #region Island management

    private List<Island> RebuildIslands(List<IslandScan> scans)
    {
        var result = new List<Island>();
        foreach (var scan in scans)
            result.Add(RebuildIsland(scan));
        return result;
    }

    private Island RebuildIsland(IslandScan scan)
    {
        var selectedLeader = SelectLeader(scan);
        var island = new Island(selectedLeader);

        // Assign all firelocks on the island to the selected leader.
        foreach (var firelock in scan.Firelocks)
        {
            if (TryComp<FirelockFollowerComponent>(firelock, out var followerComp))
            {
                // Already following this leader? Cool, do nothing.
                if (followerComp.Leader == selectedLeader.Owner)
                    continue;

                Entity<FirelockFollowerComponent> follower = (firelock, followerComp);

                // If the former leader is resolvable, remove this member from it.
                if (TryComp<FirelockLeaderComponent>(followerComp.Leader, out var formerLeaderComp))
                {
                    Entity<FirelockLeaderComponent> formerLeader = (followerComp.Leader, formerLeaderComp);

                    // Remove the follower from the former leaders' member list.
                    RaiseMemberLeaveEvent(follower, formerLeader);
                    formerLeaderComp.Members.Remove(firelock);
                    DirtyLeader(formerLeader);

                    // Destroy the group if it's empty.
                    if (formerLeader.Comp.Members.Count == 0)
                    {
                        RaiseGroupDestroyedEvent(formerLeader);
                        RemCompDeferred<FirelockLeaderComponent>(formerLeader);
                    }
                }

                selectedLeader.Comp.Members.Add(follower);
                DirtyLeader(selectedLeader);

                // Link the member to the new leader, and announce join.
                follower.Comp.Leader = selectedLeader;
                DirtyFollower(follower);

                RaiseMemberJoinEvent(follower, selectedLeader);
            }
            else
            {
                followerComp = EnsureComp<FirelockFollowerComponent>(firelock);
                Entity<FirelockFollowerComponent> follower = (firelock, followerComp);

                followerComp.Leader = selectedLeader.Owner;
                Dirty(follower);

                selectedLeader.Comp.Members.Add(firelock);
                DirtyLeader(selectedLeader);

                RaiseMemberJoinEvent((firelock, followerComp), selectedLeader);
                island.Members.Add(follower);
            }
        }

        DirtySensingIslands(selectedLeader);
        return island;
    }

    /// <summary>
    /// Given an island scan, elect a leader.
    /// </summary>
    /// <param name="island"></param>
    /// <returns></returns>
    private Entity<FirelockLeaderComponent> SelectLeader(IslandScan island)
    {
        DebugTools.Assert(island.Firelocks.Count > 0, "Cannot select leader with zero eligible firelocks");

        // If there are one or more ('former') group leaders present, just grab one of those.
        if (island.Leaders.Count >= 1)
            return island.Leaders.First();

        // First firelock we find is the new leader.
        var firelock = island.Firelocks.First();
        if (!TryComp<FirelockLeaderComponent>(firelock, out var leader))
        {
            leader = EnsureComp<FirelockLeaderComponent>(firelock);
            leader.Members.Add(firelock);
            var follower = EnsureComp<FirelockFollowerComponent>(firelock);
            follower.Leader = firelock;
            Dirty(firelock, leader);
            Dirty(firelock, follower);

            RaiseGroupCreatedEvent((firelock, leader));
            RaiseMemberJoinEvent((firelock, follower), (firelock, leader));
        }

        return (firelock, leader);
    }

    #endregion

    #region Island scanning

    private HashSet<EntityUid> GetInitials(EntityUid id)
    {
        var result = new HashSet<EntityUid>();
        result.Add(id);

        if (TryComp<FirelockFollowerComponent>(id, out var followerComp) &&
            TryComp<FirelockLeaderComponent>(followerComp.Leader, out var leaderComp))
        {
            result.UnionWith(leaderComp.Members);
        }

        return result;
    }

    private HashSet<Entity<FirelockComponent>> GetFirelocks(HashSet<EntityUid> ids)
    {
        var result = new HashSet<Entity<FirelockComponent>>();
        foreach (var id in ids)
        {
            if (!TryComp<FirelockComponent>(id, out var firelockComp)) continue;
            if (!IsValid(id)) continue;
            result.Add((id, firelockComp));
        }
        return result;
    }

    private List<IslandScan> ScanIslands(HashSet<Entity<FirelockComponent>> initials)
    {
        var remaining = new HashSet<Entity<FirelockComponent>>(initials);
        var islands = new List<IslandScan>();

        while (remaining.Count > 0)
        {
            var item = remaining.First();
            remaining.Remove(item);

            // Ignore ineligible firelocks.
            if (!IsValid(item))
                continue;

            // Floodfill from this single firelock, forming one island.
            var island = ScanIsland(item);
            foreach (var member in island.Firelocks)
                remaining.Remove(member);
            islands.Add(island);
        }

        return islands;
    }

    private IslandScan ScanIsland(Entity<FirelockComponent> firelock)
    {
        // Flood-fill across directly-connected firelocks to discover the whole group starting from this one.
        var pending = new Queue<Entity<FirelockComponent>>();

        var scan = new IslandScan();
        pending.Enqueue(firelock);

        while (pending.TryDequeue(out var current))
        {
            if (!IsValid(current)) continue;

            scan.Firelocks.Add(current);
            if (TryComp<FirelockLeaderComponent>(current, out var groupComp))
                scan.Leaders.Add((current, groupComp));
            if (TryComp<FirelockFollowerComponent>(current, out var memberComp))
                scan.Members.Add((current, memberComp));

            foreach (var neighbor in GetNeighbouringFirelocks(current, Transform(current)))
            {
                // If we haven't seen this neighbor before, enqueue it for exploring.
                if (!scan.Firelocks.Contains(neighbor))
                    pending.Enqueue(neighbor);
            }
        }

        return scan;
    }

    /// <summary>
    /// Returns the firelocks anchored on the four cardinal tiles directly adjacent to
    /// <paramref name="firelock"/>.
    /// </summary>
    private List<Entity<FirelockComponent>> GetNeighbouringFirelocks(Entity<FirelockComponent> firelock,
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

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DirtyLeader(Entity<FirelockLeaderComponent> leader)
    {
        DirtyField(leader.Owner, leader.Comp, nameof(FirelockLeaderComponent.Members));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DirtyFollower(Entity<FirelockFollowerComponent> follower) =>
        DirtyField(follower.Owner, follower.Comp, nameof(FirelockFollowerComponent.Leader));

    private void RaiseGroupCreatedEvent(Entity<FirelockLeaderComponent> groupEnt)
    {
        var ev = new FirelockGroupCreatedEvent();
        RaiseLocalEvent(groupEnt, ref ev);
    }

    private void RaiseGroupDestroyedEvent(Entity<FirelockLeaderComponent> group)
    {
        var ev = new FirelockGroupDestroyedEvent();
        RaiseLocalEvent(group, ref ev);
    }

    private void RaiseMemberJoinEvent(
        Entity<FirelockFollowerComponent> memberEnt,
        Entity<FirelockLeaderComponent> groupEnt)
    {
        var ev = new FirelockGroupMemberJoinEvent(groupEnt);
        RaiseLocalEvent(memberEnt, ref ev);
    }

    private void RaiseMemberLeaveEvent(
        Entity<FirelockFollowerComponent> memberEnt,
        Entity<FirelockLeaderComponent>? groupEnt = null)
    {
        var ev = new FirelockGroupMemberLeaveEvent(groupEnt);
        RaiseLocalEvent(memberEnt, ref ev);
    }

    #endregion

}

record IslandScan()
{
    public HashSet<Entity<FirelockComponent>> Firelocks = new();
    public HashSet<Entity<FirelockLeaderComponent>> Leaders = new();
    public HashSet<Entity<FirelockFollowerComponent>> Members = new();
}

record Island(Entity<FirelockLeaderComponent> Leader)
{
    public Entity<FirelockLeaderComponent> Leader = Leader;
    public HashSet<Entity<FirelockFollowerComponent>> Members = new();
}
