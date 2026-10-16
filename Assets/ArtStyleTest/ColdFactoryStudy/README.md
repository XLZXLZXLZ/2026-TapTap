# 冷色工业工厂美术场景

场景：Assets/ArtStyleTest/Scenes/ColdFactory_Design_Study.unity
Unity 2021.3.24f1c1 / 当前项目 URP 12.1.11 / Linear。

## 打开与观察
在 Unity Project 窗口双击 ColdFactory_Design_Study.unity。
建议 Game 视图使用 1536 × 1000 或 3:2，初始构图展示完整关卡。
新场景保持单位立方体道路，不创建玩家；机关均为静态美术模型。
Main Camera 已挂载原有 FreeFlyCamera：
Play 后点击 Game 窗口，WASD 移动，Q/E 升降，Shift 加速；
按住右键转向，Esc 释放鼠标，R 恢复初始视角。
本次许可证错误发生在后续编辑器启动时，因此未完成新场景 Play 模式验证。

## 还原内容
浅蓝灰色单位立方体地块，深蓝色工业支架与工厂纵深。
三条青色传送带、浅色发光连接桥、锯齿危险机关、红色弹跳帽、
金黄色伸缩块和开关，以及绿色起终点外观。
01 / 02 / 03 编号设备舱、收纳架、备用红色头盔部件、机械圆环与吊爪。
冷白色顶灯、边缘清晰的轻量几何光锥、黄黑警示条及地面黄色导向线。
仅右上角保留少量垂挂植物。
所有内容都是可直接选择和编辑的三维物体；没有新增玩家或机关行为脚本。
当前画面为已成功渲染的版本；更中性的浅灰微调因授权失败未应用。

## 文件
ColdFactoryStudy/Materials：独立 URP 材质。
ColdFactoryStudy/Meshes：独立道路、机关、机械圆环、标记和光锥网格。
ColdFactoryStudy/Preview/ColdFactory_Design_Study.png：Unity 实际相机渲染，不作为场景贴图。
Editor/ColdFactoryDesignBuilder.cs：独立生成与验证代码，已有目标时拒绝覆盖。
复用 ArtStyleTest 原有圆柱、球体、URP 光束 Shader 和 FreeFlyCamera，只读取、不修改它们。
实体使用 URP Lit，Base Color 为主，无纹理，Metallic 0、Smoothness 0，
关闭高光与环境反射。灯光与雾仅保存在此场景，没有后处理。
几何光锥与发光桥是风格化近似，不计算真实体积散射。
未修改 Graphics、Quality、URP Asset、Package 或任何已有场景。

## 验证与限制
在当前原项目的完整临时副本中，使用同版本 Unity 成功生成、保存、
重新打开并实际渲染新场景；840 个几何对象的材质与网格检查通过，
全部道路地块检查为单位立方体。
首次生成进程退出码 0，输出 1536 × 1000 预览。
后续启动编辑器时，Unity 报告：
No valid Unity Editor license found. Please activate your license.
重试仍失败，因此后续微调及 Play 模式验证未完成。
交付保留首次成功生成的场景与对应脚本；未修改许可证或项目设置。
原项目未由编辑器打开，只复制新增文件。
