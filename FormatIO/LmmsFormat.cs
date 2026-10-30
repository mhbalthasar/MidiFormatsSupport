using DawCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// LMMS projects (.mmp): one TuneLab track per LMMS track; Beat/Bassline clips are unrolled.
public sealed class LmmsFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        var song = LmmsParser.Parse(MidiFormat.ReadAll(stream));
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
