# 结局

入口场景：`Assets/Scenes/MainMenu/Ending.unity`。沿用原有环境与相机，主菜单仍为独立的 `MainMenu.unity`。

## 当前流程

1. 进入场景后，机器人在 `Ending Sequence/01 - Inside Fortress` 出现，停留 0.6 秒。
2. 以 0.7 米/秒沿三个路点走出堡垒，Y 方向由现有 TerrainCollider 和重力控制。
3. 到达 `03 - Walk Line` 后开放 A/D 或左右方向键，以 0.85 米/秒沿相机的水平右方向移动。没有跳跃、拆头或回溯操作；走路动画为原动画的 0.5 倍速度。
4. 进入收音机的 3D 球形范围后停止移动，锁定操作。镜头在 1.5 秒内向机器人推进 1.2 米，旋转与 FOV 保持原值。
5. 播放 `Assets/Audio/Voice/Sequences/Ending.asset`，其中使用 `Processed/Ending/Ending_Soft.wav` 和既有的 38 条中英字幕。
6. 语音完整结束后停留 1.5 秒，2 秒渐白；全白时加载 MainMenu，随后用 2 秒移除白色遮罩。遮罩独立跨场景存活，避免加载时闪出未遮挡的画面。

`MainMenu` 与 `Ending` 已追加到 Build Settings，原有场景次序保持不变。这里只配置结局场景，不修改现有关卡最后一步的跳转。

## 修改演出

- 在 `Ending Sequence` 上调整时间、镜头推进距离、语音结束后的行为；选中时可看到出场路径。
- 拖动三个路点可改出场路线。第一个点决定出生位置，第三个点也是开放操作后的行走线原点。第三点应保持在收音机沿相机右方向延伸的行走线上；竖直高度由地面碰撞决定。
- `EndingPlayer` 的 `WalkLimits` 是相对第三路点的左右边界，默认 -3 至 7 米。选中机器人可看到青色行走线。修改构图后应重新对齐行走线与收音机。
- `EndingRadio` 的 `TriggerRadius` 控制球形触发范围，默认 1.6 米。语音在 `EndingVoice` 中更换。
- 机器人和收音机使用 `Assets/Prefabs/Ending/` 下的独立预制体；玩法中的原始 Player 和 Radio 预制体保持不变。

## 保留操作权

取消 `Ending Sequence` 上的 **Return To Main Menu After Broadcast**，广播结束后会平滑恢复镜头并重新开放左右移动。已完整播放的广播不会自动重复触发。

可通过 UnityEvent、Timeline Signal 或代码调用：

- `EndingSequenceController.ReturnControl()`：中止当前出场/广播，恢复镜头并归还操作权。
- `EndingSequenceController.ReturnToMainMenu()`：以后在另一次交互中启动白屏返回菜单。
- `OnBroadcastCompleted`：只在完整播放结束时触发；监听者也可以调用 `ReturnControl()` 改变后续流程。
- `OnControlReturned`：镜头恢复且操作重新开放后触发。
- `EndingRadioTrigger.ResetTrigger()`：需要重新收听时显式重置触发次数。
- `EndingPlayerController.SetHorizontalInput(float)`：关闭 `UseKeyboard` 后可接外部输入。

语音被打断或加载失败时会归还操作权，并允许再次触发；不会直接当作剧情结束。白屏转场开始后不接受 `ReturnControl()`，避免加载过程被中断后留下遮罩。

## 验证状态

已完成脱离编辑器的 C# 编译和序列化引用检查。尚未运行 Unity Play Mode；出堡垒路线、草地起伏上的贴地效果、动画观感和完整语音到菜单的转场需要在编辑器空闲时实机确认。
