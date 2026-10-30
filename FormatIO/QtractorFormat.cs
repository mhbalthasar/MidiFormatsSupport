using DawCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Qtractor sessions (.qtr, XML): one TuneLab track per MIDI track; clip notes read from
/// the session's companion .mid files.
public sealed class QtractorFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        // Qtractor keeps note data in sibling files, so we need where the session came from.
        string? path = (stream as System.IO.FileStream)?.Name;
        var song = QtractorParser.Parse(MidiFormat.ReadAll(stream), path);
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
