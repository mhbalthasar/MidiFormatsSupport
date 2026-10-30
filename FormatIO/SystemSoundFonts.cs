using System.IO;
using System.Runtime.InteropServices;

namespace MidiFormatsSupport;

/// Locates the operating system's default General MIDI sound bank, so the import can name it (and the settings panel can show whether it is present) instead of silently assuming one exists.
public static class SystemSoundFonts
{
    public sealed record SoundBank(string Path, string Description, bool IsPlayableByEngine);

    /// The platform's default GM bank, or null when none was found.
    public static SoundBank? LocateDefaultGmBank()
    {
        foreach (var candidate in Candidates())
        {
            try
            {
                if (File.Exists(candidate.Path))
                    return candidate;
            }
            catch { /* unreadable root / permission denied: try the next one */ }
        }
        return null;
    }

    private static IEnumerable<SoundBank> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return new(Path.Combine(windir, "System32", "drivers", "gm.dls"),
                "Windows Microsoft GS wavetable (DLS)", false);
            yield return new(Path.Combine(windir, "SysWOW64", "Drivers", "gm.dls"),
                "Windows Microsoft GS wavetable, 32-bit (DLS)", false);
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return new("/System/Library/Components/CoreAudio.component/Contents/SharedSupport/gs_instruments.dls",
                "macOS GS instruments (DLS)", false);
            yield return new("/Library/Audio/Sounds/Banks/gs_instruments.dls",
                "macOS GS instruments (DLS)", false);
        }
        else
        {
            yield return new("/usr/share/soundfonts/fluidr3_GM.sf2", "FluidR3 GM (SF2)", true);
            yield return new("/usr/share/sounds/sf2/fluidr3_GM.sf2", "FluidR3 GM (SF2)", true);
            yield return new("/usr/share/soundfonts/FluidR3_GM.sf2", "FluidR3 GM (SF2)", true);
            yield return new("/usr/share/sounds/sf2/FluidR3_GM.sf2", "FluidR3 GM (SF2)", true);
            yield return new("/usr/share/soundfonts/default.sf2", "System default (SF2)", true);
        }
    }

    /// One-line status for the settings panel / the log.
    public static string Describe()
    {
        var bank = LocateDefaultGmBank();
        if (bank is null)
            return $"no default GM bank found for {RuntimeInformation.OSDescription}";
        return $"{bank.Description}: {bank.Path}"
             + (bank.IsPlayableByEngine ? " (SF2, directly playable)" : " (DLS — convert to SF2 to use with the soft-font engine)");
    }
}
