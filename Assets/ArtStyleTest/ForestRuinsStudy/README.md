# 森林废墟 · 参考图美术研究

打开 `Assets/ArtStyleTest/Scenes/Forest_Ruins_Reference_Study.unity`，Game 视图选择 **16:9**。
预览：`Preview/Forest_Ruins_Reference_Study.png`，由 Unity 2022.3.62f3c1 / URP 14 实际渲染，1600×900。

## 场景内容
起伏草坡、约五万株合并网格草叶、低多边形分层松树、逐块砌筑的石穹顶、双扇封闭门、赭色门框、残柱、碎石、破损高架和远处城市遗迹。参考图仅作为美术参考，所有景物都是可编辑的真实三维几何。

## 美术设置
- 天空：`Materials/Forest Sky.mat`，程序化蓝青渐变与象牙色舒展云带。
- 日照：暖色方向光，俯角 37°、方位 38°，强度 1.6，柔和阴影；冷色三色环境光。
- 雾：场景 Lighting / Environment，蓝灰色线性雾，38–205 米，保留近景色彩并压低远景对比。
- 调色：`Forest Atmosphere.asset` 全局 Volume；ACES、曝光 +0.08、对比 +8、饱和 +1、微暖白平衡、冷暖分离、轻 Bloom 和暗角。
- 景深：Bokeh，焦点 54 米、焦距 120 mm、光圈 f/1.8，可在 Volume 中关闭或调整。
- 前景：`05 - Warm sunlight and cool atmospheric fill` 下有仅投射阴影的画外树冠，构成下沿阴影；它们是静态美术补光遮片，可关闭对比。

## 编辑与操作
层级 01–06 分别控制草坡、穹顶废墟、松林、城市远景、光照、后处理。材质与网格均保存在本目录。未修改项目共用渲染管线、质量设置或已有场景。

Play 自由观察：WASD 移动，Q/E 升降，右键转向，Shift 加速，R 恢复初始参考构图。

`Tools > Art Style Test > Create Forest Ruins Reference Study` 提供可复现生成入口；场景已存在时拒绝覆盖。直接双击已有场景即可查看，无需重新生成。

## 验证与边界
在隔离项目中完成编译、场景重开、网格与材质引用检查以及 URP 实际渲染。Volume 堆栈确认 ACES 和 Bokeh 生效。隔离预览未加载原项目的游戏专用 Renderer Features。场景为对参考图的风格化近似；雾是距离雾，未使用真实体积散射，天空云层为程序化静态云。
