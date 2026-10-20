# 虚影操作提示

此目录是独立模块：`TapTap.Hints` 单向引用现有 `TapTap.Runtime`；`TapTap.Hints.Editor` 提供录制与配置工具。未修改现有角色、关卡、回溯或教程提示系统，也不自动修改场景。

录像保存角色身体、头部显示层级（包含骨骼）的姿态与可见性，以及连接线、左右移动 / Space / Ctrl 的输入观察数据。虚影只构建 Transform、网格、骨骼和渲染器，不复制角色脚本、Animator、碰撞、刚体、灯光或音源。机关、钥匙、世界相位和相机不随录像重演。按键目前保存在数据中，尚未增加按键提示 UI。

## 首次配置

1. 打开游玩场景，执行 `Tools > TapTap > Hints > Add Scene Setup`。每个游玩场景只放一个 `Hint Replay System`。
2. 在 Project 中选择 `Create > TapTap > Hints > Replay Catalog` 创建正向录像目录，将它赋给 `HintReplayDirector.Catalog`。
3. 保存场景。默认 H 请求提示，`HintKey` 可以调整；关闭 `UseKeyboard` 后可用 UI Button 的 `OnClick` 调用 `HintReplayDirector.PlayHint()`，停止按钮调用 `StopHint()`。

场景配置对象包含 `HintReplayDirector`、`HintReplayPlayer` 和 `HintReplayRecorder`。发布游戏不需要操作录制窗口，录制器在未开始录制时不工作，也可从场景移除。

## 录制正向关卡

1. 打开 `Tools > TapTap > Hints > Recorder`，玩家与参考点通常留空。
2. 进入 Play，来到需要录制的关卡与起始位置。让 Game 视图保持操作焦点，按 F8 开始录制。
3. 完成演示操作，再按 F8 结束。默认采样上限 30 帧 / 秒，最大录制 180 秒，可在开始前调整。
4. 可在 Play 中点击“在场景中预览”；关闭预览后仍可继续实际游玩。预览使用窗口指定的录像，留空则使用草稿。
5. 退出 Play，点击“保存草稿为录像资源”。草稿保存在当前工程的 `Library/HintReplay/RecordingDraft.json`，支持退出 Play / 脚本域重载后恢复；保存的 `.asset` 才是应提交的正式录像。重新录制会替换草稿，正式资源不会被自动覆盖。
6. 在窗口指定正向录像目录、绑定关卡与待绑定录像，点击“将录像绑定到此关卡”。也可直接编辑目录的 `Entries`。

正向使用 `LevelDefinition` 资源引用匹配，不依赖容易变化的关卡序号。`ModuleId` 通常留空；同一布局重复放入大地图且需要不同解法时，填写该模块的 `WorldMapModule.Id`，优先于普通关卡映射。独立测试场景也可通过 `LevelRegion.Source` 匹配。

正向录像以当前 `LevelRegion.transform` 为坐标参考。录制窗口参考点留空会自动采用它；正向不接受其他自定义参考点。关卡在大地图中平移后，录像仍能映射到新位置。

录制包含实际显示的动画姿态、头身分离与吸附过程。死亡复活属于解谜操作，头部、身体单独死亡或一起死亡都不会中断录制，继续记录收缩、重建与复活后的操作；复活期间也可开始录制或请求提示，已有虚影不会因真实玩家死亡而暂停。暂停和失焦期间不累计录像时间；Ctrl 缓时使用游戏时间，因此观看录像时动作按正常游戏速度呈现。按 Z、重置、正向切关或阶段变化会结束本次录制，保留已录制部分。返程录制允许跨越关卡区域。过短的录制不会覆盖已有草稿。

## 配置返程提示区域

1. 在 Hierarchy 右键选择 `TapTap > Hints > Return Hint Trigger`，调整 BoxCollider2D 范围。建议把它作为所属场景或关卡的子物体保存。
2. 进入 Play 并触发实际返程阶段 `Returning`，在所需世界位置按 F8 开始 / 结束录制。返程自动使用世界坐标，无需选中 Trigger，也无需配置参考点；窗口和录制组件中残留的参考点会被忽略。
3. 退出 Play，保存录像资源，将它赋给 Trigger 的 `Clip`。`Player` 和 `Director` 可留空，运行时寻找同场景对象。
4. 保存场景。玩家触碰 Trigger 后只切换当前录像，按 H 才播放。

新返程录像的 `UsesWorldSpace` 为 true，身体、头部根姿态和连接端点都保存世界坐标。播放时虚影位于场景根节点，始终在录制的位置出现；移动、旋转或缩放 Trigger 都不会影响它，预览也使用相同规则。正向录像仍以关卡为参考点。

