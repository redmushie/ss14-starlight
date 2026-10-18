using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Examine;

namespace Content.Shared._Starlight.Doors.Systems;

public abstract partial class SharedFirelockGroupSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnExamineFirelockGroup(Entity<FirelockLeaderComponent> firelock, ref ExaminedEvent ev)
    {
        using (ev.PushGroup("firelock-group"))
        {
            ev.PushMarkup($"Group owner");
        }
    }

    [SubscribeLocalEvent]
    private void OnExamineFirelockGroupMember(Entity<FirelockFollowerComponent> firelock, ref ExaminedEvent ev)
    {
        using (ev.PushGroup("firelock-group-member"))
        {
            ev.PushMarkup($"ID: {GetNetEntity(firelock).Id}");
            ev.PushMarkup($"Member of group {GetNetEntity(firelock.Comp.Leader).Id}");
        }
    }
}

/// <summary>
/// New firelock group created. Raised on the group entity.
/// </summary>
[ByRefEvent]
public struct FirelockGroupCreatedEvent;

/// <summary>
/// New firelock group created. Raised on the group entity.
/// </summary>
[ByRefEvent]
public struct FirelockGroupDestroyedEvent;

/// <summary>
/// Raised on a member when joining a group.
/// </summary>
/// <param name="group">The group</param>
[ByRefEvent]
public struct FirelockGroupMemberJoinEvent(Entity<FirelockLeaderComponent> group)
{
    public readonly Entity<FirelockLeaderComponent> Group = group;
}

/// <summary>
/// Raised on a member when leaving a group.
/// </summary>
/// <param name="group">The group</param>
[ByRefEvent]
public struct FirelockGroupMemberLeaveEvent(Entity<FirelockLeaderComponent>? group)
{
    public readonly Entity<FirelockLeaderComponent>? Group = group;
}

/// <summary>
/// Raised on the *new leader* entity when a firelock group changes leader.
/// </summary>
[ByRefEvent]
public struct FirelockGroupLeaderChangedEvent(Entity<FirelockLeaderComponent> formerLeader)
{
    public readonly Entity<FirelockLeaderComponent> FormerLeader = formerLeader;
}
