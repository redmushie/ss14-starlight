using Robust.Shared.Utility;

// ReSharper disable once CheckNamespace
namespace Content.Shared._Afterlight.Silicons.Borgs;

public sealed partial class BorgSubtypeDefinitionComponent
{
    // Mind-state eye.
    //
    // Additive over the stock borg_e / borg_e_r model: by default a subtype renders the
    // plain pre-colored eye with no tint, exactly as before. The emission/tint features
    // below are all opt-in — point an eye state at a greyscale emission sprite and/or set
    // a tint color, and only then does the advanced path kick in.

    /// <summary>
    /// Eye state when the borg has a (local player) mind. Stock default; emission subtypes
    /// override it with their greyscale emission state.
    /// </summary>
    [DataField]
    public string SpriteHasMindState { get; set; } = "borg_e";

    /// <summary>
    /// Eye state when the borg has no mind.
    /// </summary>
    [DataField]
    public string SpriteNoMindState { get; set; } = "borg_e_r";

    /// <summary>
    /// Optional eye state when an AI is shunted into the borg. Null falls back to
    /// <see cref="SpriteHasMindState"/>.
    /// </summary>
    [DataField]
    public string? SpriteShuntMindState { get; set; }

    /// <summary>
    /// Optional eye state when the borg is remote-controlled. Null falls back to
    /// <see cref="SpriteHasMindState"/>.
    /// </summary>
    [DataField]
    public string? SpriteRemoteControlState { get; set; }

    /// <summary>
    /// Optional separate rsi for the mind-state eye layer (greyscale emission art under
    /// _Starlight). Null keeps the eye on <see cref="SpritePath"/>.
    /// </summary>
    [DataField]
    public ResPath? SpriteEmissionPath { get; set; }

    // Per-state tints. Null = no tint (white), so the stock pre-colored eye is untouched;
    // emission subtypes set these to color their greyscale mask.
    [DataField]
    public Color? SpriteHasMindColor { get; set; }

    [DataField]
    public Color? SpriteNoMindColor { get; set; }

    [DataField]
    public Color? SpriteShuntMindColor { get; set; }

    [DataField]
    public Color? SpriteRemoteControlColor { get; set; }

    /// <summary>
    /// Optional pre-colored on/off emission shown on its own layer
    /// (BorgVisualLayers.Powered) whenever the borg has a mind/control, alongside the eye.
    /// When null the layer is not created at all. Not tintable — the sprite carries its
    /// own color.
    /// </summary>
    [DataField]
    public string? PoweredState { get; set; }

    /// <summary>
    /// Optional separate rsi for <see cref="PoweredState"/>; falls back to
    /// <see cref="SpritePath"/> when null.
    /// </summary>
    [DataField]
    public ResPath? PoweredEmissionPath { get; set; }
}
