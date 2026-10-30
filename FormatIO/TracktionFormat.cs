using System.Text;
using DawCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Tracktion/Waveform edits (.tracktionedit): one TuneLab track per track, its MIDI clips merged.
public sealed class TracktionFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        using var reader = new System.IO.StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var song = TracktionParser.Parse(reader.ReadToEnd());
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
