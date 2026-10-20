# 结局

入口场景：`Assets/Scenes/MainMenu/Ending.unity`。沿用原有环境与相机，主菜单仍为独立的 `MainMenu.unity`。

## 当前流程

1. 进入场景后，机器人采用场景中 `EndingPlayer` 的实际位置和初始朝向，停留 0.6 秒；不会被旧的起点路标重置位置。
2. 以 0.8 米/秒沿经过 `贝塞尔路径点（按顺序）` 的三次贝塞尔曲线行走，按弧长推进，经过中间点不停顿。曲线只控制 X/Z；每次移动都向下检测场景 3D 碰撞，使用命中地面的高度确定脚底 Y。
3. 到达最后一个路径点后开放 A/D 或左右方向键，以 1 米/秒沿相机的水平右方向移动。没有跳跃、拆头或回溯操作；走路动画为原动画的 0.6 倍速度。
4. 进入收音机的 3D 球形范围后停止移动，锁定操作。镜头在 1.5 秒内向机器人推进 1.2 米，并沿相机右方向偏移 0.45 米，使角色更靠近画面中心；旋转与 FOV 保持原值。
5. 播放 `Assets/Audio/Voice/Sequences/Ending.asset`，其中使用 `Processed/Ending/Ending_Soft.wav` 和既有的 38 条中英字幕。
6. 语音完整结束后停留 1.5 秒，2 秒渐白；全白后依次播放制作单位、作者和特别鸣谢三页署名。最后一页淡出后加载 MainMenu，再用 2 秒移除白色遮罩。遮罩独立跨场景存活，避免加载时闪出未遮挡的画面。

`MainMenu` 与 `Ending` 均已加入 Build Settings。最终逃离阶段的第 8 步镜头到位后，会渐黑并加载本场景；结局语音播放完成后，按当前配置白屏返回 MainMenu。

## 修改演出

- 在 `Ending Sequence` 上调整时间、镜头推进距离、语音结束后的行为。开启 Scene Gizmos 即可看到贴地后的贝塞尔曲线和左右行走线，不需要选中对象。
- **推进时向右偏移（米）** 控制推进时的横向构图，默认 0.45；正值向右，负值向左。归还操作时会恢复原来的相机位置。
- 结局摄像机复用 `MainMenuCameraMotion` 的自动呼吸：周期 9 秒，位置幅度 `(0.015, 0.0225)` 米，旋转幅度 `(0.15, 0.2, 0.05)` 度，与 MainMenu 同步。结局的 `MouseRotation` 为零，仅自动呼吸；推进与恢复更新镜头基准位置，呼吸持续叠加，不会把镜头拉回起点。
- 直接移动场景中的 `EndingPlayer` 即可修改出生位置。
- 选中 `Ending Sequence`，在 Inspector 的 **贝塞尔路径点（按顺序）** 列表中拖入任意数量的空物体，按行走顺序排列；可增删、拖动排序。曲线平滑经过这些点，控制柄自动生成；**贝塞尔曲率** 越大转弯越圆滑，设为 0 时为直线段。所有路径点的 Y 值都忽略，脚底高度由碰撞决定，路径点本身不会被脚本覆盖。
- 点击 Inspector 下方的 **追加路径点** 可自动创建并加入一个点；选中 `Ending Sequence` 时，可拖动带编号的 X/Z 平面手柄，也可选中空物体修改 Transform 的 X/Z。创建和移动支持撤销。
- 黄色曲线表示找到可行走地面，红色表示当前检测范围内没有可行走的碰撞面。`GroundLayers` 决定脚下检测的层；草地、石板等需要有 3D Collider。机器人自己的碰撞体、触发器、头顶屋顶和过陡的面会被排除。运行时不会因路径继续而走进没有地面的区域；持续受阻达到 **路径受阻超时（秒）** 后会在当前位置归还操作权。
- 默认开启 **最后一个路径点作为水平行走线**，走完后会从该点开放左右移动。最后一点应与收音机位于沿相机右方向的同一条地面行走线上；竖直高度由地面碰撞决定。关闭此选项则使用 `EndingPlayer.WalkLine` 手动指定的行走线。
- `ModelFrontYaw` 为模型正面相对移动方向的偏移，当前模型使用 0 度，避免移动时背着走。出场停留期间保留场景里摆好的朝向，开始移动后再平滑转向。
- `EndingPlayer` 的 `IntroSpeed` 控制自动出场移速（默认 0.8），`MoveSpeed` 控制玩家操作移速（默认 1）。`WalkLimits` 是相对行走线原点的左右边界，默认 -3 至 7 米。修改构图后应重新对齐行走线与收音机。
- `GroundProbeHeight`/`GroundProbeDepth` 设置向下射线的起始高度和向下范围，`GroundSnapDistance` 是单步可贴合的落差；上台阶和斜坡还受 CharacterController 的 Step Offset、Slope Limit 限制。正常行走及出场都持续使用地面命中点决定 Y，保留一个胶囊 Skin Width 的间隙。
- `TurnSpeed` 控制指数平滑转向（默认 4.5，越小越柔和），`TurnLookAhead` 控制沿曲线提前参考的距离（默认 0.2 米）。
- `EndingRadio` 的 `TriggerRadius` 控制球形触发范围，默认 1.6 米。语音在 `EndingVoice` 中更换。
- 结局收音机预制体为 `Assets/Prefabs/Ending/EndingRadio.prefab`，使用 `Assets/Art/Materials/EndingRadio/` 下六份独立 URP Lit 材质，分别用于外壳、饰条、格栅、旋钮、金属和指示灯。保留现有配色，按部件调整 Metallic 与 Smoothness，并开启投射、接收阴影；指示灯保留少量自发光。可直接修改这套材质，不影响玩法收音机的共享材质。
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

