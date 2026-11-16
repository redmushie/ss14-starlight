using Robust.Shared.Utility;

// ReSharper disable once CheckNamespace
namespace Content.Shared._Afterlight.Silicons.Borgs;

public sealed partial class BorgSubtypeDefinitionComponent
{
    /// <summary>
    /// Optional separate rsi for the mind-state (emission) layer, letting the greyscale
    /// emission art live under _Starlight while the body keeps using <see cref="SpritePath"/>.
    /// </summary>
    [DataField]
    public ResPath? SpriteEmissionPath { get; set; }

    [DataField]
    public string SpriteNoMindState { get; set; } = "borg_emission_mind";

    [DataField]
    public string SpriteLocalMindState { get; set; } = "borg_emission_mind";

    [DataField]
    public string SpriteShuntMindState { get; set; } = "borg_emission_mind";

    [DataField]
    public string SpriteRemoteControlState { get; set; } = "borg_emission_mind";


    [DataField]
    public Color SpriteNoMindColor { get; set; } = Color.FromHex("#ffc300");

    [DataField]
    public Color SpriteLocalMindColor { get; set; } = Color.FromHex("#00a7b9");

    [DataField]
    public Color SpriteShuntMindColor { get; set; } = Color.FromHex("#ff00ff");

    [DataField]
    public Color SpriteRemoteControlColor { get; set; } = Color.FromHex("#00ff00");

    /// <summary>
    /// Optional pre-colored emission state shown on its own on/off layer
    /// (BorgVisualLayers.Powered) whenever the borg has a mind/control, alongside the
    /// mind-state eye. When null the layer is not created at all. Not tintable — the
    /// sprite carries its own color.
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
