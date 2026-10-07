using UnityEngine;

namespace TapTap
{
    [CreateAssetMenu(menuName = "TapTap/Player Visual Configuration")]
    public sealed class PlayerVisualConfig : ScriptableObject
    {
        [Header("特效基础资源")]
        [InspectorName("光点精灵"), Tooltip("生成磁力线光点、复活光点等特效预制体时使用的精灵资源。")]
        [HideInInspector] public Sprite DotSprite;
        [InspectorName("线条特效材质"), Tooltip("生成磁力连接线、吸附引导线及复活拖尾时使用的材质。")]
        [HideInInspector] public Material EffectsMaterial;
        [InspectorName("粒子材质"), Tooltip("生成落地与合体粒子特效预制体时使用的材质。")]
        [HideInInspector] public Material ParticleMaterial;

        [Header("磁力连接线与光点")]
        [InspectorName("磁力线颜色"), Tooltip("生成磁力连接线和沿线光点时使用的基础颜色与透明度。")]
        [HideInInspector] public Color MagneticColor = new Color(0.52f, 0.85f, 1f, 0.65f);
        [InspectorName("磁力线宽度"), Tooltip("磁力连接线的基础宽度；显示时会随角色尺度缩放。")]
        [HideInInspector] [Min(0.002f)] public float MagneticWidth = 0.025f;
        [InspectorName("磁力线波动幅度"), Tooltip("磁力连接线横向波动的基础幅度；显示时会随角色尺度和头身距离调整。")]
        [HideInInspector] [Min(0f)] public float MagneticAmplitude = 0.075f;
        [InspectorName("磁力线流动速度"), Tooltip("控制磁力线波动和沿线光点流动的时间倍率；不是角色的移动或吸附速度。")]
        [HideInInspector] [Min(0f)] public float MagneticSpeed = 2.1f;
        [InspectorName("磁力线分段数"), Tooltip("磁力线的线段数量。数值越高，波动曲线越细腻。")]
        [HideInInspector] [Range(3, 64)] public int MagneticSegments = 18;
        [InspectorName("磁力线光点数量"), Tooltip("生成磁力连接特效预制体时，沿线流动的光点数量。")]
        [HideInInspector] [Range(1, 12)] public int MagneticDotCount = 5;
        [InspectorName("磁力线光点大小"), Tooltip("生成沿线光点时的基础大小。运行时还会随沿线位置产生缩放变化。")]
        [HideInInspector] [Min(0.01f)] public float MagneticDotSize = 0.065f;

        [Header("落地与合体粒子")]
        [InspectorName("落地粒子数量"), Tooltip("生成落地特效时，一次接触爆发的粒子数量。")]
        [HideInInspector] [Range(1, 24)] public int LandingParticles = 7;
        [InspectorName("合体粒子数量"), Tooltip("生成合体特效时，一次拼装接触爆发的粒子数量。")]
        [HideInInspector] [Range(1, 24)] public int AssemblyParticles = 10;
        [InspectorName("粒子基础寿命（秒）"), Tooltip("生成粒子特效时使用的基础寿命；实际出生寿命在此值的百分之七十五至百分之一百一十五之间随机。")]
        [HideInInspector] [Min(0.02f)] public float ParticleLifetime = 0.28f;
        [InspectorName("粒子基础大小"), Tooltip("生成粒子特效时使用的基础大小；实际出生大小在此值的百分之七十至百分之一百二十之间随机。")]
        [HideInInspector] [Min(0.01f)] public float ParticleSize = 0.055f;
        [InspectorName("粒子基础速度"), Tooltip("生成粒子特效时使用的基础速度；实际出生速度在此值的百分之六十五至百分之一百一十五之间随机。")]
        [HideInInspector] [Min(0.01f)] public float ParticleSpeed = 2.1f;
        [InspectorName("粒子发射锥角（度）"), Tooltip("生成落地与合体粒子特效时使用的锥形发射角度。")]
        [HideInInspector] [Range(1f, 85f)] public float ParticleConeAngle = 38f;

