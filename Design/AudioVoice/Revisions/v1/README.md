# 广播语音制作交付

2026-10-18。原始 MP3 位于 `Assets/Audio/Voice`，全部保留，11 条均识别为英文，M06 同样按英文录音剪辑。

## 成品

- `Assets/Audio/Voice/Processed/Full`：11 条完整播报。
- `Assets/Audio/Voice/Processed/PowerCut`：M01—M06 各 1 条，M07 共 4 条，合计 10 条。
- `Audition.html`：全部文件的本地试听索引及实际保留台词。
- `M07_FivePass_Preview.wav`：五遍连续试听，遍次之间留 1 秒间隔，附同名 SRT。
- `Subtitles`：21 份英文字幕时间轴。`voice_manifest.json` 同时保存字幕片段、原录音取材区间及音量信息。

全部成品为 48 kHz、单声道、24-bit PCM WAV，响度目标 -19 LUFS，真峰值不高于 -1.5 dBTP。附 Unity `.meta`，保持 PCM 导入并关闭额外归一化。当前交付为音频素材，尚未接入录音机运行逻辑、场景或语音参数 SO。

## 音色与剪辑

Verity：明亮、温和的广告扬声器音色；Sarah：略窄频、带轻微箱体共鸣的厂内扩音；Sheila：稍暗、稳定的新闻收音机音色。统一听感音量，保留原录音的声线、语气、语速与音高。加入极轻设备底噪；空间混响和距离衰减适合由游戏播放环境控制。

断电版从同一录音按原顺序取材，缺失段使用短促静电、电源衰减和低电平噪声连接，保留词句处保持清楚。被删掉的人声已移除，不在噪声底下继续播放。长缺失段压缩为适量故障停顿，成品不与原始录音保持等长。

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

已有时间标记随交付保留，重新制作无需下载识别模型。若替换原始录音，先用 `transcribe_voice.py` 与 `align_voice.py` 重建标记，再复核 `voice_edits.json` 的词索引；识别依赖额外的 faster-whisper，精细对齐依赖 torch/torchaudio。对重新生成的断电文件应重新运行本地转写复核。
