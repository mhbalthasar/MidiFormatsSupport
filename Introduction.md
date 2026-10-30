# MidiFormatsSupport

DAW 风格的「打谱/音轨格式」导入，及 MIDI 系导出。与内建 MIDI 导入的唯一区别：
**内建导入把每个 MIDI 轨的音符合并进一个 MidiPart（丢掉通道），本插件按声部拆轨。**

## 导入

| 后缀 | 格式 |
| --- | --- |
| `.mid` `.midi` | Standard MIDI File，每条 MIDI 通道一条轨 |
| `.kar` | 带歌词的 SMF，Lyric 落到音符 |
| `.rmi` `.xf` | RIFF 容器，拆包取内嵌 SMF |
| `.musicxml` `.xml` `.mxl` | MusicXML，每个 `<part>` 一条轨；`.mxl` 自动解压 |
| `.abc` | ABC 记谱，每个 `V:` 声部一条轨 |
| `.mml` | Music Macro Language，每个通道一条轨 |
| `.mod` `.xm` `.it` `.s3m` `.mptm` | Tracker 模块，每个卷轨通道一条轨 |
| `.rpp` | REAPER 工程，每条 REAPER 轨一条轨（轨内 MIDI item 合并） |
| `.als` | Ableton Live Set（自动解压 gzip），每条 MIDI 轨一条轨（轨内 clip 合并） |
| `.tracktionedit` `.tracktion` | Tracktion/Waveform 工程，每条轨一条轨（轨内 MIDI clip 合并） |
| `.xrns` | Renoise 歌曲，每条音序器轨一条轨（pattern 沿编排展开） |
| `.mmp` | LMMS 工程，每条轨一条轨（Beat/Bassline 循环展开） |
| `.rg` | Rosegarden 工程（自动解压 gzip），每条轨一条轨 |
| `.ardour` | Ardour 会话，每条 MIDI 轨一条轨（音符取自随附 `.mid`） |
| `.qtr` | Qtractor 会话，每条 MIDI 轨一条轨（音符取自随附 `.mid`） |
| `.dawproject` | DAWproject 工程（开放标准），每条编排轨一条轨，仅取 MIDI 音符 |
| `.flp` | FL Studio 工程，每个通道/乐器一条轨（playlist clip 展开） |

## 导出

`.mid` `.midi`（SMF format 1，首轨为 conductor）、`.kar`（附加歌词）、`.musicxml` `.mxl`。

## DAW 模式

解析器先把文件归约为与格式无关的 `Song`（若干 `Lane`，含通道、GM 音色号、音符），
`ProjectBuilder` 再把每条 lane 变成一条 TuneLab 轨（设置里可改为「按乐器合并成一条轨」）。
DAW 工程格式的解析在独立的 `DawCore`（RPP chunk 树、秒↔tick 速度表、轨名→GM 猜测、
各 DAW 的 XML 解析器），与纯 MIDI/tracker 的 `MidiCore` 分离。

## 乐器匹配（就近匹配）

宿主未向插件暴露「枚举已装乐器引擎」的 API，插件无法自动发现音源。请在
**设置 → 扩展 → MidiFormatsSupport** 里为每个乐器族填 (引擎 Type, 音源 Id)：
按 GM 音色号就近套用；没填的族用 **Default** 兜底；再没填则绑定到软音源引擎
`TuneLabSoundFonts` 的默认音源——**绝不会留空**（空 SoundSourceId 的 part 不出声）。

设置面板顶部的 **System GM bank** 显示本机 OS 默认 GM 音色库路径：
Windows `gm.dls`、macOS `gs_instruments.dls`、Linux `fluidr3_GM.sf2`。
其中只有 SF2 可被软音源引擎直接播放，DLS 需先转换。

## 命名

