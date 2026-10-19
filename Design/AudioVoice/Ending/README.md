# 最终独白语音交付

2026-10-19。原始录音 `Assets/Audio/Voice/Ending.mp3` 保留。

- 柔化成品：`Assets/Audio/Voice/Processed/Ending/Ending_Soft.wav`。
- 语音 SO：`Assets/Audio/Voice/Sequences/Ending.asset`。
- `Audition.html`：原声与柔化成品对比，成品带同步中文字幕预览。
- `Ending_Soft_Preview.wav`：从苦涩的否认与希望转折开始，到结尾告别的试听节选。
- `Ending.zh-CN.srt`、`Ending.en.srt`：字幕导出。
- `ending_manifest.json`：处理参数、响度、哈希及全部字幕时间点。

成品时长约 169.953 秒，48 kHz、单声道、24-bit PCM WAV。轻收高频与齿音、温和压缩并加入极轻的软饱和，统一到约 -19.06 LUFS，真峰值约 -1.57 dBTP。原音高、语速、全部停顿、叹息和苦笑保留，无额外底噪或空间效果。

SO 只有一个完整播放段，配 38 条双语字幕，沿用已实现的音频采样时间轴。中文字幕取自定稿，结尾保留两次“早安”。没有把配音 Tag 放进字幕，没有为了换字幕切片音频，也没有自动接入结尾场景。

转写全在本地进行。整段 Whisper 曾在拼接边界漏识别“未完成的玩具”等词句，并误识别两个额外词；对相关原声短片段独立转写后已恢复定稿内容，再以逐句 CTC 对齐字幕。38 句与定稿一致，`Ha` 按原有非语言笑声及随后的句子配置。

重新制作步骤：

1. 替换录音后重新生成本地转写并复核差异；本轮 `Analysis` 与 `Review` 已保留。
2. 运行 `python Tools/Audio/align_ending.py` 生成精细字幕时间点。该脚本的本轮复核修正对应当前录音，换录音时需重新核对。
3. 运行 `python Tools/Audio/build_ending.py` 导出柔化成品、SO、字幕和试听页。

通用的广播制作脚本只处理 M/H 编号录音，避免将 Ending.mp3 当作广告或新闻重做。Ending 的构建不会改变已有 21 个广播成品及其序列。

Unity 2022.3.62f3c1 最小独立工程导入校验通过：真实 SO 与音频引用、169.953 秒时长、48 kHz 单声道、38 条双语字幕及逐条时间轴查询均有效。结果见 UnityValidation.txt。此检查不包含结尾场景接线或整项目编译。
