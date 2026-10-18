# 广播语音制作交付

2026-10-18，第 4 版。原始 MP3 位于 `Assets/Audio/Voice`，全部保留，11 条均识别为英文，M06 同样按英文录音剪辑。当前广告与新闻的 7 条完整播报为第 3 版，4 条工厂完整播报为第 4 版，10 条断电文件保留已认可的第 2 版。

## 成品

- `Assets/Audio/Voice/Processed/Full`：11 条完整播报。
- `Assets/Audio/Voice/Processed/PowerCut`：M01—M06 各 1 条，M07 共 4 条，合计 10 条。
- `Audition.html`：全部文件的本地试听索引及实际保留台词，页首提供工厂完整版调整前后的对比。
- `M07_FivePass_Preview.wav`：五遍连续试听，遍次之间留 1 秒间隔，附同名 SRT。
- `M07_FivePass_Preview_v2.wav`、`M04_PowerCut_Preview_v2.wav`：本轮试听快照，便于避免旧音频缓存。
- `Revisions/v1`：第一版音频与清单备份；`Revisions/v2`：本轮调整前的广告、新闻完整版及清单备份。
- `H01_Full_Preview_v3.wav`、`M06_Full_Preview_v3.wav`：第三版广告和新闻的独立试听快照。
- `M04_Full_Preview_v4.wav`：去底噪、略增回音的工厂完整版试听；`Revisions/v3/Full` 保留本轮调整前的工厂完整版。
- `Subtitles`：21 份英文字幕时间轴。`voice_manifest.json` 同时保存字幕片段、原录音取材区间及音量信息。

全部成品为 48 kHz、单声道、24-bit PCM WAV，响度目标 -19 LUFS，真峰值不高于 -1.5 dBTP。附 Unity `.meta`，保持 PCM 导入并关闭额外归一化。当前交付为音频素材，尚未接入录音机运行逻辑、场景或语音参数 SO。

## 音色与剪辑

第 4 版仅修改 Sarah 的 4 条工厂完整版（H02、H03、M03、M04）：移除人工添加的设备底噪，加入 43—237 毫秒间的少量衰减早期反射，回音滤去部分高低频，产生轻微厂房空间感。保留原语速、音高、时长与字幕时间点，并重新统一响度。广告、新闻、全部断电版及已有断电试听文件内容和修改时间均不变，记录见 `revision4_preservation_check.json`。

第 3 版只对 Verity 的广告完整版（H01、M01、M02）及 Sheila 的新闻完整版（H04、M05、M06、M07）增加一层柔暗的旧广播频响：广告低通约 3.05 kHz、新闻约 2.85 kHz，并柔和衰减高频、略增低中频，使声音更闷。音高、语速、字幕时序保持不变；最终仍统一到 -19 LUFS。工厂完整版与所有断电音频的文件内容及修改时间均保持不变，核对记录见 `revision3_preservation_check.json`。

Verity：温和的广告扬声器音色；Sarah：略窄频、带轻微箱体共鸣的厂内扩音；Sheila：稍暗、稳定的新闻收音机音色。第 2 版进一步收柔高频、增加轻微软饱和与设备底噪，体现旧广播质感。完整版保留原音高；断电版降低 1.5 个半音，并额外柔化高频、略增低中频，使声音更沉。降调保持时长和语速，保留原配音表演；空间混响和距离衰减适合由游戏播放环境控制。

断电版从同一录音按原顺序取材。第 2 版将缺失段改成更明显、持续且不规则的电流滋响，叠加电源嗡鸣谐波和下坠尾音，采用较柔和的起落，减少短促敲击感。电流效果加在缺失段，保留词句处保持清楚。被删掉的人声已移除，不在噪声底下继续播放。长缺失段压缩为适量故障停顿，成品不与原始录音保持等长。

电流声在词语边界留有约 45 毫秒缓冲。M07 第 3、4 版将 `Follow the light` 连贯拼接，把这处电流间隔移到前面的句间停顿，防止故障声被误听成单词尾音。