旧录像没有世界坐标标记，保持原来的相对坐标行为；`PlaybackOrigin` 仅用于兼容这些旧返程录像，需与旧录像的录制参考点一致，留空仍使用 Trigger 本身。新返程录像忽略它。可在录像 Inspector 查看“坐标空间”，现有资源无需自动迁移或改写。

Trigger 组件保持订阅同场景的 `GameStageChanged`，其 Collider 在正向流程关闭，进入 `GameStage.Returning` 后启用；返程后加载或重新启用也会查询当前阶段。检测下半身的逻辑运动轨迹，支持快速穿越、旋转和缩放，不依赖 Unity 的刚体 Trigger 回调。

触碰只更新当前可播放录像，不自动播放。离开后保留选择，进入其他 Trigger 后更换；折返重进旧 Trigger 可以切回。进入返程时清除正向选择，首次触碰返程 Trigger 前没有可播放提示。普通 Z 时间回溯不会撤销已选返程提示。

## 播放行为与美术

`Assets/Prefabs/UI/PauseMenu.prefab` 的右上角提供提示图标，位于暂停图标左侧；两者悬停时淡入白色描边。有可用录像时点击提示图标，会淡入确认面板；点击“现在看”后先淡出，再调用原有 `HintReplayDirector.RequestHint()`，点击“再想想”、面板外或再次点击提示图标则关闭。H 快捷键仍直接播放。

面板不暂停游戏。暂停、切关或返程提示选择改变会关闭面板；确认淡出期间选择发生变化时，取消这次播放，避免播出旧关卡录像。没有录像时图标变淡且不展开；没有提示系统的场景隐藏此图标。图标、面板与按钮使用 `Art/Sprite/UI/CheatPanel` 的原图，通过 RawImage 的 UV 裁掉透明留白，字体使用 `text(SMALL) SDF`。`HintReplayPanel` 单向观察提示系统，现有角色与暂停逻辑不依赖此组件。透明度与淡入淡出时长可在预制体中调整。

需要重建此 HUD 时，可使用 `TapTap > UI > Hints > Build Hint Confirmation HUD`。工具只替换自己创建的 `HintButton` 和 `HintReplayPrompt`，并补齐两个图标的悬停组件。

- 单次播放，结束即清除虚影；再次按 H 从头重播。玩家仍可正常操作。
- 暂停和失焦时冻结；Z 回溯、切关、重置、流程变化、进入选关、控制锁或录像选择改变时停止播放。正向即使通过 Z 静默恢复了当前关卡，请求提示时也重新解析当前录像。
- 虚影不参加游戏时间回溯，也不写入 `RewindManager` 历史。
- `HintReplayPlayer.Tint` 默认半透明青色；`PlaybackSpeed`、连接线宽度可调。`GhostMaterial` 留空使用 `Runtime/Resources/HintReplayGhost.shader`，资源路径保证构建时可用。
- 录像兼容性按角色显示节点名称、父子关系和渲染器数量检查。角色模型层级改变后需要重录；这是为了避免错误骨骼对应产生异常姿态。当前模型的骨骼动画和眼部显隐可直接记录，运行中替换网格、Sprite 资源或 blend shape 权重不在此版本录制范围内。

## 回归验证

仅在独立测试工程副本中运行（不带 `-quit`，测试结束会退出）：

```text
Unity.exe -batchmode -projectPath <测试副本> -executeMethod TapTap.Hints.Editor.HintReplayRegressionTests.RunBatch -logFile <日志路径>
```

测试在 `Assets/HintReplayValidation` 创建独立场景与资源，不加载或保存现有游玩场景。结果位于 `Logs/HintReplays/results.txt`，启用图形设备时另存 `ghost-preview.png`。覆盖真实角色骨骼回放、纯显示组件、参考点移动、草稿与正式资源跨 Play 保存、暂停、死亡复活连续录制、返程世界坐标录制 / 播放 / 预览 / 保存、Trigger 变换独立、旧相对录像兼容、Z 中断、正向静默切关、返程事件隔离、Trigger 重入与晚加载、旋转 / 高速接触、控制锁与播放清理。

HUD 验证使用相同的独立副本限制，入口为 `TapTap.Hints.Editor.HintReplayUiRegressionTests.RunBatch`。它会重建副本中的暂停预制体，验证实际录像的确认播放、淡入淡出、取消、过期选择、暂停兼容、两图标悬停以及指定字体，并在 `Logs/HintUI` 保存结果与两种分辨率截图。
