# URP 渲染配置

通用像素、笔刷、撕裂、色散和显像管滤镜见 [屏幕滤镜说明](ScreenFilters/README.md)。
入口：**Tools → Rendering → 屏幕滤镜工作台**；公共配置为 `ScreenFilters/Profiles/DefaultScreenFilters.asset`。

## 本次运行的探索遮罩

大世界游玩时自动启用 `World Exploration Mask` Renderer Feature。`WorldMapRoot.PrepareExploration` 创建并刷新 `WorldExplorationMask`，只揭开 `RegionRevealView.Discovered` 为真的区域；倒带保留探索，重新开始按现有流程重置。地图选择、拼图预览、Scene 视图和独立单关不应用这张遮罩。

可见窗口取已探索区域的内部范围与实际模型投影的并集：内部按 `LevelFootprint` 填充，保留凹口；模型通过独立的纯色绘制补上超出逻辑边界的 3D 外轮廓。遮罩按当前透视相机每帧投影，在 Bloom、倒带和屏幕滤镜后盖住窗口外的全部画面，因此背景放在更远的 Z 位置也不会漏出。场景材质、碰撞与机关逻辑不变，UI 随后正常绘制。

遮罩的空间边界保持硬边。首次跨入区域时，`RegionRevealView.SetDiscovered(true)` 立即记录探索状态并准备模型，再通过 `VisibleOpacity` 在默认 0.4 秒内平滑揭开遮罩。`RevealDuration` 可调整时长，设为 0 则立即显示；`SetDiscovered(value, true)` 用于初始化与重置，立即应用目标状态并取消未完成的渐变。暂停时渐变停止，子弹时间不拉长时长；重复进入与刷新模型不重播，临时演出覆盖仍与探索显示进度取最大值。

`GameplayRuntime/Environment/EnvironmentParticle` 使用专用 `EnvironmentBackground` Layer，单独渲染到没有关卡深度遮挡的背景纹理；遮罩外显示相机背景底色与这套粒子，遮罩内保留正常场景画面。使用同一套粒子模拟和原材质，粒子在已探索区域内仍正常受模型遮挡。没有启用粒子背景时回退到 `Mask Color`（默认黑色）。Play 中可在大世界根节点停用 `WorldExplorationMask` 对比。

`EnvironmentParticle` 的 `WorldEnvironmentParticles` 组件在运行时按整个 World 的包围范围自动居中并扩大发射区域，每边默认额外覆盖 16 单位。它只修改 Shape 范围，保持粒子大小、寿命、运动和颜色设置；发射率与最大粒子数按新旧面积比同步增加，以保留原始密度。重复进入、倒带和重开使用最初捕获的参数，不叠加倍数，也不会重置一个范围未变化的粒子场。

粒子、线条和 TextMesh 标签不参与探索轮廓；模型轮廓按网格几何绘制，不读取原材质的透明裁切。普通背景应放在区域模型层级之外，避免被当作区域外轮廓。环境粒子背景在独立纹理中直接合成，不经过主场景的 Bloom 和屏幕滤镜；其位置与主相机保持一致。

图形验证需在独立副本中使用 Unity 2021.3.24f1c1：`-batchmode -executeMethod WorldExplorationMaskValidation.RunBatch` 检查模型轮廓、凹口、背景深度、物理镜头偏移、探索重置与现有倒带逻辑；`WorldExplorationMaskValidation.RunPlayBatch` 进一步检查实际大世界运行、跨区、倒带及模型重建。截图与结果写入 `Logs/ExplorationMask/`。

迁移前基准：`main` 的 `a88199f`。试验分支：`codex/urp-trial`。
URP 试验提交 `0c69c0b` 已合入 `C:\GitHub\2026-TapTap` 的本地 `main`；最近的手感优化和关卡编辑改动一并保留。
编辑器保持 Unity 2021.3.24f1c1，URP / Core RP / Shader Graph 使用 12.1.11。

## 打开与试用

直接打开 `C:\GitHub\2026-TapTap`，用上述版本等待 Unity 完成包、脚本和 Shader 导入。渲染管线配置和转换后的材质已经保存，正常打开不需要运行迁移命令。

