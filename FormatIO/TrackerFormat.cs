using System.Text;
using MidiCore;
using MidiCore.Parsers;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Tracker module formats (.mod, .xm, .it, .s3m, .mptm).
public sealed class TrackerFormat : IImportFormat, IExtensionSettings
{
    public ProjectInfo Deserialize(System.IO.Stream stream)
    {
        var bytes = ReadAll(stream);
        var song = Parse(bytes);
        return ProjectBuilder.Build(song, MidiFormat.Options());
    }

    internal static Song Parse(byte[] bytes)
    {
        Song song;
        if (bytes.Length > 17 && Encoding.ASCII.GetString(bytes, 0, 17) == "Extended Module: ")
            song = XmParser.Parse(bytes);
        else if (bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "IMPM")
            song = ItParser.Parse(bytes);
        else if (bytes.Length > 48 && Encoding.ASCII.GetString(bytes, 44, 4) == "SCRM")
            song = S3mParser.Parse(bytes);
        else if (bytes.Length > 1084 && TrackerMagic(bytes, 1080, 4) != "")
            song = ModParser.Parse(bytes);
        else
            throw new FormatException("not a recognized tracker module (expected XM/IT/S3M/MOD signature)");
        return song;
    }

    private static string TrackerMagic(byte[] data, int off, int len) => Encoding.ASCII.GetString(data, off, len) switch
    {
        "M.K." or "M!K!" or "M&K!" or "N.T." or "OKTA" or "CD81" or "FLT4" or "4CHN" or "6CHN" or "8CHN" or "16CN" or "32CN" => "yes",
        _ => "",
    };

    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
        => SettingsSource.Instance.GetSettingsConfig(context);

    public void ApplySettings(PropertyObject settings)
        => SettingsSource.Instance.ApplySettings(settings);

    internal static byte[] ReadAll(System.IO.Stream s)
    {
        if (s is System.IO.MemoryStream ms && ms.TryGetBuffer(out var buf))
            return buf.ToArray();
        using var copy = new System.IO.MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }
}
