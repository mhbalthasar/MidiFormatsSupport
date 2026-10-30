using DawCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Ableton Live Sets (.als): one TuneLab track per MIDI track, its clips merged.
public sealed class AbletonFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        var song = AbletonParser.Parse(MidiFormat.ReadAll(stream));
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
