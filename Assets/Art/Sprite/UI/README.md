# Fqh 暂停菜单

## 使用

- `World_Character_Play.unity` 已放入 `Assets/Prefabs/UI/PauseMenu.prefab` 的关联实例。运行后点击右上角纸张图标或按 **Esc** 打开菜单，再按 **Esc** 或点击“继续游戏”关闭。
- 按钮、入口图标及滑条手柄使用 `UIInteractionScale`：悬停放大至 105%，按下缩小至 95%，0.1 秒平滑过渡；暂停时也能播放。所有控件关闭颜色过渡，自动选中“继续游戏”和退场禁用交互均保持原色。按钮美术以实际按钮中心缩放，点击区域保持固定；滑条只缩放手柄，拖动范围不变。
- “主菜单”优先进入当前大世界配置的选关场景；普通场景使用 `PauseMenu.MainMenuScenePath`，默认是 `World_Character_Select`。发布时将目标场景加入 Build Settings。
- 音乐和音效滑条分别控制 BGM 与菜单音效。调整音效时播放试听声；音量会保存在 PlayerPrefs 中。BGM 在菜单关闭、游戏暂停和切换场景时继续播放。

## 移植

把 `Assets/Prefabs/UI/PauseMenu.prefab` 拖入目标场景即可，每个场景保留一个激活实例。无需拖入 Fqh 的相机或灯光，也无需手动绑定控件。已有 EventSystem 时直接复用，没有时在初始化时自动创建，保证入口图标立即可点击。

根节点保持启用，脚本只隐藏 `MenuContent`，因此关闭界面后仍能接收 Esc。编辑模式下内容保持可见，便于调整美术。Canvas 使用 1920×1080 参考尺寸并完整适配屏幕；按钮仅在实际美术区域响应点击。

菜单打开后暂停游戏并屏蔽移动、分离、回溯、重置和选关操作；退场动画完成后恢复此前速度及鼠标状态，移除菜单时立即恢复。已有批注窗口或场景过渡优先处理，避免两个暂停窗口抢占时间。带此 Prefab 的场景中，Esc 用于暂停菜单，“主菜单”按钮负责返回选关。

## 动画与模糊

- DOTween 使用独立时间更新，暂停后仍能播放。左图从上方滑入，退出时向左滑出；右侧标题、继续、主菜单、音量区按从上到下的顺序从右侧滑入/滑出。点击区域和滑条随所属行一起移动。
- `PauseMenu` 的 `SlideDuration`（默认 0.48 秒）、`RowStagger`（0.065 秒）和 `FadeDuration`（0.4 秒）可在 Inspector 调整。退场期间再次按 Esc，会从当前动画位置平滑重新打开。
- Panel 使用纯色半透明底，与暗色遮罩一起淡入/淡出，不引用 `Assets/Art/UI/Panel.jpg`。已移除“ESC 返回游戏”文字。
- `TapTapRenderer` 上的 `Pause Menu Blur` 在场景后处理完成后执行双向高斯模糊，Overlay 菜单保持清晰；强度跟随打开/关闭动画变化，关闭后跳过模糊渲染。如移植到不同 URP Renderer，需为该 Renderer 添加 `PauseBlurFeature` 并指定 `Hidden/TapTap/Pause Blur` shader。

## 音频

`Assets/Audio/QuietWorkshop.wav` 是为本项目生成的原创测试配乐，72 BPM，约 26.67 秒，柔和分解和弦循环；`SoftClick.wav` 是原创轻提示音，无第三方素材授权依赖。替换 Prefab 上的 `BackgroundMusic` / `ClickSound` 即可更换。

`MenuAudio` 跨场景保持单实例，两路音量独立，不修改全局 AudioListener 音量。后续通过 `MenuAudio.Current.PlayEffect(clip)` 播放的音效也遵循音效滑条；独立 AudioSource 需要自行接入。

## 维护与验证

`Tools > TapTap > UI > Build Fqh Pause Menu Prefab` 可从 Fqh 中的 01–05 美术层重新整理并保存 Prefab，并确保目标场景已放入实例、Renderer 已接入模糊。该命令会重新生成控件布局，应在需要重建时使用，不会在打开 Unity 时自动执行。

`TapTap.Editor.PauseMenuRegressionTests.RunBatch` 用于隔离项目的 Unity batchmode 回归，覆盖暂停/恢复、输入屏蔽、按钮命中、音量与保存、无 EventSystem 移植、重复实例、实际场景切换，并输出 16:9 和 5:4 预览图到 `Logs/PauseMenu`。

`TapTap.Editor.PauseMenuRegressionTests.RunInteractionBatch` 在空场景加载当前 Prefab，专项检查选中、悬停、按压、移出、禁用、隐藏及重新打开的颜色与缩放，验证暂停时的独立时间动画、实际退场和中断退场；不依赖场景或模糊渲染配置。
