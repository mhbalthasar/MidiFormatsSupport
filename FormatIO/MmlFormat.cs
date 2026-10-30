using MidiCore;
using MidiCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Music Macro Language (.mml) — plain text.
public sealed class MmlFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8, true, -1, true);
        string text = reader.ReadToEnd();
        var song = MmlParser.Parse(text);
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