        [Header("接触形变与恢复")]
        [InspectorName("落地挤压幅度"), Tooltip("接触地面时角色视觉横向变宽、纵向变扁的基础比例；实际幅度随碰撞速度调整，也用于分离和吸附失败的小幅反馈。仅影响视觉，不改变碰撞体。")]
        [Range(0f, 0.4f)] public float LandingSquash = 0.12f;
        [InspectorName("合体挤压幅度"), Tooltip("头身接触拼装时下半身视觉挤压的比例，头部采用较小幅度。也作为伸头和踩头弹起反馈的强度基准。")]
        [Range(0f, 0.4f)] public float AssemblySquash = 0.16f;
        [InspectorName("踩头弹簧形变幅度"), Tooltip("散落头部被从上方踩到时，先横向变宽、纵向压扁，再横向变窄、纵向拉长，最后恢复。仅影响视觉，不改变碰撞体。")]
        [Range(0f, 0.4f)] public float SpringSquash = 0.2f;
        [InspectorName("运动拉伸幅度"), Tooltip("下半身随纵向速度拉长、变窄的最大比例；头部在伸头和吸附期间使用此拉伸幅度。仅影响视觉，不改变实际伸头距离或跃起高度。")]
        [Range(0f, 0.4f)] public float MotionStretch = 0.05f;
        [InspectorName("形变恢复时长（秒）"), Tooltip("一次挤压或拉伸反馈恢复到原始形状的时间，采用缓出曲线；不控制伸头、收头或吸附动作的时长。")]
        [Min(0.02f)] public float InteractionDuration = 0.22f;

        [Header("呼吸起伏")]
        [InspectorName("呼吸形变幅度"), Tooltip("呼吸时下半身纵向缩放的基础比例，并伴随较小的横向变化。头部采用较小幅度，水平移动时整体呼吸幅度减弱。")]
        [Range(0f, 0.03f)] public float BreathingAmplitude = 0.008f;
        [InspectorName("呼吸频率（次/秒）"), Tooltip("每秒完成的呼吸起伏周期数。设为零时停止呼吸相位推进。")]
        [Min(0f)] public float BreathingFrequency = 0.6f;

        [Header("磁力连接时的移动姿态")]
        [InspectorName("移动视觉滞后距离（格）"), Tooltip("头身拉开但仍由磁力连接时，满速水平移动造成的最大视觉滞后距离。头部使用完整幅度，下半身使用百分之二十；不改变实际位置或碰撞。")]
        [Range(0f, 0.2f)] public float MagneticLagDistance = 0.075f;
        [InspectorName("姿态跟随平滑时间（秒）"), Tooltip("视觉位移滞后和头部倾斜的平滑时间常数。值越大，跟随越慢；不是固定动画完成时长，也不控制下半身倾斜的平滑速度。")]
        [Min(0.01f)] public float MagneticLagSmoothTime = 0.1f;
        [InspectorName("连接移动倾斜角（度）"), Tooltip("头身拉开且保持磁力连接时，下半身满速水平移动的目标倾斜角度。静止时回正，向左和向右的倾斜方向相反。")]
        [Range(0f, 10f)] public float MagneticLeanAngle = 5f;
        [InspectorName("头部倾斜倍率"), Tooltip("磁力连接拉开时，头部目标倾斜角相对下半身目标倾斜角的倍率；零表示头部不随水平移动倾斜。")]
        [Range(0f, 1f)] public float MagneticHeadLeanMultiplier = 0.75f;

        [Header("复活光点与拖尾")]
        [InspectorName("复活光点大小"), Tooltip("生成复活光点预制体时的基础大小；光点飞行及显隐时间由角色控制配置决定。")]
        [HideInInspector] [Min(0.01f)] public float RespawnDotSize = 0.13f;
        [InspectorName("复活拖尾保留时间（秒）"), Tooltip("生成复活光点预制体时，拖尾中的轨迹保留多久；不是光点飞回复活点的时长。")]
        [HideInInspector] [Min(0.01f)] public float RespawnTrailTime = 0.18f;
    }
}
