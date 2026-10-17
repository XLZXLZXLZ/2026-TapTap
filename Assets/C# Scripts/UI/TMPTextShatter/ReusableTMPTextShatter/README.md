# Reusable TMP Text Shatter

一个不依赖 DOTween、不会复制整份文本对象的 TMP 分字碎裂组件。它直接操作 TMP 字符网格，因此支持富文本、字体材质、描边、渐变以及多材质字符，并同时适用于 `TextMeshProUGUI` 和世界空间 `TextMeshPro`。

## 安装

任选一种方式：

1. 把整个文件夹放进其他 Unity 项目的 `Packages` 目录。
2. 只把 `Runtime` 文件夹复制进目标项目的 `Assets`。

目标项目需要安装 TextMesh Pro。除此之外没有第三方依赖。

## 使用

1. 在 TMP 对象上添加 `TMP Text Shatter`。
2. `Source` 留空时会自动使用同对象上的 TMP；也可以拖入另一个 TMP。
3. Button、UnityEvent 或 Animation Event 调用公开方法 `Shatter()`。
4. 需要再次使用时调用 `StopAndRestore()`；运行时修改了文字或布局后调用 `Rebuild()`。

## 常用参数

- `Duration Range`：每个字符的动画时长范围。
- `Character Delay`：相邻字符开始碎裂的间隔。
- `Character Order`：从左到右、从右到左、随机或同时。
- `Launch Direction`：字符初始飞出方向，以 TMP 本地坐标表示。
- `Launch Speed Range`：飞出速度范围。
- `Perpendicular Scatter`：垂直于飞出方向的随机散布。
- `Gravity`：本地坐标重力；UI 文本常用较大的像素值，世界空间文本应调小。
- `Rotation Range`：每个字符最终旋转角度范围。
- `End Scale` / `Fade Start`：结束尺寸和开始淡出的归一化时间。
- `Random Seed`：设为非零时，每次碎裂结果一致。
- `Use Unscaled Time`：暂停或转场期间仍然播放。

## 公开 API

```csharp
shatter.Configure(tmp);   // 运行时更换目标文本
shatter.Prepare();        // 提前缓存当前字符网格
shatter.Shatter();        // 播放
shatter.StopAndRestore(); // 停止并还原完整文本
shatter.Rebuild();        // 文本或布局变化后重建缓存
```

`On Shatter Started` 和 `On Shatter Completed` 可直接在 Inspector 里接音效、切场或其他动画。

## 与旧版本的区别

旧版本为每个字符克隆一份完整 TMP，再隐藏其他字形；如果克隆对象也带着碎裂脚本或按钮脚本，很容易递归生成大量完整文本。本版本只修改原 TMP 的字符顶点，不创建字符 GameObject，因此不会发生该问题，也显著减少运行时对象和 GC。

这是纯视觉碎裂，不会给字符创建 Rigidbody 或 Collider。
