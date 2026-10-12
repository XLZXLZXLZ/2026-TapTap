# Low Poly 美术测试场景

Unity 2021.3.24f1c1，Built-in 渲染管线，Linear 色彩空间。

## 打开与调整
在 Unity Project 窗口双击 Scenes/LowPoly_ArtStyle_Test.unity。
也可使用 Tools > Art Style Test > Open Low Poly Test Scene。
建议 Game 视图使用 16:10。编辑模式保留初始透视视角，FOV 38°；Play 模式启用自由观察相机。
在 Inspector 中修改材质 Albedo、灯光或几何体即可比较效果。

## 内容
12 × 10 × 4 的无屋顶观察房间，4 × 2 墙体开口，
四根立柱、4 单位传送带、不同尺寸纸箱、2 单位工作台、
三组管道、两株几何植物、简单未完成玩具，以及立方体、球体和圆柱体测试台。
主要结构采用模块化尺寸，小型装饰允许使用分数尺寸。
球体与十二边圆柱使用独立平面法线网格，保留 Low Poly 明暗面。
所有几何内容均已保存为可直接编辑的场景对象；仅自由相机使用独立运行时脚本。

## 材质和灯光
8 个 Standard 材质，无纹理、Metallic 0、Smoothness 0，
关闭 Specular Highlights 与 Glossy Reflections。
墙面 #A99592；地面 #41464A；机器 #74858A；纸箱 #D5B995；植物 #839C83。
实时方向光强度 0.85，暖色 Spot 强度 2.2、范围 7、角度 62°。
两盏灯均设置 Soft Shadows；平坦环境光 RGB (0.30, 0.32, 0.34)。
场景反射强度 0，无天空盒、雾或后处理。
阴影效果仍受原项目当前 Quality 档限制：桌面 Ultra 支持软阴影，
移动端默认 Medium 仅支持硬阴影。未修改任何项目级设置。

## 文件与验证
Scenes/：场景；Materials/：8 个材质；Meshes/：2 个低面数网格。
Editor/LowPolySceneBuilder.cs：独立生成器，检测到已有场景、材质或网格时拒绝覆盖。
Preview/LowPoly_ArtStyle_Test.png：Unity 相机渲染预览，不是材质贴图。
Validation.txt：验证记录。
本场景在原项目的临时副本中，使用同版本 Unity 编译、生成、重新打开和渲染。
原项目未被编辑器打开；仅新增 Assets/ArtStyleTest/ 及对应目录 meta。

## Play 模式自由观察
打开测试场景，点击 Play，再点击 Game 窗口获取输入焦点。
- WASD：沿视角前后移动、左右平移。
- Q / E：沿世界竖直方向下降 / 上升。
- Shift：按住加速（默认三倍）。
- 鼠标右键：按住并移动鼠标转向，松开恢复鼠标。
- Esc：释放鼠标。
- R：恢复进入 Play 时的初始相机位置与方向。
这是无碰撞的飞行观察相机，可以穿过几何体查看细节。
Main Camera 的 Free Fly Camera 组件可调整速度、加速倍率、鼠标灵敏度和操作提示。
使用原项目已有的旧 Input Manager，不修改输入或项目设置。
FreeCameraValidation.txt 记录实际 Play 模式中的程序化验证；
没有模拟物理键盘或鼠标输入。
## 边缘清晰的窗光
06_Window_Light_Beams 下有两束斜向窗光。
窗光使用独立低面数几何体与简单的加法透明 Shader，无纹理与后处理。
光束边缘清晰，沿长度变淡，并带有窗口亮面与地面光斑。
场景实体仍使用 Standard 受光材质。
Materials/WindowBeam_Warm.mat 的 Light Color and Opacity 可调整光束颜色和透明度；
Alpha 越大，光束越明显。WindowSource_Warm 和 WindowFloor_Light 分别控制窗口与落地光斑。
可在 Hierarchy 中关闭 06_Window_Light_Beams，比较效果。
这是风格化近似：光束不计算真实体积光遮挡；调整方向光后，光束几何不会自动跟随。
未改动渲染管线、Quality 或 Graphics 设置。