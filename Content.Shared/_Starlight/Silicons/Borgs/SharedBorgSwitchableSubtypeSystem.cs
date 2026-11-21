using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Prototypes;

// ReSharper disable once CheckNamespace
namespace Content.Shared._Afterlight.Silicons.Borgs;

public abstract partial class SharedBorgSwitchableSubtypeSystem
{
    /// <summary>
    /// Get the borg subtype to render. Returns either the explicit subtype, the default subtype if configured, or null,
    /// in that order.
    /// </summary>
    protected ProtoId<EntityPrototype>? ResolveSubtype(Entity<BorgSwitchableSubtypeComponent> ent)
    {
        if (ent.Comp.BorgSubtype is { } explicitSubtype)
            return explicitSubtype;

        if (TryComp<BorgSwitchableTypeComponent>(ent, out var type)
            && type.SelectedBorgType is { } typeId
            && Prototypes.TryIndex(typeId, out var typeProto)
            && typeProto.DefaultSubtype is { } def)
            return def;

        return null;
    }

    private void SelectSubtypeMessageHandler(Entity<BorgSwitchableTypeComponent> ent, ref BorgSelectSubtypeMessage args)
    {
        if (args.Subtype is { } requested && !Prototypes.HasIndex(requested))
            return;

        if (!TryComp<BorgSwitchableSubtypeComponent>(ent, out var subtypeComp))
        {
            _switchableType.TrySelectBorgType(ent, args.BorgType);
            return;
        }

        var previous = subtypeComp.BorgSubtype;
        subtypeComp.BorgSubtype = args.Subtype;

        if (!_switchableType.TrySelectBorgType(ent, args.BorgType))
        {
            subtypeComp.BorgSubtype = previous;
            return;
        }

        Dirty(ent.Owner, subtypeComp);
    }
}
