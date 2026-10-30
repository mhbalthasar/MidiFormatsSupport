namespace DawCore;

/// Best-effort GM instrument guess from a free-form track/instrument name (就近匹配).
public static class InstrumentGuesser
{
    /// Drums first: a name that says "drum"/"kit"/"snare" must not be read as a pitched instrument.
    private static readonly string[] _drumWords =
    {
        "drum", "kit", "percussion", "perc", "snare", "kick", "hihat", "hi-hat", "hi hat", "cymbal", "crash",
        "ride", "tom", "clap", "battery", "addictive", "superior", "ezdrummer", "bfd", "groove agent", "beatbox",
    };

    /// Substring -> GM program, most specific first (e.g. "electric piano" must beat "piano").
    private static readonly (string Key, int Program)[] _map =
    {
        ("synth bass", 38), ("sub bass", 38), ("contrabass", 43), ("double bass", 43), ("bass", 32),
        ("electric piano", 4), ("e.piano", 4), ("rhodes", 4), ("mark i", 4), ("wurlitzer", 4), ("wurli", 4),
        ("grand", 0), ("piano", 0), ("keys", 0), ("harpsichord", 6), ("clav", 7),
        ("organ", 16), ("accordion", 21), ("harmonica", 22),
        ("nylon", 24), ("acoustic guitar", 24), ("steel guitar", 25), ("clean", 27), ("muted", 28),
        ("overdriven", 29), ("distort", 30), ("dist", 30), ("guitar", 24),
        ("violin", 40), ("viola", 41), ("cello", 42),
        ("harp", 46), ("timpani", 47), ("string", 48), ("ensemble", 48), ("orchestra", 48),
        ("choir", 52), ("vox", 52), ("vocal", 53), ("voice", 53),
        ("trumpet", 56), ("trombone", 57), ("tuba", 58), ("french horn", 60), ("horn", 60), ("brass", 61),
        ("sax", 65), ("oboe", 68), ("english horn", 69), ("bassoon", 70), ("clarinet", 71),
        ("piccolo", 72), ("flute", 73), ("recorder", 74), ("pan flute", 75), ("whistle", 78), ("ocarina", 79),
        ("lead", 80), ("pad", 88), ("square", 80), ("saw", 81), ("synth", 81),
        ("sitar", 104), ("banjo", 105), ("shamisen", 106), ("koto", 107), ("kalimba", 108),
        ("bagpipe", 109), ("fiddle", 110), ("shanai", 111),
        ("bell", 112), ("glocken", 112), ("steel drum", 114), ("woodblock", 115), ("taiko", 116), ("tom", 117),
        ("effect", 96), ("fx", 96), ("atmosphere", 99), ("rain", 96),
    };

    /// Guesses (GM program, is-drum) from any of the given names; (-1, false) when nothing matches.
    public static (int Program, bool IsDrum) Guess(params string?[] names)
    {
        string hay = string.Join(' ', names.Where(n => !string.IsNullOrWhiteSpace(n))).ToLowerInvariant();
        if (hay.Length == 0) return (-1, false);

        foreach (var w in _drumWords)
            if (hay.Contains(w, StringComparison.Ordinal))
                return (-1, true);

        foreach (var (key, program) in _map)
            if (hay.Contains(key, StringComparison.Ordinal))
                return (program, false);

        return (-1, false);
    }
}
