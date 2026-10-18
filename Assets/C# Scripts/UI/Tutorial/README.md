# 操作教学提示

把 `Assets/Prefabs/UI/Tutorial` 中的触发器拖入关卡，调整位置和 BoxCollider2D 的 Size 即可。只检测玩家下半身；检测包含移动轨迹，可以捕获快速穿过窄触发区的情况。触发器支持旋转和缩放。

| 触发器 | 提示与完成条件 |
| --- | --- |
| `Trigger_01_Hover` | 按住 [Space] 使头部悬浮；松开后退出，衔接“再按 [Space] 向头部吸附” |
| `Trigger_03_Rewind` | 操作失误时，按住 [Z] 回溯；松开后退出 |
| `Trigger_04_BulletTime` | 按下 [Ctrl] 放慢时间，方便精细操作；按下左/右 Ctrl 后退出 |

运行时首个触发器会按需创建同场景唯一的 `TutorialPresenter` Canvas，也可以提前放置它。Canvas 排序为 900，低于暂停菜单与黑屏遮罩；Screen Anchor 默认 `(0.5, 0.8)`。不需要给场景额外添加相机或 EventSystem。

## 自定义一条提示

复制一个提示 Prefab，修改 `TutorialPromptView`：

- `Prefix`、`Key`、`Suffix`：按键前文字、按键图标、按键后文字。按键作为 TMP 内联 Sprite 排版，和文字一同散落。
- `Completion`：`Press` 等待一次新的按下，`ReleaseAfterHold` 等待按住后松开。进入提示时已按住的键也可以松开完成；未按住就松开不会完成。没有额外长按时长门槛。
- `Next Prompt Prefab`：可留空。退出动画结束、经过 `Next Delay` 后优先播放下一条，再处理其他触发器排队的提示。循环链会被阻止。
- `Enter Duration`、`Panel Fade Duration`、`Jelly Overshoot`：调整入场、背景退出与回弹。
- TMP 对象上的 `TMPTextShatter`：调整散落速度、重力、旋转、分字延迟等。

在触发器的 `Prompt Prefab` 中指定复制出的提示。`Player` 可留空，自动寻找同场景角色。`Play Once` 默认开启：每次场景运行仅触发一次，回溯不会撤销完成状态；重载场景后重置。关闭后每次重新进入可以播放。

提示从入场开始观察操作。连续提示在上一条的退出与间隔期间也会记录下一条操作，避免快速按键漏判。动画使用真实时间；暂停、反馈、转场、失焦期间不判定输入，Z 提示可在回溯期间接收松开，其他提示忽略回溯期间的操作。输入只被观察，不消耗 `PlayerInput` 的玩法队列。

退出时背景在 0.16 秒内淡出，文字先轻微向上抛出，再加速下落，横向以 ±300 的随机初速度散开，向下重力为 5000。文字保持大小和不透明度，按当前 Canvas 的屏幕底边自动计算下落时长，全部落出屏幕后才结束；1080p 下约 0.7 秒，竖屏会自动留出足够的下落时间。

吸附提示只检查按键，不检查吸附成功。悬浮教学触发器应放在头部能穿过单向平台、松开后留在平台上的位置，让接下来的吸附提示符合场景条件。

## 美术与演示

- 字体：`Assets/Fonts/text(SMALL) SDF.asset`。
- 背景：保留 `Assets/Art/UI/Panel.jpg`，通过 `TutorialPanel.mat` 的白色转透明材质使用原图的形状与柔边，无白色矩形背景。
- 按键：`Assets/Art/UI/Tutorial/Key_Space.svg`、`Key_Z.svg`、`Key_Control.svg`。矢量线段同时用于生成 `Keyboard.png`，不依赖 SVG 导入插件。`Keyboard.asset` 是 TMP Sprite Asset。
- 演示：打开 `Assets/Scenes/Demo/TutorialPrompts_Demo.unity`，向右行走经过三个区域。第一组在悬空平台附近，可实际尝试头部悬浮、松开、再次吸附；Ctrl 再按一次恢复时间速度。

`Tools > TapTap > UI > Tutorial > Build Assets and Demo` 可以显式重建预置资源和演示场景，会覆盖这些预置文件；正常打开 Unity 不会自动重建，也不会改写其他关卡。

回归入口：在独立项目使用 `-batchmode -executeMethod TapTap.Editor.TutorialPromptRegressionTests.RunBatch`（不带 `-quit`）。结果和实际 UI 截图输出到 `Logs/TutorialPrompts`，测试完成后自动退出。
