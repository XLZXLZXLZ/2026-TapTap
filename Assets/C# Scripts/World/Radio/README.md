# 收音机与语音字幕时间轴

把 `Assets/Prefabs/Interactables/Radio.prefab` 拖入关卡。模型使用纯色 URP Unlit 材质；根节点的 BoxCollider2D 是触发区，默认 4×4，只检测玩家身体，支持高速经过。

## 触发区与播放呼吸

选中收音机根节点，在 BoxCollider2D 中保持 Is Trigger 开启，用 Size 设置宽、高，Offset 设置区域相对收音机的位置；也可点击 Edit Collider 在 Scene 视图中拖动边框。正方形区域让 Size 的 X、Y 相等。默认 Size 为 (4, 4)、Offset 为 (0, 1)。不要通过缩放根节点来调触发范围；收听距离由 RadioReceiver 的 Full Volume Radius / Listening Radius 单独控制。

RadioReceiver 的 Breathe While Playing 默认开启：模型播放时平滑放大再恢复，循环进行。Breathing Amount 默认 0.06（最大 106%），Breathing Duration 默认 1.2 秒（一轮）。Breathing Target 指向 RadioModel 子节点；现有实例未指定时会自动查找同名子节点。仅缩放模型，触发区不变。暂停或按 Z 时定格，继续播放后接着呼吸；播完、中断、禁用时恢复原尺寸。替换模型时可把新的模型子节点拖到 Breathing Target。

## 配置音频

在 Project 右键选择 **Create → TapTap → Audio → Voice Sequence**，为每组播报建立初见、返程两份独立 SO，分别赋给 RadioReceiver 的 FirstEncounter 和 ReturnJourney。同内容可共用只读 SO，各收音机的收听记录仍按实例独立保存。

`Assets/Audio/Voice/Sequences` 已配置 18 份内容资源：

| 播报组 | FirstEncounter | ReturnJourney |
| --- | --- | --- |
| H01—H04 | 对应的 `Hxx_FirstEncounter.asset` | 留空；返程不会经过这些收音机 |
| M01—M06 | 对应的 `Mxx_FirstEncounter.asset` | 对应的 `Mxx_ReturnJourney.asset` |
| M07 | `M07_FirstEncounter.asset` | `M07_ReturnJourney.asset`，共五个播放段 |

`FirstEncounter.asset` 和 `ReturnJourney.asset` 仍保留为空白模板。正式关卡的摆放和引用由关卡配置决定，本次没有自动替换场景中的收音机。

结尾独白另有 `Ending.asset`：一条完整的 `Processed/Ending/Ending_Soft.wav`，配 38 条中英字幕时间轴，默认中文。可赋给收音机字段，或在结尾演出中调用 `VoicePlayer.Play(endingSequence)`。此资源与 18 份广播序列独立维护。

按播放顺序添加 Segments，每段配置：

- Clip：一段完整音频。正常播报和 M01—M06 返程均为一个播放段，换字幕不会切换音频。
- Subtitles：本段的字幕时间轴，每条填写 Start、End、Chinese、English。时间相对本段音频开头，单位为秒；区间包含 Start、不包含 End。按时间排列，不重叠，且全部落在音频时长内。时间轴空隙自动隐藏字幕。
- Gap After：本段后的停顿秒数，最后一段忽略。

Sequence 的 Volume 控制整组音量。所有段都有有效 Clip，且字幕时间轴有效时才允许播放；不完整或无效配置不会消耗收听次数。旧配置的整段 Chinese / English 字段仍兼容，只在 Subtitles 为空时使用。字幕不要包含配音语气 tag。

字幕跟随 `AudioSource.timeSamples / Clip.frequency`，独立于游戏速度及帧累计时长；暂停、撤销暂停和继续播放时与音频同步。同一音频中的字幕切换不会重启 AudioSource。连续字幕直接换字，时间轴空隙及播放结束时淡出。

## M07 五遍返程

`M07_ReturnJourney.asset` 按序播放四份现有断电音频：1、2、3、4、4。段间隔为 1 秒，最后两段引用同一个 `M07_PowerCut_04.wav`，不复制音频。每遍有一条独立中文译文，前几遍保留缺失语意：

