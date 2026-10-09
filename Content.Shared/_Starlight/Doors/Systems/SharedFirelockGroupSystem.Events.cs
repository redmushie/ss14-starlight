using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Examine;

namespace Content.Shared._Starlight.Doors.Systems;

public abstract partial class SharedFirelockGroupSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnExamineFirelockGroup(Entity<FirelockGroupComponent> firelock, ref ExaminedEvent ev)
    {
        using (ev.PushGroup("firelock-group"))
        {
            ev.PushMarkup($"Group owner");
        }
    }

    [SubscribeLocalEvent]
    private void OnExamineFirelockGroupMember(Entity<FirelockGroupMemberComponent> firelock, ref ExaminedEvent ev)
    {
        using (ev.PushGroup("firelock-group-member"))
        {
            ev.PushMarkup($"ID: {GetNetEntity(firelock).Id}");
            ev.PushMarkup($"Member of group {GetNetEntity(firelock.Comp.Holder).Id}");
        }
    }
}

[ByRefEvent]
public struct FirelockGroupDestroyedEvent(Entity<FirelockGroupComponent> firelock)
{
    public readonly Entity<FirelockGroupComponent> Firelock = firelock;
}

[ByRefEvent]
public struct FirelockLeavesGroupEvent(
    Entity<FirelockGroupComponent> group,
    Entity<FirelockGroupMemberComponent> firelock)
{
    public readonly Entity<FirelockGroupComponent> Group = group;
    public readonly Entity<FirelockGroupMemberComponent> Firelock = firelock;
}

[ByRefEvent]
public struct FirelockJoinsGroupEvent(
    Entity<FirelockGroupComponent> group,
    Entity<FirelockGroupMemberComponent> firelock)
{
    public readonly Entity<FirelockGroupComponent> Group = group;
    public readonly Entity<FirelockGroupMemberComponent> Firelock = firelock;
}

[ByRefEvent]
public struct FirelockGroupMergeEvent(
    Entity<FirelockGroupComponent> group,
    Entity<FirelockGroupComponent> absorbed)
{
    public readonly Entity<FirelockGroupComponent> Group = group;
    public readonly Entity<FirelockGroupComponent> Absorbed = absorbed;
}

[ByRefEvent]
public struct FirelockGroupSplitEvent(
    Entity<FirelockGroupComponent> group,
    HashSet<Entity<FirelockGroupComponent>> newGroups)
{
    public readonly Entity<FirelockGroupComponent> Group = group;
    public readonly HashSet<Entity<FirelockGroupComponent>> NewGroups = newGroups;
}
