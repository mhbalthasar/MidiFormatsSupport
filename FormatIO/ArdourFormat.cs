using DawCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Ardour sessions (.ardour, XML): one TuneLab track per MIDI route, notes read from the
/// session's companion .mid sources under interchange/.
public sealed class ArdourFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        // Ardour keeps note data in sibling files, so we need where the session came from.
        string? path = (stream as System.IO.FileStream)?.Name;
        var song = ArdourParser.Parse(MidiFormat.ReadAll(stream), path);
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
