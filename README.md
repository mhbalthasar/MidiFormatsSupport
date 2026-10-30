# MidiFormatsSupport

A TuneLab `format` extension for DAW-mode multi-track import of MIDI-based
formats, score notation and tracker modules, plus MID/KAR/MusicXML export.

Import splits every source lane into its own track, so an 18-voiced
arrangement arrives as 18 tracks instead of one flattened piano roll.

## Formats

Import:

| Suffixes | Format |
| --- | --- |
| `mid`, `midi`, `kar`, `rmi`, `xf` | Standard MIDI File, plus RIFF-wrapped (`rmi`) and XF |
| `musicxml`, `xml`, `mxl` | MusicXML (compressed `.mxl` included) |
| `abc` | ABC notation |
| `mml` | Music Macro Language |
| `mod`, `xm`, `it`, `s3m`, `mptm` | Tracker modules |
| `rpp` | REAPER project (one track per REAPER track; its MIDI items merged) |
| `als` | Ableton Live Set (gzip auto-decompressed; one track per MIDI track) |
| `tracktionedit`, `tracktion` | Tracktion/Waveform edit (one track per track; its MIDI clips merged) |
| `xrns` | Renoise song (one track per sequencer track; patterns unrolled along the arrangement) |
| `mmp` | LMMS project (one track per track; Beat/Bassline clips looped to fill) |
| `rg` | Rosegarden project (gzip auto-decompressed; one track per track) |
| `ardour` | Ardour session (one track per MIDI route; notes read from its companion `.mid` sources) |
| `qtr` | Qtractor session (one track per MIDI track; notes read from its companion `.mid` files) |
| `dawproject` | DAWproject bundle (open standard; one track per arrangement track, MIDI notes only) |
| `flp` | FL Studio project (one track per channel/instrument; playlist clips unrolled) |

Export: `mid`, `midi` / `kar` / `musicxml`, `mxl`.

## Layout

```
MidiFormatsSupport.csproj   plugin project (at the repo root, like other TuneLab extensions)
FormatIO/                   plugin layer sources: format facades, project builder, writers, settings
manifest.json               extension manifest (18 format entries)
Introduction.md             in-app extension description
MidiCore/                   pure parser library, no TuneLab dependency, independently testable
  Parsers/                  one parser per format family
DawCore/                    DAW project-format library, reuses MidiCore's Song model, no TuneLab dependency
  Parsers/                  RPP chunk tree, tempo map, REAPER/Ableton/Tracktion/Renoise/LMMS/Rosegarden/Ardour/Qtractor readers, DAWproject and FL Studio parsers
TestHarness/                local self-test runner (not distributed, git-ignored)
tools/pack.ps1              build + package (+ optional install) script
packages/                   packaged output: packages/MidiFormatsSupport/ and packages/MidiFormatsSupport.tlx
```

## Build

Requirements: .NET 8 SDK and a checkout that contains `TuneLab.SDK` and
`TuneLab.Foundation`. The default location is `..\TunelabExpermental`;
override it with `TUNELAB_SDK_ROOT` or `-p:TuneLabSdkRoot=...`.

```powershell
dotnet build .\MidiFormatsSupport.csproj -c Release
# or, with a different SDK checkout:
dotnet build .\MidiFormatsSupport.csproj -c Release -p:TuneLabSdkRoot=..\TunelabExpermental
```

The plugin references `TuneLab.SDK.dll` / `TuneLab.Foundation.dll` with
`Private=false`; they are provided by the host and are not copied into the
package.

## Package and install

```powershell
# build + package into packages/
.\tools\pack.ps1

# build + package + copy into %AppData%\TuneLab\Extensions\MidiFormatsSupport
# (close TuneLab first: it locks the extension DLLs)
.\tools\pack.ps1 -Install
```

## Instrument matching

The host exposes no "list installed instrument engines" API to plugins, so the
plugin cannot auto-discover engines. Configure one `(engine type, source id)`
pair per instrument family in the extension settings. Each lane is assigned to
the family its GM program number classifies into; anything unconfigured falls
back to the `Default` family, and finally to the `TuneLabSoundFonts` engine's
default source — never to an empty, silent part.

Part and track names read `<GM/GS instrument> [<bank>_<program>]`, e.g.
`String Ensemble 1 [0_48]`. A program number that cannot be resolved falls back
to the piano name while keeping the numbers the file actually carried.

DAW projects carry no GM program, so tracks are matched by name: a REAPER track
called "Bass" (or a `VSTi: ...` instrument name), an Ableton/Tracktion track or
VST plugin name, a Renoise sequencer-track name, or an LMMS/Rosegarden/Ardour
track name is mapped to the nearest GM program, and a name containing
"drum"/"kit"/"snare" is treated as a drum lane. Qtractor prefers the track's own
MIDI program when it has one. A DAWproject track is matched by its track or
instrument-device name; an FL Studio channel is matched by its channel or
generator name. The matched program picks the same per-family
source as any other format.

## Pitch bend, controllers and dynamics

Note-on velocity becomes the note's `velocity` property (0–127), which
velocity-aware engines — the bundled wavetable instrument among them — read
directly, instead of falling back to a flat default.

Continuous MIDI data is imported as part automations under a stable id
convention, so any engine that declares the same track ids picks the curves up:

| id | source |
| --- | --- |
| `pitchbend` | pitch wheel, scaled by the RPN 0 bend range (value axis = semitones) |
| `mod` `vol` `pan` `expr` `sustain` `resonance` `attack` `release` `brightness` `vibrato_rate` `vibrato_depth` `portamento` `portamento_on` | the matching CC (value axis = 0–127) |
| `ccN` | any other CC number N (0–127) |
| `aftertouch` | channel pressure (0–127) |

Pitch bend is also mirrored into the dedicated Pitch line (absolute pitch =
note pitch + the bend in force), which voice engines and the editor read.

Curves are extracted from the Standard MIDI family (`mid`/`midi`/`kar`/`rmi`/
`xf`) and from REAPER `.rpp` MIDI items. The wavetable instrument declares and
consumes `pitchbend`, `mod`, `vol`, `expr` and `sustain`; an engine
that declares fewer (or none) simply ignores the extra curve data, which stays
in the project unharmed. Other DAW/score/tracker formats currently import notes
(and, where present, velocity) only.

## Notes on external data

Ardour (`.ardour`) and Qtractor (`.qtr`) sessions keep their note data in
companion `.mid` files, not in the project XML. The plugin resolves those files
relative to the session, so it must receive a real file stream (the host opens
the file from disk); a plain in-memory stream raises a clear error. Audio tracks,
devices and automation in these sessions are ignored.

DAWproject (`.dawproject`) is a ZIP bundle; only the MIDI notes of the
`Arrangement` are imported (audio clips, scenes/clip-slots and automation are
dropped). FL Studio (`.flp`) is a binary event stream: notes are placed along the
playlist; a pattern-mode project with no playlist blocks is laid out pattern by
pattern. A zero-length FL note holds until the next one in the same channel.

## Self-test

```powershell
dotnet run --project .\TestHarness\SelfTest.csproj
```
