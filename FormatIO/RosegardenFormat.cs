using DawCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Rosegarden projects (.rg, gzip-compressed XML): one TuneLab track per Rosegarden track.
public sealed class RosegardenFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        var song = RosegardenParser.Parse(MidiFormat.ReadAll(stream));
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
