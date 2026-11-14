using Content.Shared.Inventory;
using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;
using Content.Shared.Materials;
using Robust.Shared.Timing;

namespace Content.Shared.Storage.EntitySystems;

/// <summary>
/// <see cref="MagnetPickupComponent"/>
/// </summary>
public sealed partial class MagnetPickupSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;

    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        SLInitialize();
        SubscribeLocalEvent<MagnetPickupComponent, MapInitEvent>(OnMagnetMapInit);
    }

    private void OnMagnetMapInit(EntityUid uid, MagnetPickupComponent component, MapInitEvent args)
    {
        component.NextScan = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MagnetPickupComponent, TransformComponent, MetaDataComponent>();
        var currentTime = _timing.CurTime;

        while (query.MoveNext(out var uid, out var comp, out var xform, out var meta))
        {
            if (comp.NextScan > currentTime)
                continue;

            comp.NextScan += ScanDelay;
            Dirty(uid, comp);

            // Starlight-start
            if ((comp.RequiresEquipping && !IsEquipped((uid, comp, xform, meta))) ||
                (comp.RequiresCarrying && !IsCarriedAlive(uid)))
            {
                // Try to toggle off. Will do nothing if this comp isn't used in combination with an ItemToggle comp.
                _itemToggle.TryDeactivate((uid, null), predicted: false, showPopup: true);
                continue;
            }

            if (TryComp<StorageComponent>(uid, out var storage))
            {
                ProcessStoragePickup(uid, comp, xform, storage);
            }
            else if (TryComp<MaterialStorageComponent>(uid, out var materialStorage))
            {
                ProcessMaterialPickup(uid, comp, xform, materialStorage);
            }
            // Starlight-end
        }
    }
}
