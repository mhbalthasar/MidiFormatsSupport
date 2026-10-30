using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Exports .kar (karaoke SMF): same MIDI content as MidExportFormat, with note lyrics written as Lyric meta events — that is what makes an SMF a KAR.
public sealed class KarExportFormat : IExportFormat, IExtensionSettings
{
    public void Serialize(System.IO.Stream output, ProjectInfo info)
    {
        byte[] smf = SmfWriter.Write(info, includeLyrics: true);
        output.Write(smf, 0, smf.Length);
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
