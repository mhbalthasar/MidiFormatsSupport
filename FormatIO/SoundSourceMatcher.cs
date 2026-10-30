using MidiCore;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Resolves a source lane's instrument to a TuneLab sound source.
public static class SoundSourceMatcher
{
    /// Engine type used when the user fills a source id but leaves the engine type blank.
    public const string DefaultEngineType = "TuneLabSoundFonts";

    public static SoundSourceInfo Resolve(Lane lane, Dictionary<string, (string Type, string Id)> familySources,
        Action<string>? log = null)
    {
        string family = Classify(lane);

        // 1. exact family match
        if (Try(familySources, family, out var hit))
            return hit;

        // 2. a configured catch-all beats a silent part
        if (family != Families.Default && Try(familySources, Families.Default, out hit))
            return hit;

        // 3. nothing configured: still name the engine, so the host resolves its default source (a piano for the soft-font engine) instead of leaving the part unassigned.
        log?.Invoke($"[MidiFormatsSupport] no source configured for family '{family}' (program {lane.Program}); " +
                    $"binding to the '{DefaultEngineType}' engine's default source");
        return new SoundSourceInfo { Kind = SourceKind.Instrument, Type = DefaultEngineType, Id = "" };
    }

    private static bool Try(Dictionary<string, (string Type, string Id)> sources, string family,
        out SoundSourceInfo info)
    {
        info = null!;
        if (sources is null || !sources.TryGetValue(family, out var pair))
            return false;
        if (string.IsNullOrWhiteSpace(pair.Id))
            return false;
        info = new SoundSourceInfo
        {
            Kind = SourceKind.Instrument,
            Type = string.IsNullOrWhiteSpace(pair.Type) ? DefaultEngineType : pair.Type,
            Id = pair.Id,
        };
        return true;
    }

    /// Which family a lane belongs to: drum lanes, otherwise the GM program's family.
    public static string Classify(Lane lane)
    {
        if (lane.IsDrum) return Families.Drums;
        return GmInstruments.FamilyOf(lane.Program);
    }
}
