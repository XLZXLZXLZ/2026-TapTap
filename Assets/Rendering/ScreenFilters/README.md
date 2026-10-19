# 屏幕滤镜

入口：Unity 菜单 **Tools → Rendering → 屏幕滤镜工作台**。

公共配置：`Profiles/DefaultScreenFilters.asset`。初始为原画，各滤镜已有可用参数；开启对应项即可生效。
`GameplayRuntime.prefab` 的主相机引用这份资源，测试场景和大地图共用配置，无需重新生成关卡。
`MainMenu.unity` 和 `Ending.unity` 的主相机 `Camera` 也引用同一份配置，调整后一起生效。

主菜单／结局的接入方式：
1. 选中带 `MainCamera` 标签、实际启用的 `Camera`，添加 **Rendering → Screen Filter Camera** 组件。
2. 将 `Profiles/DefaultScreenFilters.asset` 拖到组件的 **Profile** 字段。
3. 在相机使用的 Renderer 资源中添加 **Screen Filters** Renderer Feature。这两个场景使用
   `URP-HighFidelity-Renderer.asset`（Renderer 索引 1），已在原有 SSAO 和雾效之后补上。
   只挂相机组件而没有 Renderer Feature，滤镜不会执行。
4. 在 Game 视图或 Play 模式查看效果；Scene 视图不显示此滤镜。Screen Space Overlay 的 UI 保持清晰。

## 调整和预览

1. 在工作台选择配置，用左上方的原画、像素、笔刷、撕裂、色散、显像管按钮快速试用。
   预设按钮会重置整个草稿；组合多个滤镜时，在列表中开启各项，再拖动执行顺序。
2. 展开参数面板调节强度和细节。可以撤销、重做；随机种子固定时，同一时间得到相同结果。
3. 预览来源支持内置测试画面、项目图片或场景相机。相机模式复制取景到临时相机，
   不移动、保存或修改场景相机，不包含 HUD、黑屏遮罩和玩法倒带。
4. 可切换分辨率、画幅，调整原画／效果分界；暂停后拖动时间，检查故障效果的某一时刻。
5. 试调保存在工作台草稿中。点击“应用并保存”才写回共享资源，并刷新游戏画面；
   “另存配置”创建独立预设。也可以直接在配置 Inspector 调整，此时所有引用相机会立即生效。
6. “导出效果 PNG”保存右侧完整效果图。预览动画以 30 秒为循环，与游戏时间、暂停和倒带独立。

像素、笔刷色块、CRT 条纹的尺寸按实际输出像素计算。撕裂和色散偏移以 1080p 为基准，随输出高度缩放。
笔刷使用屏幕空间色块、颜色量化、边缘感知采样和低频微扰；不是贴合模型表面的真实笔触。
CRT 包含扫描线、RGB 荫罩、曲率、暗角、亮度补偿和闪烁，不额外保存历史帧。

## 接入与隔离

- `Runtime/` 是独立 `ScreenFilters.Runtime` 程序集，仅依赖 Unity / URP；不引用 TapTap 玩法。
- `Editor/` 是独立编辑器程序集。工作台、相机渲染和验证共用 `ScreenFilterRenderer` 与同一套 Shader。
- `Resources/ScreenFilters/ScreenFilters.shader` 确保构建包含五种滤镜，无需修改 Always Included Shaders。
- 新相机添加 `ScreenFilterCamera` 并引用 Profile；空配置、关闭组件或总开关时不处理。
- Renderer 资源中先放原有 `TapTap Screen Effects`，后放 `Screen Filters`。两者使用相同的
  `AfterRenderingPostProcessing` 注入点，URP 12 对同一注入点按添加顺序稳定执行。
  流程为：场景 → 倒带及旧像素兼容 → 通用滤镜栈 → 屏幕 UI。
- Scene 视图、未接入的地图预览相机、Overlay Camera 不受影响。
- 旧 `CrispPixelation` 和资源 GUID 保留；同一相机启用新配置中的像素化时，跳过旧像素 pass，避免重复。
- 只有启用像素化且尺寸大于 1 时接管并关闭相机 MSAA；停用后恢复原状态。
- 关闭的滤镜不执行；全部关闭时不入队全屏 pass。启用时共用两张临时纹理轮换处理，按相机描述重建，完成后释放。

倒带仍由原有 `EffectManager` / `RewindScreenEffect` 管理触发、过渡和原始历史帧。
其已有撕裂、色差、扫描线会与通用滤镜叠加；配置常驻故障风格时，请在实际倒带中检查组合强度。

## 验证

Unity 菜单 **Tools → Rendering → 验证屏幕滤镜**：验证五种效果、关闭与零强度还原、固定时间／种子、
像素块精确性、排序、分辨率变化及 MSAA 恢复。测试使用临时对象，不切换当前场景。

完整批处理入口：`ScreenFilters.Editor.ScreenFilterValidation.RunBatch`。
另验证公共预制体、Renderer 顺序、大地图相机，并进入实际 Play 模式渲染。
批处理应在独立项目副本中运行，不加 `-nographics` 或 `-quit`；执行器完成后自行退出。
结果和效果图位于忽略目录 `Logs/ScreenFilters/`。

当前目标为 Unity 2021.3 / URP 12。手机 GPU、XR、相机堆叠整体画面及其他 URP 主版本尚未验证。
