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
public sealed class FirelockGroupDestroyedEvent(Entity<FirelockGroupComponent> group) : EntityEventArgs
{
    public readonly Entity<FirelockGroupComponent> Group = group;
}

[ByRefEvent]
public struct FirelockGroupMemberRemovedEvent(
    Entity<FirelockGroupComponent> group,
    Entity<FirelockGroupMemberComponent> member)
{
    public readonly Entity<FirelockGroupComponent> Group = group;
    public readonly Entity<FirelockGroupMemberComponent> Member = member;
}

[ByRefEvent]
public struct FirelockGroupMemberAddedEvent(
    Entity<FirelockGroupComponent> group,
    Entity<FirelockGroupMemberComponent> member)
{
    public readonly Entity<FirelockGroupComponent> Group = group;
    public readonly Entity<FirelockGroupMemberComponent> Member = member;
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
public struct FirelockGroupLeaderChangedEvent(
    Entity<FirelockGroupComponent> previousLeader,
    Entity<FirelockGroupComponent> currentLeader)
{
    public readonly Entity<FirelockGroupComponent> PreviousLeader = previousLeader;
    public readonly Entity<FirelockGroupComponent> CurrentLeader = currentLeader;
}

[ByRefEvent]
public struct FirelockGroupSplitEvent(
    Entity<FirelockGroupComponent> group,
    HashSet<Entity<FirelockGroupComponent>> newGroups)
{
    public readonly Entity<FirelockGroupComponent> Group = group;
    public readonly HashSet<Entity<FirelockGroupComponent>> NewGroups = newGroups;
}
