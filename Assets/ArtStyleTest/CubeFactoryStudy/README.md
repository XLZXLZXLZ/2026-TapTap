# 方块工厂设计图美术还原

场景：Assets/ArtStyleTest/Scenes/CubeFactory_Design_Study.unity
Unity 2021.3.24f1c1 / Built-in / Linear。

## 范围
依据设计图左侧完整游玩画面搭建；右侧仅用于机关外观参考。
134 个道路地块全部为 1 × 1 × 1 Unity Unit 立方体，采用轻微倒角。
没有创建玩家；所有机关仅为静态美术模型，没有交互、碰撞或游戏逻辑。
现有场景、脚本、Prefab、Build Settings、Graphics、Quality、Package 均未修改。

## 打开与观察
在 Unity Project 窗口双击 CubeFactory_Design_Study.unity。
建议 Game 视图设为 1536 × 1000；3:2 也可，初始视角展示完整左侧关卡。
固定初始视角采用窄视角透视，接近横版 2.5D 构图。
点击 Play，再点击 Game 窗口获得输入焦点。
WASD 移动，Q/E 升降，Shift 加速；按住右键转向，Esc 释放鼠标，R 恢复初始构图。
Main Camera / Free Fly Camera 可修改移动速度、灵敏度和操作提示。
相机无碰撞，可穿过物体观察。较宽屏幕会显示更多两侧空间。

## 场景结构
01_Cube_Roads_And_Walls：单位立方体道路、墙体、阶梯与悬空平台。
02_Slate_Supports：蓝灰色工业支架、斜撑、管线和电气箱。
03_Conveyors_And_Mechanism_Models：三条青色传送带、
暖黄发光连接桥、两个危险锯齿机关、两个红色弹跳帽、
金黄色伸缩块、开关、可穿越平台外观、绿色起终点。
04_Hanging_Vines：低多边形垂挂植物。
05_Warm_Factory_Background：暖色远景窗户、工厂框架、吊爪、工作台与机关示意牌。
06_Sunlight_And_Glow：斜向硬边光束、自然方向光、背景暖色补光和起点绿色光。
Main Camera：自由观察相机。
共 910 个几何对象，分组后可以单独调整。

## 美术
实体使用 Standard Lit，Base Color 为主，无纹理，Metallic 0，Smoothness 0。
关闭材质高光与反射。光窗和光束复用已有简单加法透明 Shader。
暖色方向光支持软阴影，斜向几何光束提供清楚的边缘线。
场景使用少量远景线性雾，主相机增加清晰像素化后处理，没有更换渲染管线。
几何光束和发光桥光晕为风格化近似，不模拟真实体积散射和遮挡。

## 文件与验证
CubeFactoryStudy/Materials：本场景独立材质。
CubeFactoryStudy/Meshes：单位倒角立方体、叶片、半球、齿轮、箭头及斜向光束。
CubeFactoryStudy/Preview/CubeFactory_Design_Study.png：Unity 实际相机渲染，不用于场景材质。
Editor/CubeFactoryDesignBuilder.cs：独立生成与验证工具；已有目标时拒绝覆盖。
复用原 ArtStyleTest 的圆柱、球体、光束 Shader 和 FreeFlyCamera，只读取、不修改它们。
Validation.txt 与 PlayValidation.txt：检查记录。
在当前完整原项目的临时副本中，用同版本 Unity 编译、重新打开、渲染并进入实际 Play 模式。
程序化验证自由相机移动、升降、加速、转向、复位和鼠标清理；
没有模拟物理键盘/鼠标输入。原项目未由编辑器打开。

## 清晰像素化
主相机已挂载 `Crisp Pixelation`，在 Game 视图和 Play 模式生效。
在 Inspector 调整 `Pixel Size`：1 为原分辨率，默认 4，8 的像素感更强；范围 1–64。
单位为相机输出画面的实际像素，改变 Game 分辨率会改变像素块相对于场景的大小。
运行时也可通过 `CrispPixelation.PixelSize` 调整。禁用组件可查看原画。
Scene 视图仍用于查看原始几何，不显示相机后处理。

Shader 对最终相机图像按整数方格取样，固定选取源纹理中的一个像素，使用 Point 过滤和 LOD 0。
每格颜色完全一致，最后不足一格的边缘直接裁切，不拉伸整张画面。
组件启用期间关闭该相机的 MSAA，禁用时恢复原值；不会修改全局 Quality 设置。
已有光照、雾和阴影保留。这是整个画面的像素化，不是轮廓描边，也不包含颜色数量压缩。
建议直接以 Game 视图原始大小观察，外部图片查看器缩放可能再次平滑像素。

文件：`Shaders/CrispPixelation.shader`、`Scripts/CrispPixelation.cs`（相对于 ArtStyleTest）。
验证菜单：`Tools > Art Style Test > Validate Cube Factory Pixelation`（打开目标场景并渲染）。
批处理入口：`ArtStyleTestTools.CrispPixelationValidation.RunBatch`，同时验证编辑状态和实际 Play 模式。
验证检查 1 倍图像保持、4/8 倍像素格颜色完全一致、641×359 下 7 倍的边缘裁切及参数范围。
测试报告：`PixelationValidation.txt`；预览：`Preview/Pixelation_Edit_4.png`、`Pixelation_Edit_8.png` 等。
