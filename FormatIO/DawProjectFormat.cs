using DawCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// DAWproject bundles (.dawproject): one TuneLab track per arrangement track, matched by the
/// track/instrument name; only MIDI notes are imported (audio clips are ignored).
public sealed class DawProjectFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        var song = DawProjectParser.Parse(MidiFormat.ReadAll(stream));
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
