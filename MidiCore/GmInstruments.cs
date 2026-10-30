namespace MidiCore;

/// General MIDI program/drum names, English + Chinese.
public static class GmInstruments
{
    // 128 GM instrument programs, index = program number.
    public static readonly string[] Names =
    {
        "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano",
        "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavinet",
        "Celesta", "Glockenspiel", "Music Box", "Vibraphone",
        "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
        "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ",
        "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
        "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)", "Electric Guitar (clean)",
        "Electric Guitar (muted)", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics",
        "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass",
        "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
        "Violin", "Viola", "Cello", "Contrabass",
        "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
        "String Ensemble 1", "String Ensemble 2", "Synth Strings 1", "Synth Strings 2",
        "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
        "Trumpet", "Trombone", "Tuba", "Muted Trumpet",
        "French Horn", "Brass Section", "Synth Brass 1", "Synth Brass 2",
        "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax",
        "Oboe", "English Horn", "Bassoon", "Clarinet",
        "Piccolo", "Flute", "Recorder", "Pan Flute",
        "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
        "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)",
        "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass+lead)",
        "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)",
        "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
        "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)",
        "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
        "Sitar", "Banjo", "Shamisen", "Koto",
        "Kalimba", "Bagpipe", "Fiddle", "Shanai",
        "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock",
        "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
        "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet",
        "Telephone Ring", "Helicopter", "Applause", "Gunshot"
    };

    // 128 GM instrument programs, Chinese names, index = program number.
    public static readonly string[] NamesZh =
    {
        "大钢琴", "明亮钢琴", "电子大钢琴", "酒吧钢琴",
        "电钢琴1", "电钢琴2", "拨弦古钢琴", "克拉维内尔",
        "钢片琴", "钟琴", "八音盒", "颤音琴",
        "马林巴", "木琴", "管钟", "扬琴",
        "拉杆风琴", "打击式风琴", "摇滚风琴", "教堂管风琴",
        "簧风琴", "手风琴", "口琴", "探戈手风琴",
        "尼龙吉他", "钢弦吉他", "爵士电吉他", "清音电吉他",
        "闷音电吉他", "过载吉他", "失真吉他", "吉他泛音",
        "原声贝斯", "指弹电贝斯", "拨片电贝斯", "无品贝斯",
        "拍弦贝斯1", "拍弦贝斯2", "合成贝斯1", "合成贝斯2",
        "小提琴", "中提琴", "大提琴", "低音提琴",
        "弦乐颤音", "拨奏弦乐", "管弦乐竖琴", "定音鼓",
        "弦乐合奏1", "弦乐合奏2", "合成弦乐1", "合成弦乐2",
        "合唱啊", "人声咕", "合成人声", "管弦乐打击",
        "小号", "长号", "大号", "弱音小号",
        "圆号", "铜管组", "合成铜管1", "合成铜管2",
        "高音萨克斯", "中音萨克斯", "次中音萨克斯", "上低音萨克斯",
        "双簧管", "英国管", "巴松", "单簧管",
        "短笛", "长笛", "竖笛", "排箫",
        "吹瓶", "尺八", "口哨", "奥卡里那笛",
        "主音1(方波)", "主音2(锯齿波)", "主音3(蒸汽风琴)", "主音4(奇夫)",
        "主音5(查让)", "主音6(人声)", "主音7(五度)", "主音8(贝斯+主音)",
        "音色垫1(新时代)", "音色垫2(温暖)", "音色垫3(复合成)", "音色垫4(合唱)",
        "音色垫5(弓弦)", "音色垫6(金属)", "音色垫7(光环)", "音色垫8(扫弦)",
        "特效1(雨)", "特效2(电影配乐)", "特效3(水晶)", "特效4(大气)",
        "特效5(明亮)", "特效6(小妖)", "特效7(回声)", "特效8(科幻)",
        "锡塔尔", "班卓", "三味线", "日本筝",
        "卡林巴", "风笛", "提琴", "唢呐",
        "叮当铃", "阿戈戈", "钢鼓", "木鱼",
        "太鼓", "旋律嗵鼓", "合成嗵鼓", "反向镲片",
        "吉他煞车噪声", "换气噪声", "海浪", "鸟鸣",
        "电话铃", "直升机", "掌声", "枪声"
    };

