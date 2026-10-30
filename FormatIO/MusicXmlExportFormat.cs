using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Exports MusicXML 4.0 partwise (.musicxml): one part per track, notes laid into measures according to the project's time signature (default 4/4).
public sealed class MusicXmlExportFormat : IExportFormat, IExtensionSettings
{
    public void Serialize(System.IO.Stream output, ProjectInfo info)
    {
        byte[] xml = MusicXmlWriter.Write(info);
        output.Write(xml, 0, xml.Length);
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
