using MidiCore;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace MidiFormatsSupport;

/// Shared extension settings.
public sealed class SettingsSource : IExtensionSettings
{
    public static readonly SettingsSource Instance = new();

    // instrument family -> (engine Type, source Id). Empty Type/Id = use the host default.
    private readonly Dictionary<string, (string Type, string Id)> _familySources = new();
    private bool _dawMode = true;
    private bool _mergeSameProgram;

    private static readonly string[] _families =
    {
        Families.Keys, Families.Guitar, Families.Bass, Families.Strings, Families.Ensemble,
        Families.Brass, Families.Reed, Families.Flute, Families.SynthLead, Families.SynthPad,
        Families.Pad, Families.Ethnic, Families.Percussive, Families.Drums, Families.Choir,
        Families.Effects, Families.Unknown,
        // the catch-all: whatever the user configures here is used for any lane whose own family has no source configured, so a fresh import is never left silent
        Families.Default,
    };

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>();

        props.Add(("daw_mode", "DAW mode (one lane per track)"), CheckBoxConfig.Create(true));
        props.Add(("merge_same_program", "Collapse to one track per instrument"), CheckBoxConfig.Create(false));
        // Where the OS keeps its default GM bank, and whether the soft-font engine can play it.
        props.Add(("system_bank", "System GM bank"), TextBoxConfig.Create(SystemSoundFonts.Describe()));

        foreach (var family in _families)
        {
            string type = "", id = "";
            if (_familySources.TryGetValue(family, out var v)) { type = v.Type; id = v.Id; }
            // engine type (free text, e.g. the instrument engine id the host shows)
            props.Add(($"src_type_{family}", $"{family} - engine type"), TextBoxConfig.Create(type));
            props.Add(($"src_id_{family}", $"{family} - source id"), TextBoxConfig.Create(id));
        }
        return ObjectConfig.Create(props);
    }

    public void ApplySettings(PropertyObject settings)
    {
        _dawMode = settings.GetBoolean("daw_mode", true);
        _mergeSameProgram = settings.GetBoolean("merge_same_program", false);
        foreach (var family in _families)
        {
            string type = settings.GetString($"src_type_{family}", "").Trim();
            string id = settings.GetString($"src_id_{family}", "").Trim();
            _familySources[family] = (type, id);
        }
    }

    public ProjectBuilder.Options ToBuilderOptions() => new()
    {
        DawMode = _dawMode,
        MergeSameProgram = _mergeSameProgram,
        SkipEmptyLanes = true,
        Sources = new Dictionary<string, (string Type, string Id)>(_familySources),
        Log = s => { try { TuneLabContext.Global.GetLogger().Info(s); } catch { } },
    };
}
