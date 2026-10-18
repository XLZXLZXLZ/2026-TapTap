# 返程流程配置

本组资源不修改 `World_Character_Play.unity`。可复用的收音机与字幕系统见 `../Radio/README.md`；专属广播演出留待后续确定。

## 预制体与预览

- `Assets/Prefabs/ReturnJourney/ReturnJourneyDeparture.prefab`：阶段管理器、氛围参数、三个触发区和一个嵌套演出平台。将三个触发区分别移动到第20关所需位置；前两个各闪暗两次，第三个开启返程。警告的 Cue Id 在同场景中保持唯一。
- `Assets/Prefabs/ReturnJourney/ReturnJourneyPlatform.prefab`：外观初始高于碰撞一格，掉落后带轻微倾斜。碰撞始终固定。可替换 View 下的模型，或将 `ReturnJourneyPlatform` 挂到已经摆好的平台上，绑定 View、掉落后的局部位置／旋转和粒子。第三触发区的 Platform 槽可以引用它。
- `Assets/Prefabs/Interactables/ProgressKey.prefab`：原有钥匙已接入弱拾取粒子、携带跟随与开门消耗。放在第19关，沿用身体接触拾取；进入返程前不能拾取。
- `Assets/Prefabs/ReturnJourney/ReturnJourneyDoor.prefab`：一格实体门。将 Body Proximity 放在来路一侧，KeySocket 为钥匙飞向的位置。View、开门偏移、钥匙飞行时长、开门时长均可调整。默认门向上移一格，只身体能触发。
- `Assets/Prefabs/UI/PauseMenu.prefab`：原菜单新增 `ReturnJourneyPausePortrait`。Return Art 为预先隐藏的子图片，已引用 `Assets/Art/UI/RobotLevelBlueprint.png`，可直接换图。

暂停菜单根对象必须保持启用，`PauseMenu` 会在 Awake 隐藏 Content，关闭菜单时无需停用根对象。根对象未激活会导致 Esc 回落到返回选关逻辑，也无法在完整拾取演出后打开菜单。完整地图测试副本已启用该根对象。
- `Assets/Scenes/ReturnJourneyPreview.unity`：正式地图的独立测试副本，直接 Play 从第20关入口出生，镜头和复活点同步切到第20关。`WorldMapProgression.InitialLevelNumber` 为此场景的启动序号；正常场景默认 0，仍从首关开始。该副本用于真实地图测试，返程预制体仍按上面的说明摆放和绑定。
- `Assets/Scenes/ReturnJourneyMechanicsPreview.unity`：保留原来的小型流程演示，左右移动依次经过两次警告、第三触发点、钥匙和门。

显式维护入口 **TapTap → 维护 → 返程 → 创建缺失预制体并接入钥匙和暂停菜单** 只创建缺失资源，不覆盖已有返程预制体的手工配置。**创建独立演示场景** 仅创建新场景，不打开或保存正式游玩场景，不覆盖已存在的演示场景。

维护菜单中的“创建独立演示场景”对应小型机制演示，不会覆盖从第20关启动的完整地图测试副本。

Departure 建议作为独立场景根节点；放在区域 Custom 下也可使用。其 Target Camera 留空时仅自动选择同场景 DemoCamera 的游玩镜头，不影响选关相机。使用的 URP Renderer 需要已有的 `TapTapScreenEffectsFeature`（项目当前游玩 Renderer 已配置）。

警告每次闪暗 0.24 秒、间隔 0.24 秒，各播放两次，峰值强度 0.20。第三次触发后在 0.65 秒内进入持续强度 0.16 的变暗（原为 0.10），避免引导灯亮起后整体变暗不明显。可在 Departure 的 `ReturnJourneyPresentation` 调整。

## 时间线与状态

1. `Outbound`：正常流程，前两个触发点各闪暗两次，不改变阶段。
2. `Returning`：第三个触发点开启返程，清空 Z 历史，停用普通出口和 R，开启轻微变暗／色散／撕裂，引导灯光淡入，演出平台掉落。钥匙仍在原处。
3. `CarryingKey`：完整播放原有拾取放大、顿帧与时缓，期间屏蔽输入、Z、R 并冻结玩法模拟。完成后建立新的 Z 起点，恢复此前游戏速度，再强制打开暂停面板。面板入场结束后，以强撕裂切到蓝图；由玩家继续游戏。之后打开暂停菜单保持蓝图。
4. `DoorOpened`：携带钥匙时身体进入门附近，短暂锁定输入和 Z，钥匙飞入 KeySocket，播放原有强粒子并消失，门碰撞打开、外观上移。完成后建立新的 Z 起点并释放输入锁。门仅打开一次。

阶段、钥匙归属和门状态不进入 Z 快照。Z 在返程普通移动期间照常使用，但不能越过上述三个进度点。警告、拾取和开门不会因跨区域或 Z 重复触发。

进度沿用本次 Play 会话内保留的规则，不写磁盘存档。同一游玩场景重新载入时恢复阶段：拾取前保留地面钥匙，携带中重建跟随钥匙，开门后保留门开和钥匙消耗；退出 Play 清除。R 快捷键在第三触发点之后禁用，程序重置接口仍保留。

## 跟随、显隐与暂停

钥匙的携带外观独立放在场景根目录，原拾取区域的显隐不会隐藏它。头身分离仍跟身体；运动方向改变时平滑换边；死亡返回、传送或跨区不丢失。远距离传送立即跟上，Z 时直接对齐历史中的身体。跟随偏移以角色配置的单格长度为单位，浮动使用模拟时钟。

引导光在第三触发点统一淡入，恢复场景或延迟启用时直接恢复当前阶段的状态。灯光不揭示未发现区域，继续遵从 `RegionRevealView`。暂停冻结平台掉落、灯光淡入和开门，暂停面板与图片撕裂使用真实时间。返程滤镜和 Z 滤镜分别控制，可叠加。

## 阶段事件

`EventManager.Subscribe<GameStageChanged>(listener)` 接收场景、旧阶段和新阶段。每次实际推进只发送一次，场景恢复不重复发送。延迟启用的监听者应在 OnEnable 查询 `GameStageProgression.StageIn(gameObject.scene)` 恢复状态。后续录音机可接入此事件。

## 验证

在隔离项目运行 `-batchmode -executeMethod TapTap.Editor.ReturnJourneyRegressionTests.RunBatch`，不要同时使用 `-quit`，因为检查会进入 Play Mode。该入口仅创建独立验证场景，输出到 `Logs/ReturnJourney/results.txt` 及暂停面板预览图。覆盖三次阶段通知与 Z 起点、钥匙不提前消失、外观掉落不动碰撞、灯光淡入、完整拾取后暂停、蓝图撕裂、原速度恢复、携带显隐、开门消耗与输入锁释放。

`TapTap.Editor.ReturnJourneyRegressionTests.RunPreviewBatch` 单独检查已生成的演示场景，验证开场处于正常阶段、实际 URP 镜头中的返程滤镜与拾取放大，并输出 `preview-results.txt` 与前后画面。画面变暗比较使用固定背景区域，避免新增引导光和爆炸粒子影响统计。

`TapTap.Editor.ReturnJourneyRegressionTests.RunWorldBatch` 检查完整地图测试副本中的第20关出生、Esc 对应的暂停／恢复、减速闪暗、第三次触发后的真实镜头亮度，以及完整拾取后打开菜单并替换蓝图。亮度使用同帧启用／跳过滤镜的成对画面比较，避免引导灯和粒子干扰。
