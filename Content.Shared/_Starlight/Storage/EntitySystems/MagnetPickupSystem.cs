using Content.Shared.Item.ItemToggle;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.Components;
using Robust.Shared.Physics.Components;

// ReSharper disable once CheckNamespace
namespace Content.Shared.Storage.EntitySystems;

public sealed partial class MagnetPickupSystem
{
    [Dependency] private MobStateSystem _mobState = null!;
    [Dependency] private SharedMaterialStorageSystem _materialStorage = null!;
    [Dependency] private ItemToggleSystem _itemToggle = null!;

    private EntityQuery<PhysicsComponent> _physicsQuery;

    public void SLInitialize() => _physicsQuery = GetEntityQuery<PhysicsComponent>();

    private void ProcessStoragePickup(EntityUid uid, MagnetPickupComponent comp, TransformComponent xform, StorageComponent storage)
    {
        // No space
        if (!_storage.HasSpace((uid, storage)))
            return;

        var parentUid = xform.ParentUid;
        var playedSound = false;
        var finalCoords = xform.Coordinates;
        var moverCoords = _transform.GetMoverCoordinates(uid, xform);

        foreach (var near in _lookup.GetEntitiesInRange(uid, comp.Range, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (_whitelistSystem.IsWhitelistFail(storage.Whitelist, near))
                continue;

            if (!_physicsQuery.TryGetComponent(near, out var physics) || physics.BodyStatus != BodyStatus.OnGround)
                continue;

            if (near == parentUid)
                continue;

            // TODO: Probably move this to storage somewhere when it gets cleaned up
            // TODO: This sucks but you need to fix a lot of stuff to make it better
            // the problem is that stack pickups delete the original entity, which is fine, but due to
            // game state handling we can't show a lerp animation for it.
            var nearXform = Transform(near);
            var nearMap = _transform.GetMapCoordinates(near, xform: nearXform);
            var nearCoords = _transform.ToCoordinates(moverCoords.EntityId, nearMap);

            if (!_storage.Insert(uid, near, out var stacked, storageComp: storage, playSound: !playedSound))
                continue;

            // Play pickup animation for either the stack entity or the original entity.
            if (stacked != null)
                _storage.PlayPickupAnimation(stacked.Value, nearCoords, finalCoords, nearXform.LocalRotation);
            else
                _storage.PlayPickupAnimation(near, nearCoords, finalCoords, nearXform.LocalRotation);

            playedSound = true;
        }
    }

    private void ProcessMaterialPickup(EntityUid uid, MagnetPickupComponent comp, TransformComponent xform, MaterialStorageComponent materialStorage)
    {
        var parentUid = xform.ParentUid;

        foreach (var near in _lookup.GetEntitiesInRange(uid, comp.Range, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (materialStorage.Whitelist != null && _whitelistSystem.IsWhitelistFail(materialStorage.Whitelist, near))
                continue;

            if (!_physicsQuery.TryGetComponent(near, out var physics) || physics.BodyStatus != BodyStatus.OnGround)
                continue;

            if (near == parentUid)
                continue;

            _materialStorage.TryInsertMaterialEntity(uid, near, uid, materialStorage);
        }
    }

    /// <summary>
    /// Whether the entity is equipped (if required) and
    /// </summary>
    /// <param name="ent"></param>
    /// <returns></returns>
    private bool IsEquipped(Entity<MagnetPickupComponent, TransformComponent?, MetaDataComponent?> ent)
    {
        if (!ent.Comp1.RequiresEquipping)
            return true;

        // Check if it's contained in an equipment slot.
        if (!_inventory.TryGetContainingSlot((ent.Owner, ent.Comp2, ent.Comp3), out var slotDef))
            return false;

        // Lastly we verify that the slot it's in match one of the permitted slots.
        return (slotDef.SlotFlags & ent.Comp1.SlotFlags) != 0x0;
    }

    /// <summary>
    /// Finds the entity that is carrying the given entity, if any, and whether they are alive.
    /// </summary>
    /// <param name="uid">The entity for which to determine if it's carried by an alive person</param>
    /// <returns>Whether the entity is carried and the carrier is alive.</returns>
    private bool IsCarriedAlive(EntityUid uid)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();
        var current = uid;
        while (xformQuery.TryGetComponent(current, out var xform) && xform.ParentUid.IsValid())
        {
            current = xform.ParentUid;
            if (HasComp<MobStateComponent>(current))
                return _mobState.IsAlive(current);
        }

        return false;
    }
}

/// <summary>
/// Raised on a <see cref="MagnetPickupComponent"/> when the magnet is effectively disabled.
/// </summary>
public struct MagnetTriggerDisableEvent;