| 遍次 | 中文字幕 |
| --- | --- |
| 1 | 召回几套机器人套装……关键问题是……在送达之后……轻得孩子也能…… |
| 2 | 召回几套……关键问题……在送达之后……足够轻…… |
| 3 | 拿起……关键问题……跟着光走。 |
| 4 | 拿起钥匙。跟着光走。 |
| 5 | 拿起钥匙。跟着光走。 |

这是同一次播放中的五个段，不需要五次触发收音机；专属镜头或触发演出仍可从外部调用 `TryPlay()`。

## 播放规则

玩家身体进入触发区时播放，离开触发区但仍处于收听范围内会继续。声音按玩家身体与收音机的 XY 距离计算，Full Volume Radius 内保持完整音量，至 Listening Radius 线性衰减至零；默认分别为 3、10 世界单位。还会乘以游戏现有的音效音量设置。

每台收音机初见、返程分别完整播放一次；未播完就超出收听范围会中断，下次进入从第一段重播。可关闭 Stop Outside Listening Range，让广播在范围外静音但继续计时。同一场景只保留一组广播，新广播会中断旧广播，避免字幕混杂。

返程取自 GameStageProgression；进入 Returning 时中断初见广播，并允许播放返程配置。携带钥匙、开门不会再次重置。暂停菜单、输入锁定、按 Z 撤销期间暂停播放；Z 不切换初见/返程、不撤销已听完的记录。临时禁用后再启用会保留记录，销毁或重新载入场景则开始新的收听记录，叙事阶段由现有流程系统恢复。

后续演出可关闭 Trigger Automatically，调用 `RadioReceiver.TryPlay()`；`ResetPlaybackHistory()` 清除该实例的两阶段收听记录。M07 五段语音已配置。

需要返程广播特写时，使用 `Assets/Prefabs/ReturnJourney/ReturnJourneyRadio.prefab`，或在现有收音机根节点增加 `ReturnJourneyRadioFocus`。它在返程语音开始时推进到 3 格高的近景，禁止玩家操作，但玩家动画、时间和场景模拟继续运行，同时锁定 Z 与调试镜头；全部语音和间隔播完后退回当前区域的正常镜头，初见广播不接管镜头。可调整 Closeup Height、Focus Target、Approach Duration、Restore Duration，或由外部调用 `TryActivate()`。暂停菜单和其他系统的锁仍会正常暂停它。详见 `../ReturnJourney/README.md`。

## 字幕

`Assets/Resources/UI/VoiceSubtitles.prefab` 会在首次播放时自动实例化。同场景收音机共享一套字幕，位于屏幕下方，使用 text(SMALL) SDF、白字、5.5 字间距，底板使用 `Assets/Art/Sprite/UI/PausePanel/Panel.png`，通过默认 UI 材质保留图片的透明渐变。按字幕时间轴显隐并按内容换行、调整底板高度，无鼠标遮挡。

默认中文。切换接口（暂未接菜单）：

```csharp
SubtitleLanguageSettings.SetLanguage(SubtitleLanguage.English);
SubtitleLanguageSettings.SetLanguage(SubtitleLanguage.Chinese);
```

播放中切换也生效。某语言为空时使用另一语言；两种均为空时不显示字幕。也可调用字幕组件的 SetLanguage 方法。

`Assets/Scenes/Demo/Radio_Demo.unity` 提供独立摆放示例，配置 SO 后即可试听；没有修改正式关卡。编辑器菜单 **TapTap → 语音与收音机 → 创建缺失资源** 只补建缺失资源，保留已配置内容。

## 内容维护与验证

中文分节定义位于 `Tools/Audio/radio_subtitles_zh.json`，英文与时间点取自 `Design/AudioVoice/voice_manifest.json`。在仓库根目录运行 `python Tools/Audio/configure_radio_sequences.py` 可重新生成上述 18 份命名资源，不修改原音频、两份空白模板或场景。手工改过生成的 SO 时，请先同步定义，避免重新生成时覆盖。

`RadioTimelineRegressionTests.RunBatch` 在独立 Unity 项目检查真实 SO 导入、时间轴边界和无效配置、语言回退、实际采样时钟、暂停/慢动作、连续字幕，以及 M07 五遍真实音频的中文显示和结束清理。原 `RadioRegressionTests.RunBatch` 继续检查触发、距离衰减、阶段切换与旧字幕配置。
