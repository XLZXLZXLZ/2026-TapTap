# 折线磁力连接试作

进入游戏后自动为已有玩家挂载 `MagneticLinkRemakeView`，替代旧磁力线的显示。无需修改场景或玩家预制体。旧脚本、特效资源和玩家逻辑保持原样；禁用玩家身上的新版组件即可恢复旧效果。删除本目录可彻底移除此试作。

新版采用五组三角折波（约十处转折）、圆角接缝、细亮芯与两层柔光，以及两个沿线运动的短光梭。折波持续从下半身向头部传播，线身叠加同方向移动的亮带，两端有小光晕并保持固定。吸附拉动时振幅收至 55%，仍保持流动，亮度增强，光梭改为向下半身流动。各层默认优先于地形显示：材质使用 `ZTest Always`、关闭深度写入，并在普通透明物体之后绘制，避免线条嵌入地形后消失。可关闭 `Draw Over Terrain` 恢复正常深度遮挡；屏幕空间 Overlay UI 仍在游戏画面之上。

分离状态下，头身满足 `PlayerController.CanRecall` 的实际吸附判定时，自动淡入黄色预览线：透明度为正式连线的 25%、宽度为 70%，光晕和光梭同步减弱。按空格并实际进入 `Pulling` 后，在约 0.15 秒内平滑增强到正式粗线。已有伸头、悬停、收头过程继续保持正式连接线。

远距离状态沿用玩家配置的 `DistantThreshold`，比较头部高于身体的纵向位置差。满足条件时，预览线与正式连线的亮芯、外层、光晕、端点、光梭全部平滑变红，波动振幅提高到 1.6 倍，空间折波频率和传播速度提高到 1.8 倍。红线额外加粗 15%，波峰以 1.5 次幂收尖，圆角逐步减至尖角。吸附开始后读取 `IsDistantPull`，距离缩短不改变本次远距离吸附的红色表现。离开判定区、合体或死亡后，保留最后有效跨度与颜色完成淡出。关卡转场或角色离场演出开始时立即清理所有线层和缓存跨度，不依赖已停止的模拟时钟完成淡出，避免伸头通关时残留。

动画从现有模拟时钟读取时间，暂停时停止，回溯时沿历史时间反向播放并从恢复的玩家状态重建预览/正式和远近样式。远近波形使用同一模拟时钟分别计算再平滑混合，跨越阈值不会因直接改变时间倍率而突然跳相位。端点、阶段和时钟由适配器读取，纯绘制组件 `FoldedMagneticLink` 只接收世界坐标、显隐、张力、时间、尺度及两种样式强度，不引用任何玩家类型。

辉光来自线条自身的叠加柔光层。特效不创建 Bloom Volume、不切换相机 HDR / 后处理 / Volume Layer Mask，也不改动 Pipeline Asset、Volume Profile 或滤镜资源，避免干扰已有自定义滤镜的输出流程。如果原有渲染配置已启用 Bloom，线条的 HDR 亮芯仍会自然参与其处理。

可在运行时调整：

- 玩家 → `MagneticLinkRemakeView`：Forward Offset（默认沿世界 Z 前移 0.08 个单位尺度）、Fade In Duration（默认 0.1 秒）、Fade Out Duration（默认 0.28 秒）、State Blend Duration（粗细与远近样式过渡，默认 0.15 秒）。
- 玩家子物体 `Folded Magnetic Link (Remake)` → `FoldedMagneticLink`：Core Width、Bend Width、Wave Count（折波数量）、Flow Speed（流动速度）、Irregularity（默认 0.7）、HDR Core Color、Preview Opacity、Preview Width（默认 0.70）、Draw Over Terrain（默认开启）、Distant Amplitude、Distant Frequency、Distant Width（默认 1.15）、Distant Sharpness（默认 1.5）、Distant Core Color。

此试作在编辑器与打包版本中均自动启用，仅在游玩时生成。与仅供编辑器使用的区域线框彼此独立。隔离项目中可通过 `-executeMethod TapTap.MagneticLinkRemake.Editor.MagneticGuideValidation.RunBatch` 验证真实玩家判定、空格吸附、远距离保持、暂停、回溯与死亡淡出，并验证两种引导线在前景地形遮挡下的优先显示，输出六张样式与遮挡截图到 `Logs/MagneticGuide`。