- 游戏入口：`Assets/Scenes/WorldMaps/World_Character_Select.unity`。
- 游戏表现：`Assets/Scenes/WorldMaps/World_Character_Play.unity`。
- 独立保留示例：`Assets/Scenes/Test/Demo_Hanging_Test.unity`。
- 未用于大地图的旧 Demo、SampleScene 和三个独立美术测试场景已清理；美术模型、材质、预览图片及历史说明继续保留。像素化组件仍可使用，原方块工厂专用验证在场景不存在时明确跳过。

## 迁移内容

`Settings/TapTapRenderer.asset` 使用 Universal Renderer，同时渲染 3D 物体和无光照精灵。
内置的 TapTap Screen Effects Renderer Feature 仅处理挂有相应组件的游戏相机；Scene 视图和地图预览相机不会受到倒带效果影响。
倒带先处理场景颜色并保存两张未处理的历史帧，像素化随后执行。屏幕叠加 UI 维持原来的独立绘制方式。
组件停用、效果结束、分辨率变化及新倒带会话会释放或重建历史帧。

Graphics 默认使用 TapTapHigh。Very Low / Low 使用 TapTapLow（无阴影）；Medium 使用 TapTapMedium（硬阴影）；High / Very High / Ultra 使用 TapTapHigh（软阴影，2x MSAA）。像素化相机自行关闭 MSAA。
各画质档位均开启管线 HDR；TapTapRenderer 已配置 URP 默认 PostProcessData，以支持相机和 Volume 中启用的 Bloom 等内置后处理。具体效果仍由相机的 Post Processing 开关与场景 Volume 配置控制。
关闭 URP 新增的运行时调试菜单，避免它轮询项目中没有配置的 Debug 输入按钮；保留现有玩法输入映射。编辑器 Rendering Debugger 仍可使用。

Standard 转为 URP Lit，旧 Unlit 转为 URP Unlit；显式精灵材质转为 URP Sprite Unlit。
Unity 默认 SpriteRenderer 材质保持引擎默认引用，在 Universal Renderer 下继续验证其兼容性。
现有资源 GUID 保持不变，场景无需重新生成。美术与玩法材质生成器已经改为创建 URP 材质。
旧 LowPoly 场景仍引用历史提交中已删除的 11 个材质；本分支按原 GUID 补回这些被引用的资源，并转换需要转换的材质。
区域渐显与窗光使用 URP Shader；渐显同时映射 URP 的 BaseMap / BaseColor。

URP 一次性迁移脚本及材质转换菜单已清理。项目直接使用已保存的管线配置和材质；渲染与构建验证入口保留。

## 验证

批处理使用 Unity 的真实图形设备，日志与场景截图写入忽略目录 `Logs/URPTrial/`。
编辑场景渲染与渐显：`UrpTrialValidation.RunBatch`。
像素化编辑/Play 渲染：`ArtStyleTestTools.CrispPixelationValidation.RunBatch`。
倒带玩法与相机渲染：`TapTap.Editor.RewindRegressionTests.RunBatch`。
Windows 构建：`UrpTrialValidation.BuildBatch`。

验证结果见 `Validation.txt`。原有美术目录中的 Validation 与 Preview 保留为迁移前历史记录。
最新 URP 截图在 `Preview/`，包括两个工厂场景、运行时玩法、像素化和倒带残影。

本机的 Windows 构建模块只安装在 Unity 2021.3.32f1c1 中，因此附带的本地开发构建使用该编辑器及其自带 URP 12.1.13。构建成功后进行了启动检查。源码目标仍为 2021.3.24f1c1 / URP 12.1.11；该目标的同版本 Windows 构建尚未验证。
该早期构建仍在原试验工作目录的 `Builds/URPTrial/TapTap-URP.exe` 中，没有加入 Git，也不包含后来新增的手感优化。当前版本请在主项目目录进入 Play 试用。

## 当前边界

目标是保留现有玩法和效果，提供可比较的 URP 版本；灯光、阴影与透明混合需要人工比较，画面不承诺与 Built-in 逐像素一致。
尚未对 Android / iOS 真机进行帧率、显存及 Shader 变体验证。
迁移前参考提交为 `a88199f`。合入前未提交文件的本地备份位置记在 `Logs/URPIntegrationBackup.txt`；回退或切换提交前需保留当前手感和关卡编辑改动。
