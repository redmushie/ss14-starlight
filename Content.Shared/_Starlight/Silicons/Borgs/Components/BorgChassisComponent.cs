using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

// ReSharper disable once CheckNamespace
namespace Content.Shared.Silicons.Borgs.Components;

public sealed partial class BorgChassisComponent
{
    #region Visuals

    // Note that "HasMindState" and "NoMindState" exist in the upstream file.
    [DataField] public string ShuntMindState = string.Empty;
    [DataField] public string RemoteControlState = string.Empty;

    [DataField] public Color HasMindColor = Color.FromHex("#44dada"); // Blue
    [DataField] public Color NoMindColor = Color.FromHex("#808080"); // Grey
    [DataField] public Color ShuntMindColor = Color.FromHex("#da44da"); // Magenta
    [DataField] public Color RemoteControlColor = Color.FromHex("#44da44"); // Green

    /// <summary>
    /// Set when the eye is a greyscale emission mask (the subtype configured a
    /// SpriteEmissionPath), so the mind colors may be multiplied onto it. False leaves the
    /// stock pre-colored eye untinted — the original behavior.
    /// </summary>
    [DataField] public bool UseMindColors;

    #endregion

    [DataField] public ProtoId<JobIconPrototype> JobIconOverride = "JobIconBorg";
    [DataField] private string? _jobTitle;
    [DataField] public LocId? JobTitleOverride = "job-name-borg";

    public string? LocalizedJobTitle
    {
        set => _jobTitle = value;
        get => _jobTitle ?? Loc.GetString(JobTitleOverride ?? string.Empty);
    }
}
