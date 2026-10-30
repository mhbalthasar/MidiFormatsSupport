using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Exports .mid / .midi (SMF format 1).
public sealed class MidExportFormat : IExportFormat, IExtensionSettings
{
    public void Serialize(System.IO.Stream output, ProjectInfo info)
    {
        byte[] smf = SmfWriter.Write(info, includeLyrics: false);
        output.Write(smf, 0, smf.Length);
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
