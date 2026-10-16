# LocalFog — Unity 2021.3 / Built-in + URP 12.1

## 本次修复
原来组件会在 URP 下关闭 MeshRenderer；现在已移除该逻辑，并增加 URP 专用 SubShader。材质、脚本和预制体 GUID 均保持不变，已有实例不需要换材质。
当前项目 URP 质量资产默认关闭深度纹理。雾组件在 beginCameraRendering 中临时请求摄像机已有 UniversalAdditionalCameraData 的深度，在 endCameraRendering 中恢复原 requiresDepthOption。多雾共用协调器，避免恢复顺序冲突。没有自动新增摄像机组件，没有修改场景或管线资产。URP Scene View 使用管线生成的深度。
脚本依赖项目已有的 URP 包。若移植到其他 Built-in 项目，需保留 URP 包供编译，但无需启用 URP 管线。

## 使用
等待 Unity 自动导入与编译。已有实例可禁用再启用 Local Fog Volume，或重新进入 Play；也可重新拖入 Assets/Art/LocalFog/LocalFogVolume.prefab。
把雾体放在摄像机前方可见范围。用 Transform Position/Rotation 定位，用组件 Size 调大小（组件会管理 Transform Scale）。保持根对象或静止父对象，不要作为摄像机子对象。
- Shape：Box 长方体 / Sphere 球体；Sphere 三轴尺寸不等时为椭球。
- Size：完整宽高深，Sphere 为三轴直径，默认 (10,5,10)。父对象建议单位正缩放，不使用静态批处理。
- Color：雾颜色，Alpha 乘入浓度；Alpha=0 时无雾。
- Density：每世界单位浓度，0 为无雾，越大越浓；默认 0.25，测试可用 0.5。
- Edge Fade：0 为硬边，0~1 控制向内渐变宽度，占半尺寸/半径比例。
- Ray Steps：8~96，默认 48，越高边缘越稳定但 GPU 开销越高。

## 摄像机与深度
正常 URP 摄像机已有 Universal Additional Camera Data，无需挂额外脚本。组件不会自动添加它。
若摄像机缺少该组件且管线深度关闭，现会自动使用雾自身的备用不透明网格深度纹理；无需给摄像机挂组件。
目标为项目当前 Forward Universal Renderer 和普通单目透视/正交相机。不保证摄像机堆叠、2D Renderer、XR、反射和特殊投影。

## 限制
摄像机 Far Clip 必须覆盖整个雾体包围盒，出口背面被远裁剪时可能缺雾。摄像机在区域内外均可见，无需触发器。
URP 不透明物需支持 DepthOnly Pass，Built-in 需支持深度纹理。玻璃、粒子、部分 Sprite 等不写深度的透明物不能可靠遮挡雾。多雾重叠和透明排序存在普通透明渲染限制。不模拟体积灯光、阴影或散射。

## 验收
1. 等待导入，Console 无脚本/Shader 编译错误，MeshRenderer 已启用。
2. 摄像机置于雾外，Density=0.5、Color Alpha=1 检查可见性，再移动到雾内观察。
3. 在雾前后放不透明物检查遮挡；切换 Box/Sphere、大小、颜色和边缘。
4. Density=0 应完全无雾；复制实例独立调色，分别检查透视/正交相机。
5. 确认摄像机深度选项渲染后恢复，没有自动添加组件或修改管线资产。

已按本地 URP 12.1.11 源码核对 API 和回调时机；尚未在 Unity 编辑器执行 Shader 编译和画面验收。
## 前景保护修复
URP 现在使用深度纹理重建世界坐标，再截断雾积分；缺少深度时不再使用远裁剪距离代替深度。注意：若雾区本身延伸到了前景表面之前，物理上仍会有雾；默认厚度 Z=10 可能同时包住前景和背景，应在 Scene 视图检查雾区前边界。

需要无条件保持某些前景清晰时：
1. 选中雾实例，在 Local Fog Volume 的 Foreground Protection 下展开 Protected Foreground。
2. 添加元素，将前景物体或包含这些物体的父对象拖入。无需改代码。
3. 被指定的 MeshRenderer/SkinnedMeshRenderer 轮廓内不会绘制该雾，不论雾区是否穿过它。
4. 仅放需要保护的前景，不要放整个场景根对象，否则可能排除所有可见雾。

遮罩是此雾独立生成的临时纹理，不改变物体材质、Layer、场景或摄像机组件；每个启用保护的雾有一次额外绘制开销，空列表不生成遮罩。
此功能针对普通不透明网格。透明网格按完整几何轮廓保护，不支持贴图镂空；SpriteRenderer/TilemapRenderer/粒子不参与。蒙皮顶点变形的实际效果需在 Unity 验收。多个摄像机串行渲染支持，嵌套/堆叠相机未验证。
新增 Shader 已由预制体直接引用，避免只用 Shader.Find 导致构建时剥离。已有实例若字段未自动更新，可重新拖入预制体。
验收时对比：保护列表为空 / 加入前景 / 再移除；仅前景轮廓应发生变化，背景雾仍保留。此修复尚未在 Unity 编辑器中完成画面验收。
## Game 视图消失修复
项目 GameplayRuntime.prefab 的 Main Camera 没有 UniversalAdditionalCameraData，且管线默认关闭深度。旧版保护分支因此不绘制雾。现增加 LocalFogOpaqueDepth，在此条件下单独采集不透明 MeshRenderer/SkinnedMeshRenderer 的深度，供雾截断使用。前景保护列表继续生效；不新增摄像机组件、不修改场景或管线资产。
普通摄像机已有 URP 深度时仍使用原管线深度，不增加此采集开销。备用路径每摄像机额外扫描 Renderer 并绘制不透明网格，场景较大时有 CPU/GPU 开销；目前未作移动设备性能验收。
备用深度仅按网格几何采集：不支持贴图 AlphaClip 镂空、材质顶点动画、透明物、Sprite/Tilemap、地形和特殊 LOD；常规不透明平台/墙面适用。与原始材质裁剪不同的物体需进一步适配。
等待脚本编译和 Shader 导入后再进入 Play。已有雾实例无需重建；新增 Shader 在预制体中直接引用以避免构建剥离。若把整个场景父对象加入 Protected Foreground，会排除全部雾，请只指定真正的前景。
本次仍未通过 Unity 画面验收；源码和资源引用会进行静态检查。
## 雾区渐暗：Alpha 合成修正
原 Shader 使用相同的源/目标因子混合 RGB 和 Alpha。当背景 Alpha=1、雾 Alpha=0.5 时，写回 Alpha 会变为 0.75。现在采用预乘 RGB，并使用独立混合因子 Blend One OneMinusSrcAlpha, Zero One / ColorMask RGB 保留目标 Alpha；RGB 雾混合结果与原来一致。
这修复了雾无意改变画面透明度的明确问题，但并不等于已证实静止时渐暗的全部原因。仍需编辑器画面验收：保持摄像机、物体和参数静止，对比运行开始与 10 秒后的同一像素；雾区应稳定。不要用降低浓度掩盖问题。
本次没有修改摄像机、画面滤镜或现有场景。