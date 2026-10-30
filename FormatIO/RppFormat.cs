using System.Text;
using DawCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// REAPER project files (.rpp): one TuneLab track per REAPER track, its MIDI items merged.
public sealed class RppFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        var song = RppParser.Parse(ReadAllText(stream));
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    static string ReadAllText(System.IO.Stream s)
    {
        using var reader = new System.IO.StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
