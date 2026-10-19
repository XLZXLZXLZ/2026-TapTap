# 返程流程配置

本组资源不修改 `World_Character_Play.unity`。可复用的收音机与字幕系统见 `../Radio/README.md`，返程广播特写见下文。

## 预制体与预览

- `Assets/Prefabs/ReturnJourney/ReturnJourneyDeparture.prefab`：阶段管理器、氛围参数、三个触发区和一个嵌套演出平台。将三个触发区分别移动到第20关所需位置；前两个各闪暗两次，第三个开启返程。警告的 Cue Id 在同场景中保持唯一。
- `Assets/Prefabs/ReturnJourney/ReturnJourneyPlatform.prefab`：外观初始高于碰撞一格，掉落后带轻微倾斜。碰撞始终固定。可替换 View 下的模型，或将 `ReturnJourneyPlatform` 挂到已经摆好的平台上，绑定 View、掉落后的局部位置／旋转和粒子。第三触发区的 Platform 槽可以引用它。
- `Assets/Prefabs/Interactables/ProgressKey.prefab`：原有钥匙已接入弱拾取粒子、携带跟随与开门消耗。放在第19关，沿用身体接触拾取；进入返程前不能拾取。
- `Assets/Prefabs/ReturnJourney/ReturnJourneyDoor.prefab`：宽 1 格、高 3 格的卡通圆角金色门，根节点仍为底部一格的中心（下沿 y=-0.5、上沿 y=2.5），保持原有落地位置。Body Proximity 放在来路一侧，中央 KeySocket 为钥匙齿尖飞向的位置。Unlit 材质使用固定顶点配色，不含金属、高光或灯光阴影；锁孔为实际凹槽。钥匙飞行默认 0.55 秒，随后从锁孔向外溶解 0.9 秒，交界呈金色荧光并伴随碎光，整门及边框全部消散后解除碰撞。仅身体能触发。
- `Assets/Scenes/GoldenDoorPreview.unity`：独立的金色门预览，含参照砖块、钥匙与角色。Play 后向左移动拾取钥匙并靠近门即可查看完整动画。预览自带 Bloom；正式场景的荧光晕由其相机 Post Processing 与 Volume 的 Bloom 配置控制。
- `Assets/Prefabs/UI/PauseMenu.prefab`：原菜单新增 `ReturnJourneyPausePortrait`。Return Art 为预先隐藏的子图片，已引用 `Assets/Art/Sprite/UI/PausePanel/RobotLevelBlueprint.png`，可直接换图。
- `Assets/Prefabs/ReturnJourney/ReturnJourneyRadio.prefab`：收音机的返程特写变体，预填 M07 初见／返程语音。也可直接给已有收音机增加 `ReturnJourneyRadioFocus`，沿用它已有的音频和触发范围。
- `Assets/Scenes/ReturnJourneyPreview.unity`：正式地图的独立测试副本，直接 Play 从第20关入口出生，镜头和复活点同步切到第20关。`WorldMapProgression.InitialLevelNumber` 为此场景的启动序号；正常场景默认 0，仍从首关开始。该副本用于真实地图测试，返程预制体仍按上面的说明摆放和绑定。
- `Assets/Scenes/ReturnJourneyMechanicsPreview.unity`：保留原来的小型流程演示，左右移动依次经过两次警告、第三触发点、钥匙和门。

暂停菜单根对象必须保持启用，`PauseMenu` 会在 Awake 隐藏 Content，关闭菜单时无需停用根对象。根对象未激活会导致 Esc 回落到返回选关逻辑，也无法在拾取演出后打开菜单。完整地图测试副本已启用该根对象。

显式维护入口 **TapTap → 维护 → 返程 → 创建缺失预制体并接入钥匙和暂停菜单** 只创建缺失资源，不覆盖已有返程预制体的手工配置。**创建独立演示场景** 仅创建新场景，不打开或保存正式游玩场景，不覆盖已存在的演示场景。

**TapTap → 维护 → 返程 → 更新卡通金色溶解门** 显式更新门预制体的 View、1×3 碰撞、锁点与触发区，不保存游玩场景。已有门预制体实例自动继承更新；场景中手动覆盖过的尺寸/锁点需检查 Overrides。模型位于 `Assets/Art/3D Resources/ReturnJourney/GoldenDoor.fbx`，可编辑源文件为 `Design/Models/GoldenDoor.blend`，重建脚本为 `Tools/Blender/build_golden_door.py`。

维护菜单中的“创建独立演示场景”对应小型机制演示，不会覆盖从第20关启动的完整地图测试副本。

Departure 建议作为独立场景根节点；放在区域 Custom 下也可使用。其 Target Camera 留空时仅自动选择同场景 DemoCamera 的游玩镜头，不影响选关相机。使用的 URP Renderer 需要已有的 `TapTapScreenEffectsFeature`（项目当前游玩 Renderer 已配置）。

