using MidiCore;
using MidiCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// ABC notation (.abc) — plain text, no container.
public sealed class AbcFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8, true, -1, true);
        string text = reader.ReadToEnd();
        var song = AbcParser.Parse(text);
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
