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
    public string SpriteNoMindState { get; set; } = "borg_emission";

    [DataField]
    public string SpriteLocalMindState { get; set; } = "borg_emission";

    [DataField]
    public string SpriteShuntMindState { get; set; } = "borg_emission";

    [DataField]
    public string SpriteRemoteControlState { get; set; } = "borg_emission";


    [DataField]
    public Color SpriteNoMindColor { get; set; } = Color.FromHex("#ffc300");

    [DataField]
    public Color SpriteLocalMindColor { get; set; } = Color.FromHex("#00a7b9");

    [DataField]
    public Color SpriteShuntMindColor { get; set; } = Color.FromHex("#ff00ff");

    [DataField]
    public Color SpriteRemoteControlColor { get; set; } = Color.FromHex("#00ff00");

}