part/track 名统一为 `<GM/GS 乐器名> [<bank>_<program>]`（如 `Violin [0_40]`）。
bank/program 取自 CC0/CC32 与 Program Change；未发 Program Change 的通道按 GM 规范取
program 0（钢琴）；乐器号无法解析时回落钢琴名但保留原数字。没有 GM 概念的格式
（tracker/ABC/MML）用来源自带的声部名，都没有时回落 `<乐器名> <序号>`。
REAPER 轨没有 GM 号，按轨名/`VSTi:` 乐器名就近匹配 GM 音色（含 "drum/kit/snare" 判为鼓）。
Ableton/Tracktion 按轨名或 VST 插件名就近匹配，Renoise 按音序器轨名就近匹配；
LMMS/Rosegarden 按轨名匹配，Ardour 按 MIDI 轨名匹配，Qtractor 优先用轨自带的 MIDI program。

## 力度 / 弯音 / 控制器

note-on 力度写进音符的 `velocity` 属性（0–127），带力度概念的乐器引擎（含本机 wavetable
乐器）直接使用，不再一律取默认力度。

连续 MIDI 数据导入为 part automation，轨 id 使用固定约定，声明了同名轨的引擎即可直接消费：

- `pitchbend`：弯音轮，按 RPN 0 弯音范围换算为半音；
- `mod` `vol` `pan` `expr` `sustain` `resonance` `attack` `release` `brightness`
  `vibrato_rate` `vibrato_depth` `portamento` `portamento_on`：对应 CC（0–127）；
- `ccN`：其余 CC 号 N（0–127）；
- `aftertouch`：通道触后（0–127）。

弯音同时写入独立的 Pitch 曲线（绝对音高 = 音符音高 + 当前弯音），供 voice 引擎与编辑器使用。

目前从 SMF 系（`mid`/`midi`/`kar`/`rmi`/`xf`）与 REAPER `.rpp` 的 MIDI item 提取曲线；
wavetable 乐器声明并消费 `pitchbend`/`mod`/`vol`/`expr`/`sustain`。引擎没声明的轨只是
不显示、不消费，数据仍留在工程里；其余 DAW/记谱/tracker 格式目前只导入音符（及力度）。

## 已知边界

- Tracker 的「乐器」是采样回放而非 GM 音色，拆出的轨无法还原音色。
- 导出会丢失 TuneLab 侧的音源/效果/自动化/滑音。
- `.mod` 仅支持标准 magic 的 31 采样经典布局。
- `.rpp` 只导入 MIDI item（含音符力度与 CC/弯音曲线）；音频轨、VST 音色、轨包络一律丢弃，
  速度自动化按 TEMPOENVEX 尽力还原（非 0 锚定时回落基础速度）。
- `.als` 只取编曲视图的 MIDI clip；会话视图 clip（`Time` 为哨兵值）跳过，音频轨/设备/自动化丢弃。
- `.tracktionedit` 只取 MIDI clip；插件状态里的非标准 `base64:` 属性会被重命名后解析。
- `.xrns` 按 pattern 顺序展开（兼容新旧两种 `PatternSequence` 布局），音符时值取到同列下一个事件；
  采样乐器不还原为 GM。
- `.mmp` 采用 Beat/Bassline 循环语义展开（负时值音符延续到 pattern 末尾）；音频/采样轨不还原为 GM。
- `.rg` 按 segment 携带的轨号归轨；链接 segment（linkerid 相同）在各轨分别保留，故同段音乐可能出现多次。
- `.ardour` 与 `.qtr` 的音符存放在会话目录旁的 `.mid` 里，**必须从磁盘打开**（宿主需把文件流
  以 `FileStream` 形式传入）；直接喂内存流会报错。工程内的音频轨、设备、自动化一律丢弃。
- `.dawproject` 是 ZIP 包，只取 `Arrangement` 的 MIDI 音符；音频 clip、会话视图槽位、自动化丢弃。
- `.flp` 为二进制事件流，按 playlist 放置音符；纯 pattern 工程按 pattern 顺序铺开；零时值音符延续到同通道下一个音符。
- 仿真类格式（NSF/GBS/SPC/VGM/VGZ）不实现。
