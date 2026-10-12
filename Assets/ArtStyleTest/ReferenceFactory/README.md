# 工厂参考图还原场景

场景：Assets/ArtStyleTest/Scenes/Factory_Reference_Study.unity
Unity 2021.3.24f1c1 / Built-in / Linear。

## 打开与观察
在 Unity Project 窗口双击 Factory_Reference_Study.unity。
Game 视图选择 16:9，即可看到依据参考图布置的初始画面。
点击 Play 后点击 Game 窗口：WASD 移动，Q/E 升降，Shift 加速；
按住右键转向，Esc 释放鼠标，R 回到进入 Play 时的初始构图。
Main Camera 采用 19.6° 窄视角透视，接近参考图的横向、压缩纵深构图。
近距离观察时可以在 Inspector 调大 Camera Field of View。
场景是无碰撞的美术观察空间，不是正式关卡。

## 还原重点
青绿色高窗工厂背景、铜褐色分层平台、中央立柱与大型弯折管道。
右侧控制室、梯子、箱体、悬挂线缆、前景草丛与破损边缘。
象牙白低多边形小机器人，包含独立头、躯干、眼镜头、关节及四肢。
暖黄色窗面、半透明几何光束、前景斜向光斑及机器人投影。
435 个几何对象；结构、设备、草丛、灯光与机器人按组组织，可逐项调整。

## 美术与实现
实体使用 Standard 受光纯色材质，Metallic 0、Smoothness 0，无纹理。
亮窗、光束与几何光斑复用已有简单加法透明 Shader。
环境光、线性青绿色雾、实时暖色方向光及青绿色背景补光均保存在此场景。
暖色人物灯仅影响新场景中使用数字层 27 的机器人和展示平台；
未在 TagManager 中定义或更改任何项目图层。
光束与几何光斑为轻量风格化近似，不计算真实体积散射或遮挡。
未使用复杂后处理或安装 Package。
重新搭建的是参考图的构图与风格，不是原图模型或逐像素复制。

## 资源与验证
ReferenceFactory/Materials：本场景独立材质。
ReferenceFactory/Meshes：独立草丛、光束、光斑和破损边缘网格。
ReferenceFactory/Preview/Factory_Reference_Study.png：Unity 实际相机渲染。
Editor/FactoryReferenceBuilder.cs：生成及验证代码；已有目标时拒绝覆盖。
复用 ArtStyleTest 原有圆柱、球体、光束 Shader 和 FreeFlyCamera，只读取、不修改它们。
在完整原项目临时副本中，用同版本 Unity 编译、重新打开并渲染。
实际进入新场景 Play 模式，程序化验证自由相机移动、转向、复位和鼠标清理；
未模拟物理键盘/鼠标输入。
原项目没有由编辑器打开，仅复制新增文件。
