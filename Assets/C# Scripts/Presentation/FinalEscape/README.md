# 关卡 22：最终逃离演出

已配置于 `Assets/Scenes/WorldMaps/World_Character_Play.unity` 的 `World_Character/Custom/Final Escape Sequence`，对应资源为 `Assets/Prefabs/FinalEscape/FinalEscapeSequence.prefab`。不依赖关卡序号硬编码，通过 Level 引用 `Level_OutWay`，由首次触发区域开始演出。

## 编辑配置

- `Final Camera Target` 的世界坐标为 `(26.5, -38.9, -227.9)`。开启 Scene Gizmos 可见黄色位置标记。整个拉远过程保持进入演出时的相机世界旋转，不转向大世界中心，也不使用目标 Transform 的旋转。默认继承进入演出时的 FOV；需要独立视野大小时开启 Override Field Of View。
- `Escape Triggers (Level Local Grid)` 下有 8 个 BoxCollider2D 触发区，按 Step 1–8 排序，覆盖最后平台通道的宽度，局部高度分别为 1、5.5、9.5、13.5、17.5、21.5、25.5、30.9。触发组运行时自动对齐 Level 引用对应的区域。可直接调整触发区子对象的位置和 Box Collider 尺寸，保持第一步为 1、最后一步等于 Step Count。
- 每次触发推进目标进度：`(CurrentStep / StepCount)^2`。起点固定为首个触发区处的实际镜头，每段默认平滑过渡 0.65 秒。连续跨越多个触发区时只前进、不重复累计。
- 首次触发后，默认用 0.85 秒显示全部子区域。通过表现覆盖显示模型和灯光、渐退区域内部遮罩；区域外遮罩保持，未写入探索和解锁存档。
- 最后一步镜头到位后，停留 0.25 秒，0.7 秒渐黑，激活可替换的 `Ending Screen`，再渐亮。默认是 THE END / 感谢游玩 的简洁结束画面；`FinalEscapeEndingTransition.OnBlack` 可另接结局逻辑。

## 操作规则

首个触发区之后禁止 Z 回溯和 U 调试相机，玩家继续正常跑跳；末尾转场开始才通过通用输入锁停止操作和正常模拟。触发检测只检查下半身自身范围及移动轨迹，不使用可能包含头部的合体范围，也不依赖 Rigidbody 触发回调。

第20关第三个触发点进入返程后，R 快捷键持续禁用，拾取钥匙与开门继续保留该规则（沿用本次 Play 阶段状态）。退出 Play 后清除。程序调用的重置接口仍可使用，结局输入锁期间除外。

## 接口与隔离

- `GameplayActionLocks` 提供场景范围的回溯、输入、调试镜头锁；玩法系统不引用终局类。回溯锁不会禁止正常模拟。
- `DemoCamera.TryAcquireControl/ReleaseControl` 提供独占镜头控制，正常镜头和直接 SetRegion 调用不会覆盖演出镜头。
- `RegionRevealView.SetVisibilityOverride` 与 `VisibleOpacity` 提供临时显示覆盖，独立于 Discovered；遮罩填充使用最大值合并，保证已探索部分不被淡入区域再次盖黑。
- 演出在物理触发检测前和镜头更新前检查关卡绑定。通关、跳关或重开使 `Generated` 重建后，清理旧演出的步数、镜头控制、操作锁及显示覆盖，通过当前大世界重新绑定关卡、触发组和区域显示组件。已停用但尚未销毁的旧关卡与初始状态模板不参与绑定；新关卡暂不可用时等待恢复。
- 演出代码、预制体和结局画面集中存放，原有滤镜、Volume、Post Processing 配置不变。

回归入口：`TapTap.Editor.FinalEscapeRegressionTests.RunBatch`，只在隔离项目以 batchmode 执行，覆盖重建前后触发、引用与显示覆盖更新、镜头和输入锁释放、禁用恢复、第 8 步到达最终镜头位置，以及所有步骤的过渡、连续触发和暂停期间保持入场旋转。视觉效果仍需在游戏中验收。
