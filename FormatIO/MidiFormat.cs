using System.Text;
using MidiCore;
using MidiCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Standard MIDI files and the RIFF-wrapped variants that embed one (.mid, .midi, .kar, .rmi, .xf).
public sealed class MidiFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        var bytes = ReadAll(stream);
        var smf = Unwrap(bytes);
        var song = SmfParser.Parse(smf);
        return ProjectBuilder.Build(song, Options());
    }

    static byte[] Unwrap(byte[] bytes)
    {
        if (RiffMidi.IsRiff(bytes))
        {
            var inner = RiffMidi.ExtractSmf(bytes);
            if (inner.Length >= 4)
                return inner.ToArray();
            throw new FormatException("RIFF container found but it holds no embedded Standard MIDI File");
        }
        return bytes;
    }

    internal static ProjectBuilder.Options Options() =>
        SettingsSource.Instance.ToBuilderOptions();

    internal static byte[] ReadAll(System.IO.Stream s)
    {
        if (s is System.IO.MemoryStream ms && ms.TryGetBuffer(out var buf))
            return buf.ToArray();
        using var copy = new System.IO.MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }

    // ---- extension settings (shared by every entry of this package) ----
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);
}