警告每次闪暗 0.24 秒、中间间隔 0.24 秒，各播放两次。两次警告均在进入时短暂锁住玩家操作及 Z，闪暗结束后释放；场景模拟、时间及重力继续运行。第三次先用 0.18 秒压暗到 0.62，停留 0.08 秒，再用 0.32 秒回弹到 Darkness 指定的常态值（默认 0.22）；随后才让演出平台掉落，并播放强度 0.3、持续 0.35 秒的震动，镜头震动范围上限为平时的三倍。平台掉落和震动结束后释放玩家操作及 Z。峰值与各段时间在 `ReturnJourneyPresentation` 调整，震动参数在第三个 `ReturnJourneyTrigger` 调整；已有场景组件自动获得新增参数默认值。

返程撕裂每秒两次、每次持续 0.08 秒，位移强度 7 像素，间歇恢复稳定画面。可在 Departure 的 `ReturnJourneyPresentation` 调整。暂停菜单会暂停闪暗、回弹、掉落与震动的时间线，禁用触发器会释放其操作锁。

返程同时淡入 15% 降饱和与强度 0.18 的柔和暗角，暗角从中心归一化半径 0.55 开始，保留中央区域。色彩错位基准为 2.5 像素，平时只保留十分之一，撕裂条带出现时增至 1.5 倍。这些均为屏幕后处理，可作用于 Unlit 场景，不修改灯光或材质，也不作用于独立的暂停菜单 UI。

## 时间线与状态

1. `Outbound`：正常流程，前两个触发点各闪暗两次，演出期间暂停玩家操作，不改变阶段。
2. `Returning`：第三个触发点立即开启返程并清空 Z 历史，停用普通出口和 R，引导灯光淡入。锁住玩家操作，先过度压暗再回弹到指定常态亮度，然后平台掉落并伴随强震动，演出完成后解锁。钥匙仍在原处。
3. `CarryingKey`：拾取时只轻推镜头 6%，推进 0.22 秒、停留 0.05 秒、回退 0.30 秒，不顿帧、不时缓，角色输入和玩法模拟继续运行。演出期间只锁 Z，并阻止额外的重置、阶段切换和菜单操作。镜头演出完成后建立新的 Z 起点，再强制打开暂停面板。面板入场结束后，以强撕裂切到蓝图；由玩家继续游戏。之后打开暂停菜单保持蓝图。
4. `DoorOpened`：携带钥匙时身体进入门附近，短暂锁定输入和 Z，钥匙齿尖对准 KeySocket 飞入并缩小，播放原有强粒子并消失，同时提交钥匙消耗进度。门从锁孔向外溶解，完成后关闭碰撞、建立新的 Z 起点并释放输入锁。门仅打开一次；暂停冻结飞行和溶解。飞入前中断保留钥匙并恢复其尺寸，消耗后禁用或重载直接恢复为已消散、可通行状态。

阶段、钥匙归属和门状态不进入 Z 快照。Z 在返程普通移动期间照常使用，但不能越过上述三个进度点。警告、拾取和开门不会因跨区域或 Z 重复触发。

所有带 `LevelEndpoint` 的出口（包括共用 `GridEndpoint` 内嵌的 End 模型）监听 `GameStageChanged`：进入 `Returning` 后，将绿色材质、发光颜色、Sprite 色调和灯光映射为略有区别的红色，保留原有饱和度、亮度和透明度，灰色结构保持原样。材质使用实例属性块，不改共享材质资源。后续启用或生成的出口会读取当前阶段恢复红色；这里的“回溯阶段”指第三触发点开启的返程，不是按 Z 倒带。

正式地图的出口点光源独立放在 `Arts/Art_*`，并非 End 子物体。出口也会匹配同场景中 XY 距离门前锚点两格以内的绿色光源（忽略美术层与逻辑层的 Z 差异）；若光源更靠近出生标记则保留绿色。灯光原有强度、范围和启用状态不变。

进度沿用本次 Play 会话内保留的规则，不写磁盘存档。同一游玩场景重新载入时恢复阶段：拾取前保留地面钥匙，携带中重建跟随钥匙，开门后保留门开和钥匙消耗；退出 Play 清除。R 快捷键在第三触发点之后禁用，程序重置接口仍保留。

## 跟随、显隐与暂停

钥匙的携带外观独立放在场景根目录，原拾取区域的显隐不会隐藏它。头身分离仍跟身体；运动方向改变时平滑换边；死亡返回、传送或跨区不丢失。远距离传送立即跟上，Z 时直接对齐历史中的身体。跟随偏移以角色配置的单格长度为单位，浮动使用模拟时钟。

引导光在第三触发点统一淡入，恢复场景或延迟启用时直接恢复当前阶段的状态。灯光不揭示未发现区域，继续遵从 `RegionRevealView`。暂停冻结平台掉落、灯光淡入和开门，暂停面板与图片撕裂使用真实时间。返程滤镜和 Z 滤镜分别控制，可叠加。