    // Channel 10 GM drum-kit key names (pitch 35..81), English + Chinese.
    public static readonly Dictionary<int, string> Drums = new()
    {
        [35] = "Acoustic Bass Drum", [36] = "Bass Drum 1", [37] = "Side Stick", [38] = "Acoustic Snare", [39] = "Hand Clap",
        [40] = "Electric Snare", [41] = "Low Floor Tom", [42] = "Closed Hi-Hat", [43] = "High Floor Tom", [44] = "Pedal Hi-Hat",
        [45] = "Low Tom", [46] = "Open Hi-Hat", [47] = "Low-Mid Tom", [48] = "Hi-Mid Tom", [49] = "Crash Cymbal 1",
        [50] = "High Tom", [51] = "Ride Cymbal 1", [52] = "Chinese Cymbal", [53] = "Ride Bell", [54] = "Tambourine",
        [55] = "Splash Cymbal", [56] = "Cowbell", [57] = "Crash Cymbal 2", [58] = "Vibraslap", [59] = "Ride Cymbal 2",
        [60] = "Hi Bongo", [61] = "Low Bongo", [62] = "Mute Hi Conga", [63] = "Open Hi Conga", [64] = "Low Conga",
        [65] = "High Timbale", [66] = "Low Timbale", [67] = "High Agogo", [68] = "Low Agogo", [69] = "Cabasa",
        [70] = "Maracas", [71] = "Short Whistle", [72] = "Long Whistle", [73] = "Short Guiro", [74] = "Long Guiro",
        [75] = "Claves", [76] = "Hi Wood Block", [77] = "Low Wood Block", [78] = "Mute Cuica", [79] = "Open Cuica",
        [80] = "Mute Triangle", [81] = "Open Triangle"
    };

    public static readonly Dictionary<int, string> DrumsZh = new()
    {
        [35] = "大鼓", [36] = "低音鼓1", [37] = "小军鼓边击", [38] = "小军鼓", [39] = "拍手",
        [40] = "电子小军鼓", [41] = "落地鼓", [42] = "闭合踩镲", [43] = "高落地鼓", [44] = "踏击踩镲",
        [45] = "低鼓", [46] = "开镲", [47] = "中低鼓", [48] = "中高鼓", [49] = "碎音镲1",
        [50] = "高鼓", [51] = "叮叮镲", [52] = "中国镲", [53] = "镲钟", [54] = "铃鼓",
        [55] = "溅音镲", [56] = "牛铃", [57] = "碎音镲2", [58] = "振鸣板", [59] = "叮叮镲2",
        [60] = "高邦戈", [61] = "低邦戈", [62] = "闷音高康加", [63] = "开音高康加", [64] = "低康加",
        [65] = "高定音鼓", [66] = "低定音鼓", [67] = "高阿戈戈", [68] = "低阿戈戈", [69] = "卡巴沙",
        [70] = "沙锤", [71] = "短哨", [72] = "长哨", [73] = "短刮壶", [74] = "长刮壶",
        [75] = "响棒", [76] = "高木鱼", [77] = "低木鱼", [78] = "闷音库加", [79] = "开音库加",
        [80] = "闷音三角铁", [81] = "开音三角铁"
    };

