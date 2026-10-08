using UnityEngine;

namespace TapTap
{
    [CreateAssetMenu(menuName = "TapTap/Player Configuration")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [Header("基础尺度与移动")]
        [InspectorName("单格长度（世界单位）")]
        [Tooltip("一格对应的世界长度。移动速度、重力、伸头距离和跃起高度等以格为单位的参数会乘以此值。")]
        [Min(0.01f)] public float UnitSize = 1f;
        [InspectorName("重力加速度（格/秒²）")]
        [Tooltip("角色与分离头部向下的重力加速度；实际世界加速度为此值乘以单格长度。")]
        [Min(0.1f)] public float Gravity = 24f;
        [InspectorName("左右移动速度（格/秒）")]
        [Tooltip("左右输入控制下半身的水平速度。伸头上升期间暂停左右操控，到顶或受阻停止后恢复；头身处于磁力连接状态时，头部跟随下半身移动。")]
        [Min(0f)] public float MoveSpeed = 5f;

        [Header("伸头与收头")]
        [InspectorName("合体头身间距（格）")]
        [Tooltip("合体时头部与下半身位置基准的纵向距离；不是两者碰撞体边缘之间的空隙。")]
        [Min(0.1f)] public float JoinedOffset = 0.75f;
        [InspectorName("最大伸头距离（格）")]
        [Tooltip("按住空格时，头部在合体间距之外额外向上伸出的最大距离。上升遇实体地形会提前停住。")]
        [Min(0.1f)] public float MaxExtension = 4.4f;
        [InspectorName("伸头时长（秒）")]
        [Tooltip("头部缓出上升到最大伸出距离的时间。提前松开空格会等伸头结束或受阻后再收头。")]
        [Min(0.02f)] public float ExtensionDuration = 0.45f;
        [InspectorName("收头时长（秒）")]
        [Tooltip("松开空格后，头部缓入缩回的时间。向下触碰地形时会停住并分离，吸附交由下一次空格操作触发。")]
        [Min(0.02f)] public float RetractionDuration = 0.35f;

        [Header("分离后的吸附与合体")]
        [InspectorName("吸附对齐容差（格）")]
        [Tooltip("允许吸附的最大头身水平位置差。下半身还必须位于头部下方，并超过合体间距。")]
        [Min(0.01f)] public float AlignmentTolerance = 0.35f;
        [InspectorName("横向对齐时长（秒）")]
        [Tooltip("吸附开始后，下半身缓出对齐到头部正下方的时间；不控制纵向吸附速度。")]
        [Min(0.02f)] public float AlignmentDuration = 0.18f;
        [InspectorName("吸附起始速度（格/秒）")]
        [Tooltip("下半身向头部移动时的初始纵向速度，随后按缓入曲线过渡到最大速度。")]
        [Min(0.1f)] public float PullStartSpeed = 1f;
        [InspectorName("吸附最大速度（格/秒）")]
        [Tooltip("下半身纵向吸附加速结束后的速度。遇实体地形阻挡会结束吸附并保持分离。")]
        [Min(0.1f)] public float PullMaxSpeed = 12f;
        [InspectorName("吸附加速时长（秒）")]
        [Tooltip("纵向吸附速度从起始速度过渡到最大速度所需的时间，采用缓入曲线。")]
        [Min(0.02f)] public float PullAccelerationTime = 0.45f;
        [InspectorName("远距离吸附阈值（格）")]
        [Tooltip("吸附触发时，头部与下半身的纵向位置差大于此值，使用远距离合体跃起高度；否则使用普通高度。")]
        [Min(0f)] public float DistantThreshold = 5f;
        [InspectorName("普通合体跃起高度（格）")]
        [Tooltip("普通吸附成功合体后，按此高度计算向上的起跳速度。无阻挡的普通收头合体不触发此跃起。")]
        [Min(0f)] public float NormalLaunchHeight = 1f;
        [InspectorName("远距离合体跃起高度（格）")]
        [Tooltip("远距离吸附成功合体后的总跃起高度，替代普通高度，不是叠加值。默认普通为一格、远距离为两格，即额外增加一格。")]
        [Min(0f)] public float DistantLaunchHeight = 2f;
        [InspectorName("吸附尝试冷却（秒）")]
        [Tooltip("每次松开空格尝试吸附后，到允许再次尝试的间隔。冷却期间最多缓存一次新的空格操作。")]
        [Min(0f)] public float RecallCooldown = 0.22f;

        [Header("散落头部的拾取与弹簧交互")]
        [InspectorName("拾取头部时长（秒）")]
        [Tooltip("下半身从同高侧面接触散落头部时，头部平滑飞回身体上方的时间。拾取不会额外起跳；飞行遇地形阻挡时保持分离。")]
        [Min(0.02f)] public float PickupDuration = 0.24f;
        [InspectorName("踩头弹起高度（格）")]
        [Tooltip("下半身或其他可运动实体从上方接触可弹跳的散落头部时，按此高度计算弹起速度。头部从上方落到下半身时执行合体，不使用此高度。")]
        [Min(0f)] public float SpringHeight = 3f;

        [Header("落地反馈与碰撞精度")]
        [InspectorName("落地震屏速度阈值（格/秒）")]
        [Tooltip("普通落地的向下碰撞速度达到此值才触发落地震屏。不控制专门的收头落地或吸附受阻震屏。")]
        [Min(0.1f)] public float LandingShakeSpeed = 5f;
        [InspectorName("合体位置容差（世界单位）")]
        [Tooltip("收头合体、吸附到达终点及位置校正使用的距离容差。此值直接使用世界单位，不乘单格长度。")]
        [Min(0.001f)] public float JoinTolerance = 0.015f;
        [InspectorName("碰撞安全间距（世界单位）")]
        [Tooltip("碰撞检测在接触边缘保留的安全间距，用于避免穿透和数值抖动。此值不乘单格长度。")]
        [Min(0.001f)] public float Skin = 0.01f;

        [Header("死亡与复活过程")]
        [InspectorName("坠落死亡边界（世界 Y）")]
        [Tooltip("头部或下半身的位置 Y 低于此值时判定坠落死亡。它是世界坐标，不是相对关卡底部的距离，也不乘单格长度。")]
        public float DeathBoundary = -15f;
        [InspectorName("死亡抖动时长（秒）")]
        [Tooltip("死亡后角色视觉在原处抖动的时间；不控制相机震屏时长。随后角色缩小并化为光点。")]
        [Min(0.01f)] public float DeathShakeDuration = 0.08f;
        [InspectorName("角色缩小时长（秒）")]
        [Tooltip("死亡抖动结束后，角色视觉缩小到零的时间。头身连接时两部分一起缩小，分离时只处理本次返回的部分。")]
        [Min(0.01f)] public float RespawnCollapseDuration = 0.18f;
        [InspectorName("光点显隐时长（秒）")]
        [Tooltip("角色缩小后光点出现的时间，以及抵达复活点后光点消失的时间。此时长在完整复活流程中使用两次。")]
        [Min(0.01f)] public float RespawnDotDuration = 0.1f;
        [InspectorName("复活飞行时长（秒）")]
        [Tooltip("光点沿弧线飞回复活点的时间。仅为飞行阶段，不含死亡抖动、角色缩小、光点显隐和角色重建。复活不会重置场景。")]
        [Min(0.02f)] public float RespawnDuration = 0.45f;
        [InspectorName("角色重建时长（秒）")]
        [Tooltip("抵达复活点且光点消失后，角色视觉从零恢复原始大小的时间。此阶段结束后恢复操控与碰撞。")]
        [Min(0.01f)] public float RespawnRebuildDuration = 0.2f;

        public float ToWorld(float units) => units * UnitSize;
        public float WorldGravity => Gravity * UnitSize;
        public float LaunchSpeed(float height) =>
            Mathf.Sqrt(2f * WorldGravity * ToWorld(height));
    }
}
