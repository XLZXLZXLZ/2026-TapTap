# 森林废墟参考美术场景

打开 ../Scenes/Forest_Ruins_Detailed_Study.unity，Game 视图选择 16:9。

参考图重建：低多边形草坡、分层松树、逐块石砌穹顶、门框、碎石、残柱、高架和远处废弃城市。全部为实际可编辑几何，没有使用参考图作为背景。

天空：Forest Sky 材质的程序化蓝青渐变与象牙色云层。
光照：37° 暖方向光、柔和阴影、冷色三色环境光。
雾：Render Settings 的蓝灰色指数平方距离雾。
后处理：Forest Atmosphere 配置包含 ACES、曝光、对比度、白平衡、冷暖分离、轻 Bloom、暗角和 Bokeh 景深。景深焦点 53 米，草地前景与天际线轻柔虚化。

层级 01–06 可分别调整地形、建筑、森林、远景、灯光、后处理。所有资源限定在本美术测试目录，不修改项目共用渲染管线。

Play 自由相机：WASD，Q/E 升降，右键转向，Shift 加速，R 恢复参考构图。

Tools > Art Style Test > Create Detailed Forest Ruins Study 是重建入口，已有场景时会拒绝覆盖。