`revision2_comparison.json` 记录新旧版高频及故障段能量对比。以 M04 为例，故障段中部 RMS 从约 -44.1 dBFS 提升到 -26.4 dBFS；完整播报 3.5 kHz 以上的相对能量下降约 3.4 dB。全片仍按 -19 LUFS 统一，增强集中在故障段。

先以本地 Whisper 核对台词，再以本地 Wav2Vec2 CTC 对齐连读边界，重点清除 M07 `take`、`follow` 前的 `will` 尾音，以及 M06 末句前的 `until`。10 条断电文件的独立转写复核均与目标保留词序一致。

## M07 的五遍播放

| 遍次 | 文件 | 删去的原始人声时长 | 保留内容 |
| --- | --- | --- | --- |
| 1 | `M07_PowerCut_01.wav` | 约 64.0% | Take back several robot sets … the key issue is … follow the delivery … light enough for children. |
| 2 | `M07_PowerCut_02.wav` | 约 75.6% | Take back several … the key issue … follow the delivery … light enough. |
| 3 | `M07_PowerCut_03.wav` | 约 87.1% | Take … the key issue … follow the … light. |
| 4 | `M07_PowerCut_04.wav` | 约 89.8% | Take … the key. Follow the … light. |
| 5 | **复用 `M07_PowerCut_04.wav`** | 同上 | 再说一遍最终指令。 |

比例按对齐后的原始人声时长计算，排除原有静音及新增故障音。为保留完整词语，第一遍的约 60% 落在 64.0%。后一版的保留词语严格是前一版的子集，没有调换词序或补录声音。最终文件本身只说一遍指令，由第 4、5 遍各播放一次。

## 检查与复现

`quality_checks.json` 记录文件数、编码、采样率、声道、响度、峰值、原文件哈希和目标台词比对结果。`Analysis` 保留原始转写，`Alignment` 保留精细时间点，`Verification` 保留成品复核转写。

从仓库根目录运行：

```powershell
python -m pip install -r Tools/Audio/requirements.txt
python Tools/Audio/build_voice.py
python Tools/Audio/verify_voice.py
```

仅重做本轮指定的广告、新闻完整版：

```powershell
python Tools/Audio/build_voice.py --full-only --actors Verity Sheila
```

仅重做当前工厂完整版：

```powershell
python Tools/Audio/build_voice.py --full-only --actors Sarah
```

已有时间标记随交付保留，重新制作无需下载识别模型。若替换原始录音，先用 `transcribe_voice.py` 与 `align_voice.py` 重建标记，再复核 `voice_edits.json` 的词索引；识别依赖额外的 faster-whisper，精细对齐依赖 torch/torchaudio。对重新生成的断电文件应重新运行本地转写复核。

## 收音机字幕时间轴

运行时已采用音频播放段加独立字幕时间轴，字幕随实际音频采样位置切换；不会为了换字幕而切割音频。旧整段字幕配置仍兼容。`Assets/Audio/Voice/Sequences` 中有 11 份初见及 7 份返程序列，中文默认显示，英文也已配置。

M07 返程序列有五个播放段，段间隔 1 秒，第 4、5 段共用同一个最终断电音频及译文“拿起钥匙。跟着光走。”。前三遍保留残句语意，不提前把“关键问题”翻成“钥匙”。H01—H04 仅提供初见配置。

按编号将 SO 赋给 `RadioReceiver.FirstEncounter` / `ReturnJourney`；没有自动替换现有关卡引用。字段与播放规则见 `Assets/C# Scripts/World/Radio/README.md`。中文分节源为 `Tools/Audio/radio_subtitles_zh.json`，运行 `python Tools/Audio/configure_radio_sequences.py` 重建命名 SO，并输出 `radio_sequence_catalog.json`；21 个音频文件保持原样。

`RadioTimelineValidation.txt` 保存独立 Unity 项目中的验证结果：真实资源导入、时间轴边界和回退、音频采样时钟、暂停与慢动作、语言切换、字幕空隙、M07 五遍中文播放，以及原收音机触发、距离、阶段切换和旧字幕回归均通过。
