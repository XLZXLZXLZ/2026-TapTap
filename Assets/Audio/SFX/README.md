# 玩法音效

当前为原创程序合成的柔和、克制的机械与磁吸音色。第一批 32 个 WAV（29 个短音效、3 个循环），第二批 8 个 WAV（7 个短音效、1 个循环），合计 40 个 WAV，由 26 个 SoundId 事件复用。菜单点击继续引用 `Assets/Audio/SoftClick.wav`；音乐、收音机语音保留原目录。

柔和版降低了主体音高、噪声亮度和峰值，使用 14 毫秒圆滑起音、30 毫秒收尾与两级 1250 Hz 低通。弹跳改为短促、阻尼较强的回弹；磁吸与时间操作使用小幅音高变化，复活改为单次温和重组声。循环降低高次谐波和节奏起伏。当前试听为 `Design/Audio/SFX-Preview-Soft.wav`，原试听保留作对照。

## 资源与配置

- `Character/Magnet`：磁扣松脱、闭合，各 3 个采样。
- `Character/Impact`：轻、重壳体碰撞，各 3 个采样。
- `Character/Bounce`：踩头与弹簧共用的 3 个弹性采样。
- `Character/Feedback`：吸附失败，2 个采样。
- `Character/Respawn`：死亡 2 个、复活 1 个采样。
- `Mechanisms`：按钮 2 个、虚实切换 1 个、活塞 2 个采样。
- `Time`：回溯开始/结束、子弹时间开启/关闭。
- `Loops`：磁场、倒带、传送带，均为 2 秒无缝循环。
- `Assets/Resources/Audio/GameplaySoundLibrary.asset`：运行时自动加载的配置。Inspector 可替换 Clips，调整音量、音高范围、最小触发间隔、并发上限、优先级及暂停/回溯策略。

WAV 为单声道 44.1 kHz / PCM16；短音效带边缘淡化，循环按整周期合成。音效没有外部采样依赖。

## 事件接入

`AudioManager.Instance` 自动创建并跨场景保留。16 路 OneShot 池按优先级分配，循环按 SoundId + owner 去重。`MenuAudio` 保留为旧菜单和收音机音量接口的兼容层，不再拥有自己的音源。

```csharp
AudioManager.Instance.Play(SoundId.MagnetJoin, position, gain: 1f, owner: this);
// 在 Update/LateUpdate 持续提交；不再提交、owner 销毁或停用后自动淡出。
AudioManager.Instance.SetLoop(SoundId.MagnetHum, this, active, position);
```

角色由 `PlayerAudioFeedback` 自动绑定 PhaseChanged、Effect、ContactFeedback 和复活事件。普通归位与吸附复用磁扣；远吸和砸落合体叠加重碰撞。踩头与弹簧从实际弹跳来源发声，不重复订阅角色的 Bounce 特效事件。复活按一次返回任务发声，避免合体返回产生双响。

按钮、全局虚实、活塞、子弹时间、回溯、选关、通关、钥匙拾取已在成功发生的行为处接入。历史 RestoreState 不发声。磁场循环从恢复后的角色状态重新接续。

传送带按玩家附近最近的带体合成一条循环底声，多块拼接不会增加声音数量。空间衰减使用玩家身体的 XY 距离，不受透视相机的 Z 距离、拉远演出影响。尚未发现区域的传送带不发声。

暂停时冻结玩法音源，UI 继续响应；回溯开始停止普通玩法声音并播放独立倒带；切场景、重开和关卡转场清理循环。音量沿用原有 Music/Effects 存档键，现有语音仍按原播放时钟运行。

## 再生成与验证

在项目根目录运行 `python Tools/Audio/generate_sfx.py`。WAV 固定种子生成，Unity GUID 保持稳定。默认保留已存在的配置；加 `--reset-library` 才重建配置音量和映射。

合成参数报告输出至 `Logs/Audio/synthesis-report.json`。Unity 回归入口为 `TapTap.Editor.AudioRegressionTests.RunBatch`，应在独立项目副本中以 batchmode 运行，不加 `-quit`；结果输出至该副本的 `Logs/Audio/regression-results.txt`。

## 第二批：返程、电力、门与锯齿轮

`python Tools/Audio/generate_return_journey_sfx.py` 生成 8 个新 WAV，并向配置追加 SoundId 21～25；保留已有事件的素材映射、音量与播放参数。若先执行第一批脚本的 `--reset-library`，随后再执行第二批脚本补齐配置。

| 事件 | 资源 | 调用点 |
| --- | --- | --- |
| PowerFlicker | ReturnJourney/Power/PowerFlicker_01～03 | 前两个警告的每次闪暗开始；默认总共四次 |
| PowerDown | ReturnJourney/Power/PowerDown_01 | 第三触发点的压暗演出开始；电机从约 380 Hz 降至 45 Hz |
| PlatformDetach | ReturnJourney/Platform/PlatformDetach_01～02 | 实际平台脱落一次，含老化、脱钩与短促火花细节；恢复已落下状态时不播 |
| DoorUnlock | ReturnJourney/Door/DoorUnlock_01 | 钥匙抵达锁孔并成功提交消耗时播放一次 |
| SawLoop | Loops/SawLoop | 真正的 GridSpikes/Gear 预制体上的 SawAudioEmitter；玩家靠近时轻度淡入 |

前四项允许在演出的输入锁期间播放，暂停菜单和时间暂停仍冻结音源。它们替代原有阶段变化的通用电路声，避免断电与开门时重复叠音。锯齿轮默认 1.5 格内满幅、6 格外无声，按实际危险碰撞体与玩家身体的 XY 距离衰减；未发现区域、停转或停用的锯齿轮不发声，多片相邻锯齿轮只保留一条轻度底声。

第二批串试听位于 `Design/Audio/ReturnJourney-SFX-Preview.wav`。对应回归入口为 `TapTap.Editor.ReturnJourneyAudioRegressionTests.RunBatch`，需在独立副本、带图形设备的 batchmode 中运行；结果与钥匙遮挡截图输出至 `Logs/Audio/ReturnJourney/`。

钥匙使用独立的 `Resources/Materials/ProgressKeyOverlay` 材质（Transparent+150、ZTest Always、ZWrite Off），拾取前、携带中与飞向锁孔时共用。材质只用于钥匙；不移动逻辑碰撞体，不影响拾取或锁孔对齐。关闭钥匙外观的动态遮挡剔除，携带时继续遵从既有区域发现与显隐规则。