## 白屏后的片尾署名

`Ending Sequence.CreditsPrefab` 已指定 `Assets/Prefabs/Ending/EndingCredits.prefab`。三页均为白底深灰字，大字使用现有 `tilte(BIG).ttf`，小字使用 `text(SMALL).ttf`；按照 1920×1080 的参考尺寸缩放，图标与文字都是可编辑的 UI 节点。

1. **Made By / SIGS-IMDT**：图标、英文引导语和制作单位居中排列，完整出现后停留 3 秒，再淡出。
2. **作者**：上排三人（李雨田、陈乐兮、吴佳璐）、下排两人（衡雨瑶、冯启浩），两排各自居中，中文名字下方分别配小号大写拼音，整体停留 4.5 秒：李雨田 / LI YUTIAN、陈乐兮 / CHEN LEXI、吴佳璐 / WU JIALU、衡雨瑶 / HENG YUYAO、冯启浩 / FENG QIHAO。
3. **特别鸣谢 / 游玩本作的 / 你**：从上到下逐行淡入；已出现的行保留，最后的“你”放大显示。三行全部出现后停留 3.5 秒，再由父 CanvasGroup 同时淡出。

各页淡入、淡出均为 1.2 秒，页间留白 0.5 秒，鸣谢相邻两行的淡入间隔 0.45 秒。可在预制体根节点 `EndingCreditsSequence` 上调整全部时长。Inspector 提供三个静态预览按钮，方便在 Prefab Mode 中编辑各页，预览支持撤销；实际播放会重置所有预览状态。

演出沿用白屏转场的非缩放时间，完成后才加载主菜单。渐白和全部署名期间保持结局音乐播放；署名结束、开始返回主菜单时才淡出结局音乐，随后按现有音乐管理器的配置淡入主菜单音乐。清空 `CreditsPrefab` 可恢复直接白屏返回菜单；关闭自动回菜单时仍保留先归还操作权的流程。IMDT 图标的来源及显示区域见 `Assets/Art/UI/Ending/README.md`。

## 曾通关记录与二周目主菜单

结局录音完整播放结束时，先通过 `GameCompletionStore.MarkCompleted()` 写入 `PlayerPrefs` 的 `TapTap.GameCompletion.HasCompletedGame = 1`，并立即保存，再触发广播完成事件和后续转场。进入结局、触碰收音机、播放中断或失败均不算通关。关闭自动回菜单时，正常听完仍会记录通关。

这份记录独立于关卡完成、解锁列表；再次开始游戏或清除世界进度不会清除它。只有显式调用 `GameCompletionStore.Clear()` 才会重置此记录，不影响音量、关卡进度等其他键。

MainMenu 场景预留根物体 `AfterCompletionContent`，文件中默认关闭。把二周目新增的模型、灯光或装饰放到它下面即可。另一个始终启用的 `MainMenu Completion State` 上挂有 `MainMenuCompletionContent`，进入主菜单时按通关记录启用或关闭内容根节点；运行时记录发生变化也会同步刷新。

选中 `MainMenu Completion State`，可用 Inspector 中的预览按钮切换显示而不写存档。菜单 `TapTap/结局/通关记录` 提供显式写入“已通关”和仅清除此通关记录的调试命令；不会自动执行这些命令。

## 验证状态

已完成脱离编辑器的 C# 编译和序列化引用检查。尚未运行 Unity Play Mode；出堡垒路线、草地起伏上的贴地效果、动画观感和完整语音到菜单的转场需要在编辑器空闲时实机确认。
