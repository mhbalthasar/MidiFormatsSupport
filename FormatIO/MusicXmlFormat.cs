using MidiCore;
using MidiCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// MusicXML (.musicxml, .xml) and compressed MusicXML (.mxl).
public sealed class MusicXmlFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        var bytes = ReadAll(stream);
        var song = MusicXmlParser.ParseBytes(bytes);
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);

    internal static byte[] ReadAll(System.IO.Stream s)
    {
        using var copy = new System.IO.MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }
}
