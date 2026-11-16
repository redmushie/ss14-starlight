using System.Linq;
using Content.Client.Clothing;
using Content.Client.Inventory;
using Content.Client.Silicons.Borgs;
using Content.Shared.Inventory;
using Content.Shared._Afterlight.Silicons.Borgs;
using Content.Shared.Movement.Components;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._Afterlight.Silicons.Borgs;

/// <summary>
/// Primarily handles the appearance aspects of the borg subtype.
/// </summary>
public sealed partial class BorgSwitchableSubtypeSystem : SharedBorgSwitchableSubtypeSystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private BorgSystem _borg = default!;
    [Dependency] private BorgSwitchableTypeSystem _borgType = default!; // Starlight
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private ClientClothingSystem _clothing = null!; // Starlight

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, AfterAutoHandleStateEvent>(OnAutoHandleEvent);
    }

    private void OnAutoHandleEvent(Entity<BorgSwitchableSubtypeComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // Starlight Begin
        // No explicit subtype and no type-default: fall back to the legacy borg-type eye path.
        if (ResolveSubtype(ent) == null)
        {
            if (TryComp<BorgSwitchableTypeComponent>(ent, out var borgType))
                _borgType.UpdateEntityAppearance((ent.Owner, borgType));
            return;
        }
        // Starlight End
        SelectBorgSubtype(ent);
    }

    private void OnComponentStartup(Entity<BorgSwitchableSubtypeComponent> ent, ref ComponentStartup args)
    {
        SelectBorgSubtype(ent);
    }

    protected override void UpdateEntityAppearance(Entity<BorgSwitchableSubtypeComponent> entity, EntityPrototype borgSubtypePrototype)
    {
        // LOT of copy pasted code from BorgSwitchableTypeSystem, but is probably necessary unless the upstream code
        // is refactored

        if (!borgSubtypePrototype.TryGetComponent<BorgSubtypeDefinitionComponent>(out var borgSubtype, ComponentFactory))
            return;

        // get our required components
        var (owner, _) = entity;
        if (!TryComp<SpriteComponent>(entity, out var chassisSprite))
            return;

        // remove all existing layers
        for (int i = chassisSprite.AllLayers.Count() - 1; i >= 0; i--)
        {
            _sprite.RemoveLayer((entity, chassisSprite), i);
        }

        // Starlight begin
        if (TryComp<InventorySlotsComponent>(entity, out var inventorySlots))
            inventorySlots.VisualLayerKeys.Clear();
        // Starlight end

        for (int i = 0; i < borgSubtype.LayerData.Length; i++)
        {
            var layerData = borgSubtype.LayerData[i];

            layerData.RsiPath = borgSubtype.SpritePath?.ToString();
            if (borgSubtype.Offset != null)
                layerData.Offset = borgSubtype.Offset;
            _sprite.AddLayer((owner, chassisSprite), layerData, i);
        }

        // Starlight begin
        if (TryComp<InventoryComponent>(entity, out var inventory))
            _clothing.InitClothing(owner, inventory);
        // Starlight end

        if (borgSubtype.SpriteToggleLightState is { } lightState)
            _sprite.LayerSetRsiState((entity, chassisSprite), BorgVisualLayers.LightStatus, lightState);

        if (borgSubtype.SpriteBodyState is { } bodyState)
            _sprite.LayerSetRsiState((entity, chassisSprite), BorgVisualLayers.Body, bodyState);

        // Starlight: point the mind-state (emission) layer at its own rsi if one is set,
        // so greyscale emission art can live under _Starlight without a shared chassis rsi.
        // Set the state in the same call: otherwise LayerSetRsi keeps the layer's current
        // state (borg_e, from the chassis layerData) and errors validating it against the
        // emission rsi before SetMindStates overrides it below.
        if (borgSubtype.SpriteEmissionPath is { } emissionPath)
            _sprite.LayerSetRsi((entity, chassisSprite), BorgVisualLayers.Light, emissionPath, borgSubtype.SpriteNoMindState);

        // Starlight: add a pre-colored on/off Powered emission layer only when the subtype
        // defines one. Visibility is toggled with the mind/control state in BorgSystem.
        if (borgSubtype.PoweredState is { } poweredState)
        {
            var poweredLayer = new PrototypeLayerData
            {
                RsiPath = (borgSubtype.PoweredEmissionPath ?? borgSubtype.SpritePath)?.ToString(),
                State = poweredState,
                Shader = "unshaded",
                Visible = false,
            };
            var poweredIndex = _sprite.AddLayer((owner, chassisSprite), poweredLayer, null);
            _sprite.LayerMapSet((owner, chassisSprite), BorgVisualLayers.Powered, poweredIndex);
        }

        if (TryComp<BorgChassisComponent>(entity, out var chassis))
        {
            _borg.SetMindStates(
                (entity.Owner, chassis),
                borgSubtype.SpriteHasMindState,
                borgSubtype.SpriteNoMindState,
                borgSubtype.SpriteShuntMindState,
                borgSubtype.SpriteRemoteControlState,
                borgSubtype.SpriteHasMindColor,
                borgSubtype.SpriteNoMindColor,
                borgSubtype.SpriteShuntMindColor,
                borgSubtype.SpriteRemoteControlColor,
                borgSubtype.SpriteEmissionPath != null);

            if (TryComp(entity, out AppearanceComponent? appearance))
            {
                // Queue update so state changes apply.
                _appearance.QueueUpdate(entity, appearance);
            }
        }

        if (borgSubtype.SpriteBodyMovementState is { } movementState)
        {
            var spriteMovement = EnsureComp<SpriteMovementComponent>(entity);
            spriteMovement.NoMovementLayers.Clear();
            spriteMovement.NoMovementLayers["movement"] = new PrototypeLayerData
            {
                State = borgSubtype.SpriteBodyState,
            };
            spriteMovement.MovementLayers.Clear();
            spriteMovement.MovementLayers["movement"] = new PrototypeLayerData
            {
                State = movementState,
            };
        }
        else
        {
            RemComp<SpriteMovementComponent>(entity);
        }

        base.UpdateEntityAppearance(entity, borgSubtypePrototype);
    }
}
