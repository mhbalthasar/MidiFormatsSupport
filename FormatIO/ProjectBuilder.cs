using MidiCore;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Bridge from the format-agnostic Song to TuneLab's ProjectInfo.
public static class ProjectBuilder
{
    public sealed class Options
    {
        /// false = collapse all lanes of one family into a single track (TuneLab default), true = every lane becomes its own track.
        public bool DawMode { get; set; } = true;

        /// Drop lanes that hold no notes.
        public bool SkipEmptyLanes { get; set; } = true;

        /// Merge lanes that only differ by MIDI channel but share the same program (common in files that split one instrument across channels).
        public bool MergeSameProgram { get; set; }

        public Dictionary<string, (string Type, string Id)> Sources { get; set; } = new();

        /// Called for diagnostics; never throws.
        public Action<string>? Log { get; set; }
    }

    internal const double TicksPerQuarterNote = 480;   // TuneLab's PPQ (MusicTheory.RESOLUTION)

    public static ProjectInfo Build(Song song, Options options)
    {
        var project = new ProjectInfo();

        // tempo map
        foreach (var t in song.Tempos.OrderBy(t => t.Tick))
        {
            double pos = t.Tick;
            double bpm = t.Bpm is > 0 and < 1000 ? t.Bpm : 120;
            if (project.Tempos.Count == 0 || Math.Abs(project.Tempos[^1].Bpm - bpm) > 0.01)
                project.Tempos.Add(new TempoInfo { Pos = pos, Bpm = bpm });
        }
        if (project.Tempos.Count == 0)
            project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });

        // time signatures (dedupe, keep at most the first per bar)
        var seenBars = new HashSet<int>();
        foreach (var ts in song.TimeSignatures.OrderBy(t => t.BarIndex))
        {
            if (!seenBars.Add(ts.BarIndex)) continue;
            project.TimeSignatures.Add(new TimeSignatureInfo
            {
                BarIndex = Math.Max(0, ts.BarIndex),
                Numerator = Math.Clamp(ts.Numerator, 1, 64),
                Denominator = Math.Clamp(ts.Denominator, 1, 64),
            });
        }

        double ppqScale = 480.0 / Math.Max(1, song.TicksPerQuarter);

        // --- group lanes into target tracks ---
        var groups = new List<List<Lane>>();
        if (options.DawMode)
        {
            foreach (var lane in song.Lanes)
                if (!options.SkipEmptyLanes || lane.Notes.Count > 0)
                    groups.Add(new List<Lane> { lane });
        }
        else
        {
            var byProgram = new Dictionary<int, List<Lane>>();
            foreach (var lane in song.Lanes)
            {
                if (options.SkipEmptyLanes && lane.Notes.Count == 0) continue;
                int key = lane.Program >= 0 ? lane.Program : -1;
                if (!byProgram.TryGetValue(key, out var list)) byProgram[key] = list = new();
                list.Add(lane);
            }
            groups.AddRange(byProgram.Values);
        }

        int unnamedCount = 0;
        foreach (var group in groups)
        {
            var track = new TrackInfo();
            string name = MakeTrackName(group, ref unnamedCount);
            track.Name = name;

            foreach (var lane in group)
            {
                if (lane.Notes.Count == 0) continue;

                // The part name is where the instrument identity lives: "<GM instrument> [bank_program]".
                string partName = lane.Program >= 0
                    ? GmInstruments.PartName(lane.Bank, lane.Program, PreferHint(lane))
                    : name;

                // Anchor the part at its earliest event (note or curve point) so every stored position
                // is a non-negative offset from PartInfo.Pos — the host treats note/pitch/automation
                // positions as relative to that anchor, never as global ticks.
                double anchorTick = double.MaxValue, endTick = 0;
                void Include(double tick, double span)
                {
                    anchorTick = Math.Min(anchorTick, tick);
                    endTick = Math.Max(endTick, tick + span);
                }

                foreach (var n in lane.Notes)
                    Include(n.StartTick, Math.Max(1, n.DurationTicks));
                foreach (var points in lane.Controllers.Values)
                    foreach (var p in points) Include(p.Tick, 0);
                foreach (var p in lane.PitchBend) Include(p.Tick, 0);
                foreach (var p in lane.Aftertouch) Include(p.Tick, 0);

                double anchorPos = anchorTick * ppqScale;
                var part = new MidiPartInfo { Name = partName, Pos = anchorPos, StartOffset = 0, EndOffset = 0 };

                foreach (var n in lane.Notes)
                {
                    part.Notes.Add(new NoteInfo
                    {
                        Pos = n.StartTick * ppqScale - anchorPos,
                        Dur = Math.Max(1, n.DurationTicks * ppqScale),
                        Pitch = Math.Clamp(n.Pitch, 0, 127),
                        Lyric = n.Lyric ?? string.Empty,
                        Properties = NoteProperties(n.Velocity),
                    });
                }
                if (part.Notes.Count == 0) continue;

                BuildAutomations(lane, part, ppqScale, anchorPos);
                part.EndOffset = Math.Max(endTick * ppqScale - anchorPos, TicksPerQuarterNote * 4);

                part.SoundSource = SoundSourceMatcher.Resolve(lane, options.Sources, options.Log);
                track.Parts.Add(part);
            }

            if (track.Parts.Count > 0)
                project.Tracks.Add(track);
        }

        options.Log?.Invoke($"[MidiFormatsSupport] {song.SourceFormat} -> {project.Tracks.Count} track(s), " +
                            $"{project.Tracks.Sum(t => t.Parts.OfType<MidiPartInfo>().Sum(p => p.Notes.Count))} note(s)");
        return project;
    }

    /// A lane name worth showing: a real instrument name from the source (track/instrument meta, MusicXML part-name, tracker sample name), otherwise null.
    private static string? PreferHint(Lane lane)
    {
        string hint = (lane.InstrumentNameHint ?? "").Trim();
        if (hint.Length > 0 && !GmInstruments.LooksGeneric(hint)) return hint;
        string own = (lane.Name ?? "").Trim();
        if (own.Length > 0 && !GmInstruments.LooksGeneric(own)) return own;
        return null;
    }

    private static string MakeTrackName(List<Lane> group, ref int unnamed)
    {
        var first = group[0];

        // GM/GS identity present: "<instrument> [bank_program]" is the label.
        if (first.Program >= 0)
            return GmInstruments.PartName(first.Bank, first.Program, PreferHint(first));

        // No GM identity (tracker sample, ABC voice, MML channel): the source's own name when it has one, otherwise the GM fallback instrument with the channel number — a part named after nothing at all is as unhelpful as a silent one.
        string own = PreferHint(first) ?? "";
        if (own.Length > 0) return own;
        unnamed++;
        return $"{GmInstruments.NameZhOf(0)} {unnamed}";
    }

    /// Note-level "velocity" (0-127) as a per-note property, so note-aware engines (the bundled
    /// wavetable instrument among them) honor the imported dynamics instead of a flat default.
    static PropertyObject NoteProperties(int velocity)
    {
        var map = new Map<string, PropertyValue>();
        map.Add("velocity", (double)Math.Clamp(velocity, 1, 127));
        return new PropertyObject(map);
    }

    /// Writes the lane's controller/pitch-bend/aftertouch curves as part automations under the
    /// shared id convention (see ControllerMap) and mirrors pitch bend into the dedicated Pitch line.
    static void BuildAutomations(Lane lane, MidiPartInfo part, double ppqScale, double anchorPos)
    {
        foreach (var (cc, points) in lane.Controllers.OrderBy(kv => kv.Key))
        {
            if (points.Count == 0) continue;
            var info = new AutomationInfo { DefaultValue = ControllerMap.DefaultValue(cc) };
            foreach (var p in points)
                info.Points.Add(new Point(p.Tick * ppqScale - anchorPos, p.Value));
            part.Automations[ControllerMap.Id(cc)] = info;
        }

        if (lane.PitchBend.Count > 0)
        {
            var info = new AutomationInfo { DefaultValue = 0 };
            foreach (var p in lane.PitchBend)
                info.Points.Add(new Point(p.Tick * ppqScale - anchorPos, p.Value));
            part.Automations[ControllerMap.PitchBend] = info;
        }

        if (lane.Aftertouch.Count > 0)
        {
            var info = new AutomationInfo { DefaultValue = 0 };
            foreach (var p in lane.Aftertouch)
                info.Points.Add(new Point(p.Tick * ppqScale - anchorPos, p.Value));
            part.Automations[ControllerMap.Aftertouch] = info;
        }

        BuildPitchCurve(lane, part, ppqScale, anchorPos);
    }

    /// The dedicated Pitch line is absolute pitch (MIDI note number): note pitch plus the bend in
    /// force, sampled across each note. Voice engines and the editor read this; instrument engines
    /// instead read the "pitchbend" automation track.
    static void BuildPitchCurve(Lane lane, MidiPartInfo part, double ppqScale, double anchorPos)
    {
        if (lane.PitchBend.Count == 0) return;

        foreach (var n in lane.Notes)
        {
            double start = n.StartTick;
            double end = n.StartTick + Math.Max(1, n.DurationTicks);

            var ticks = new SortedSet<double> { start, end };
            foreach (var p in lane.PitchBend)
                if (p.Tick > start && p.Tick < end) ticks.Add(p.Tick);

            var segment = new List<Point>();
            bool moved = false;
            foreach (var t in ticks)
            {
                double bend = BendAt(lane.PitchBend, t);
                if (Math.Abs(bend) > 1e-6) moved = true;
                segment.Add(new Point(t * ppqScale - anchorPos, Math.Clamp(n.Pitch + bend, 0, 127)));
            }

            if (moved)
                part.Pitch.Segments.Add(segment);
        }
    }

    static double BendAt(List<CurvePoint> bends, double tick)
    {
        if (bends.Count == 0) return 0;
        if (tick <= bends[0].Tick) return bends[0].Value;
        if (tick >= bends[^1].Tick) return bends[^1].Value;
        for (int i = 1; i < bends.Count; i++)
        {
            if (tick > bends[i].Tick) continue;
            var a = bends[i - 1];
            var b = bends[i];
            double span = b.Tick - a.Tick;
            if (span <= 0) return b.Value;
            return a.Value + (b.Value - a.Value) * (tick - a.Tick) / span;
        }
        return bends[^1].Value;
    }
}