    /// Family classification (one of Families) for a GM program number.
    public static string FamilyOf(int program)
    {
        if (program < 0) return Families.Unknown;
        int p = Math.Clamp(program, 0, 127);
        return p switch
        {
            <= 23 => Families.Keys,          // pianos, chromatic percussion, organs, accordion
            <= 31 => Families.Guitar,
            <= 39 => Families.Bass,
            <= 51 => Families.Strings,       // violin..timpani, choir/pads 52..54 sit inside GM's string group
            <= 55 => Families.Ensemble,
            <= 63 => Families.Brass,
            <= 71 => Families.Reed,          // soprano/alto/tenor/bari sax + oboe/eh/bassoon/clarinet
            <= 79 => Families.Flute,         // piccolo..ocarina
            <= 87 => Families.SynthLead,
            <= 95 => Families.SynthPad,      // pad 1..8
            >= 104 and <= 119 => Families.Ethnic, // sitar..shanai sits between pads and effects
            <= 103 => Families.Effects,      // FX
            _ => Families.Effects
        };
    }


    /// English GM program name for a program number, falling back to Acoustic Grand Piano when the number is out of range.
    public static string NameOf(int program) =>
        program >= 0 && program < Names.Length ? Names[program] : Names[0];

    /// Chinese GM program name for a program number, falling back to 大钢琴.
    public static string NameZhOf(int program) =>
        program >= 0 && program < NamesZh.Length ? NamesZh[program] : NamesZh[0];

    /// GM/GS drum-kit key name for a channel-10 pitch, or null when unmapped.
    public static string? DrumName(int pitch) => Drums.TryGetValue(pitch, out var n) ? n : null;

    public static string? DrumNameZh(int pitch) => DrumsZh.TryGetValue(pitch, out var n) ? n : null;

    /// Formats a part name as "<GM/GS instrument> [<bank>_<program>]", e.g. Acoustic Grand Piano [0_0].
    public static string PartName(int bank, int program, string? sourceName = null)
    {
        // A name that already carries its own "[bank_program]" tag (this plugin's own output, re-imported) is taken verbatim — decorating it again would read "Violin [0_40] [0_40]".
        if (sourceName is { Length: > 0 } s && AlreadyTagged(s))
            return s.Trim();
        string instrument = sourceName is { Length: > 0 } s2 && !LooksGeneric(s2) ? s2 : NameOf(program);
        return $"{instrument} [{bank}_{program}]";
    }

    /// True when the name already ends with a "[bank_program]" tag.
    private static bool AlreadyTagged(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name.Trim(), @"\[\s*-?\d+\s*_\s*-?\d+\s*\]$");

    /// Same as PartName but with the Chinese instrument name.
    public static string PartNameZh(int bank, int program, string? sourceName = null)
    {
        string instrument = sourceName is { Length: > 0 } s && !LooksGeneric(s) ? s : NameZhOf(program);
        return $"{instrument} [{bank}_{program}]";
    }

    /// True when a source-provided name carries no information ("Channel 3", "Track 2", "Part 1", "P1" ...).
    public static bool LooksGeneric(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string trimmed = name.Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed,
            @"^(?:(?:midi|mml)\s+)?(?:channel|ch|track|part|voice|staff|p)\s*#?\d+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return true;
        // a name this plugin already decorated ("Violin [0_40]") is not new information: re-importing an exported file would otherwise stack a second "[bank_program]".
        return System.Text.RegularExpressions.Regex.IsMatch(trimmed,
            @"^.+\s*\[\s*-?\d+\s*_\s*-?\d+\s*\]$");
    }
}

/// Coarse instrument families used for user-configured sound source mapping.
public static class Families
{
    public const string Keys = "Keys";          // piano, organ, chromatic percussion
    public const string Guitar = "Guitar";
    public const string Bass = "Bass";
    public const string Strings = "Strings";
    public const string Ensemble = "Ensemble";
    public const string Brass = "Brass";
    public const string Reed = "Reed";
    public const string Flute = "Flute";
    public const string SynthLead = "SynthLead";
    public const string SynthPad = "SynthPad";
    public const string Pad = "Pad";
    public const string Ethnic = "Ethnic";
    public const string Percussive = "Percussive";
    public const string Drums = "Drums";
    public const string Choir = "Choir";
    public const string Effects = "Effects";
    public const string Unknown = "Unknown";
    /// Catch-all family: whatever the user configures here is used for any lane whose own family has no source configured.
    public const string Default = "Default";


}
