# 黄绿色日光与植物工厂美术场景

场景：Assets/ArtStyleTest/Scenes/OvergrownFactory_Design_Study.unity
Unity 2021.3.24f1c1 / 当前 URP 12.1.11 / Linear。

## 范围
按参考图重建完整横版 2.5D 初始画面。
沿用单位立方体道路、不建玩家、机关只做静态美术外观的标准。
浅绿灰地块、蓝色结构、青色传送带、粉色弹跳帽、金黄色机关、
黄绿色空气感、高窗、远景箱子传送带、货架、垂挂植物与草丛。
墙面工序图案是静态几何图示，没有玩家或玩法行为。

## 打开与操作
在 Unity Project 窗口双击 OvergrownFactory_Design_Study.unity。
Game 视图建议使用 1536 × 1000 或 3:2，初始构图显示完整关卡。
点击 Play，再点击 Game 窗口：
WASD 移动，Q/E 升降，Shift 加速，按住右键转向；
Esc 释放鼠标，R 恢复初始构图。
Main Camera 的 Free Fly Camera 组件可调整速度、灵敏度与操作提示。
相机无碰撞，可以穿过物体观察。

## 分组
01_Cube_Roads_And_Walls：单位立方体道路和墙体。
02_Slate_Supports：蓝色工业支架、斜撑、管道和电气箱。
03_Conveyors_And_Mechanism_Models：传送带、发光桥、危险机关、
粉色弹跳帽、伸缩块、开关、绿色起终点模型。
04_Hanging_Vines：垂挂植物与前景草丛。
05_Sunlit_Green_Factory_Depth：高窗、远景机械、箱子传送带、
货架、绿灯罩、工序图示和灌木剪影。
06_Clerestory_Sunlight_And_Glow：清晰边缘的斜向光束与地块受光条带。

## 材质与光照
实体使用纯色 URP Lit，Metallic 0、Smoothness 0，无纹理；
关闭高光与环境反射。光窗、发光桥和光束使用已有轻量加法透明 Shader。
暖色日光、淡黄绿色远景雾、少量补光均保存在新场景。
几何光束与裁切到立方体表面的受光条带提供清楚的边缘线，
属于风格化近似，不计算真实体积散射或完整光束遮挡。
没有后处理、Package 变更或项目级渲染设置修改。

## 资源与验证
OvergrownFactoryStudy/Materials：独立材质。
OvergrownFactoryStudy/Meshes：单位方块、机关、叶片、工序箭头、光束及受光条带网格。
OvergrownFactoryStudy/Preview/OvergrownFactory_Design_Study.png：Unity 实际相机渲染，不作为场景贴图。
Editor/OvergrownFactoryDesignBuilder.cs：独立生成和验证代码，目标已存在时拒绝覆盖。
只读复用 ArtStyleTest 原有圆柱、球体、光束 Shader 和 FreeFlyCamera。

在当前完整项目的临时副本中，使用同版本 Unity：
成功编译、生成、保存、重新打开和渲染场景；885 个几何对象材质与网格检查通过。
道路对象全部检查为单位立方体。
实际进入 Play 模式，程序化验证自由相机移动、升降、加速、转向、
俯仰限制、复位和鼠标清理；没有模拟物理键盘/鼠标输入。
详见 Validation.txt 与 PlayValidation.txt。
原项目未由编辑器打开，只复制新增文件。
