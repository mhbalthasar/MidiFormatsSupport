namespace MidiFormatsSupport;

/// Maps MIDI CC numbers to the stable automation-track ids this plugin writes, so an engine that
/// declares the same ids (e.g. the bundled wavetable instrument) picks the curves up directly.
/// Common controllers get a readable alias; everything else falls back to "ccN".
internal static class ControllerMap
{
    /// Pitch-bend track id; the value axis is semitones.
    public const string PitchBend = "pitchbend";

    /// Channel-pressure (aftertouch) track id; the value axis is 0-127.
    public const string Aftertouch = "aftertouch";

    public static string Id(int cc) => cc switch
    {
        1 => "mod",
        7 => "vol",
        10 => "pan",
        11 => "expr",
        64 => "sustain",
        65 => "portamento_on",
        71 => "resonance",
        72 => "release",
        73 => "attack",
        74 => "brightness",
        76 => "vibrato_rate",
        77 => "vibrato_depth",
        84 => "portamento",
        _ => "cc" + cc,
    };

    /// Baseline used where an imported curve has no point (the GM defaults, so untouched spans sound right).
    public static double DefaultValue(int cc) => cc switch
    {
        7 => 100,   // channel volume
        10 => 64,   // pan center
        11 => 127,  // expression
        71 or 72 or 73 or 74 or 75 or 76 or 77 or 78 or 79 => 64,
        _ => 0,
    };
}