## 收音机返程特写

`ReturnJourneyRadioFocus` 监听同物体的实际播放事件，仅在返程配置成功开始播放、且当前阶段不早于 `Returning` 时接管镜头。初见广播照常播放。玩家身体进入收音机原有 BoxCollider2D 即可激活，也可由外部代码调用 `TryActivate()`、或在 UnityEvent 绑定 `Activate()`；它只尝试开启返程广播，不会提前播放初见内容。

默认镜头用 0.6 秒推进到收音机，基准高度 Closeup Height 为 2.5 格，新增 View Padding 默认 1.2，使实际视野垂直高度为 3 格、FOV 35 度；已有场景中保存的 2.5 格配置也会自动获得 20% 余量。Focus Target 对应预制体的 Closeup Focus 子节点，可移动该节点调整构图；Camera Rig 留空时寻找同场景的 DemoCamera。广播所有播放段及段间停顿结束后，用 0.5 秒退回玩家当前区域的正常镜头。期间禁止玩家操作，但动画、重力、惯性和场景模拟正常运行；只屏蔽玩家控制、Z 和调试镜头，不使用冻结模拟的 Input 锁，不改变游戏速度，也不新增 Z 历史边界。

暂停菜单和其他系统的锁仍会暂停广播与镜头。玩家走出收听范围时沿用收音机的中断规则；播放中断或失败会退镜并释放镜头与 Z 锁，禁用组件或对象则立即恢复，未完整播放仍可重试。相机已由其他演出占用时保留其控制权，广播正常播放。组件为可选功能，普通 Radio 预制体不会自动增加特写。

在隔离项目运行 `TapTap.Editor.ReturnJourneyRadioRegressionTests.RunBatch` 验证初见不接管、返程多段广播、3 格近景构图、玩家输入被屏蔽、动画与模拟继续运行、Z 锁定、暂停及其他锁、完成与中断恢复、禁用清理和无效配置；输出 `Logs/ReturnJourney/radio-focus-results.txt`。

## 阶段事件

`EventManager.Subscribe<GameStageChanged>(listener)` 接收场景、旧阶段和新阶段。每次实际推进只发送一次，场景恢复不重复发送。延迟启用的监听者应在 OnEnable 查询 `GameStageProgression.StageIn(gameObject.scene)` 恢复状态。后续录音机可接入此事件。

## 验证

金色门资源与渲染检查：在隔离项目运行 `-batchmode -quit -executeMethod TapTap.Editor.GoldenDoorAssets.BuildBatch`，生成/更新门预制体及缺失的独立预览，验证 FBX 轴向和 1×3 尺寸、Unlit 材质及无光/强光像素一致，并输出闭合、35%/70% 溶解、全开截图到 `Logs/GoldenDoor/`。`-batchmode -executeMethod TapTap.Editor.GoldenDoorValidation.RunBatch`（不带 `-quit`）检查真实 Play 中的钥匙消耗、飞行中断与重试、暂停、碰撞时机、动作锁、单次事件和重载恢复。

在隔离项目运行 `-batchmode -executeMethod TapTap.Editor.ReturnJourneyRegressionTests.RunBatch`，不要同时使用 `-quit`，因为检查会进入 Play Mode。该入口仅创建独立验证场景，输出到 `Logs/ReturnJourney/results.txt` 及暂停面板预览图。覆盖三次阶段通知与 Z 起点、钥匙不提前消失、外观掉落不动碰撞、灯光淡入、完整拾取后暂停、蓝图撕裂、原速度恢复、携带显隐、开门消耗与输入锁释放。

`TapTap.Editor.ReturnJourneyRegressionTests.RunPreviewBatch` 单独检查已生成的演示场景，验证开场处于正常阶段、实际 URP 镜头中的返程滤镜与拾取放大，并输出 `preview-results.txt` 与前后画面。画面变暗比较使用固定背景区域，避免新增引导光和爆炸粒子影响统计。

`TapTap.Editor.ReturnJourneyRegressionTests.RunWorldBatch` 检查完整地图测试副本中的第20关出生、Esc 对应的暂停／恢复、减速闪暗、第三次触发后的真实镜头亮度，以及轻推镜头期间速度不变且模拟继续运行、演出后打开菜单并替换蓝图。输出 `world-results.txt`。亮度使用同帧启用／跳过滤镜的成对画面比较，避免引导灯和粒子干扰。

`TapTap.Editor.ReturnJourneyRegressionTests.RunCurrentWorldLightsBatch` 在隔离项目中复制当前正式地图作为一次性测试场景，只保存该副本，验证美术层下实际的 20 盏出口点光源变红、5 盏出生点灯保持绿色，以及灯光强度、范围和启用状态不变。
