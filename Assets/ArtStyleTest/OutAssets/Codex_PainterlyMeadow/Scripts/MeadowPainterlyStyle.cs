using UnityEngine;
[ExecuteAlways,DisallowMultipleComponent]
public sealed class MeadowPainterlyStyle:MonoBehaviour {
 [Range(0,1)]public float cartoonStrength=.84f;
 public Color shadowColour=new Color(.28f,.55f,.49f,1);
 [Range(.1f,1)]public float shadowBrightness=.48f;
 public Color sunlitTint=new Color(1,.98f,.86f,1);
 [Range(.05f,.5f)]public float transitionSoftness=.22f;
 public bool unifyGrassColour;
 public Color grassColour=new Color(.43f,.65f,.31f,1);
 [Range(0,1)]public float grassColourUnity=1;
 [Range(0,1)]public float grassShadowStrength=.18f;
 [Range(0,.2f)]public float grassPatchVariation=.025f;
 public bool grassWarmCoolPalette;
 public Color grassSunlitColour=new Color(.63f,.77f,.40f,1);
 public Color grassShadeColour=new Color(.24f,.46f,.36f,1);
 public bool softenTreeShadows;
 [Range(0,1)]public float treeShadowSoftness=.86f;
 public Color treeCoolShade=new Color(.29f,.46f,.36f,1);
 public Color treeOliveShade=new Color(.47f,.46f,.30f,1);
 void ApplyTrees(){Shader.SetGlobalVector("_MeadowTreeShadeParameters",new Vector4(softenTreeShadows?treeShadowSoftness:0,0,0,0));Shader.SetGlobalColor("_MeadowTreeCoolShade",treeCoolShade.linear);Shader.SetGlobalColor("_MeadowTreeOliveShade",treeOliveShade.linear);}
 public void Apply(){ApplyTrees();Shader.SetGlobalVector("_MeadowToonParameters",new Vector4(cartoonStrength,shadowBrightness,transitionSoftness,0));Shader.SetGlobalColor("_MeadowToonShadow",shadowColour.linear);Shader.SetGlobalColor("_MeadowToonSunlit",sunlitTint.linear);Shader.SetGlobalColor("_MeadowGrassSurfaceColour",grassColour.linear);Shader.SetGlobalColor("_MeadowGrassSunlitColour",grassSunlitColour.linear);Shader.SetGlobalColor("_MeadowGrassShadeColour",grassShadeColour.linear);Shader.SetGlobalVector("_MeadowGrassPaletteSettings",new Vector4(grassWarmCoolPalette?1:0,0,0,0));Shader.SetGlobalVector("_MeadowGrassSurfaceParameters",new Vector4(unifyGrassColour?grassColourUnity:0,grassShadowStrength,grassPatchVariation,.075f));}
 void OnEnable(){Apply();}void OnValidate(){Apply();}void Update(){Apply();}
 void OnDisable(){Shader.SetGlobalVector("_MeadowToonParameters",Vector4.zero);Shader.SetGlobalVector("_MeadowGrassSurfaceParameters",Vector4.zero);Shader.SetGlobalVector("_MeadowGrassPaletteSettings",Vector4.zero);Shader.SetGlobalVector("_MeadowTreeShadeParameters",Vector4.zero);}
}

