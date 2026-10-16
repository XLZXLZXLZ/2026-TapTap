using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArtStyleTestTools
{
    public static class OvergrownFactoryDesignBuilder
    {
        const string Root="Assets/ArtStyleTest/OvergrownFactoryStudy";
        public const string ScenePath="Assets/ArtStyleTest/Scenes/OvergrownFactory_Design_Study.unity";
        static Material cream,creamLight,creamDark,steel,steelLight,steelDark,teal,belt,ivory,gold,red,leaf,leafLight,stem,wall,rear,glass,beam,core,halo;
        static Mesh cube,cylinder,sphere,dome,leafMesh,chevron,gear;
        static Transform terrain,structures,mechanisms,plants,background,lighting;
        static readonly HashSet<string> tiles=new HashSet<string>();
        static Vector3 V(float x,float y,float z){return new Vector3(x,y,z);}
        static Transform Group(string name){return new GameObject(name).transform;}

        public static void Generate()
        {
            if(File.Exists(ScenePath)||Directory.Exists(Root))throw new InvalidOperationException("Target exists; refusing overwrite.");
            Directory.CreateDirectory(Root+"/Materials");
            Directory.CreateDirectory(Root+"/Meshes");
            Directory.CreateDirectory(Root+"/Preview");
            AssetDatabase.Refresh();tiles.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            cream=Mat("Sage_Block","#CED7C3");creamLight=Mat("Sage_Block_Light","#D9E0CF");creamDark=Mat("Sage_Block_Shade","#BECBAE");
            steel=Mat("Structure_Slate","#5A7896");steelLight=Mat("Structure_Edge","#8A9FA5");steelDark=Mat("Structure_Shadow","#344D66");
            teal=Mat("Conveyor_Teal","#2799BD");belt=Mat("Belt_Dark","#245564");ivory=Mat("Ivory_Markings","#F3F3DA");
            gold=Mat("Mechanism_Gold","#F5C83E");red=Mat("Bounce_Pad_Pink","#E65D8C");warningRed=Mat("Hazard_Stripe_Red","#E84E4B");
            leaf=Mat("Leaf_Olive","#758D3F");leafLight=Mat("Leaf_Olive_Light","#98AC4E");stem=Mat("Vine_Stem","#66793D");
            wall=Mat("Olive_Back_Wall","#B2BD85");rear=Mat("Distant_Olive_Frame","#7C9658");
            glass=Additive("Warm_Window",new Color(1f,.96f,.53f,.22f));
            beam=Additive("Angular_Sunbeams",new Color(1f,.96f,.52f,.105f));
            core=Additive("Mechanism_Light_Core",new Color(1f,0.98f,0.64f,0.70f));
            halo=Additive("Mechanism_Gold_Halo",new Color(1f,0.73f,0.20f,0.09f));
            cube=BeveledCube(0.035f);SaveMesh(cube,"Beveled_Unit_Cube");
            cylinder=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/ArtStyleTest/Meshes/Cylinder_12Sides.asset");
            sphere=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/ArtStyleTest/Meshes/Sphere_Faceted.asset");
            if(!cylinder||!sphere)throw new InvalidOperationException("Existing primitive meshes unavailable.");
            dome=DomeMesh();SaveMesh(dome,"Faceted_Hemisphere");
            leafMesh=LeafMesh();SaveMesh(leafMesh,"Faceted_Leaf");
            chevron=ChevronMesh();SaveMesh(chevron,"Conveyor_Chevron");
            gear=GearMesh();SaveMesh(gear,"LowPoly_Saw_Wheel");
            terrain=Group("01_Cube_Roads_And_Walls");structures=Group("02_Slate_Supports");
            mechanisms=Group("03_Conveyors_And_Mechanism_Models");plants=Group("04_Hanging_Vines");
            background=Group("05_Sunlit_Green_Factory_Depth");lighting=Group("06_Clerestory_Sunlight_And_Glow");

            RectTiles(1,18,1,1);
            RectTiles(19,24,1,3);
            RectTiles(0,0,0,13);
            RectTiles(25,25,0,16);
            RectTiles(1,8,15,15);
            RectTiles(9,14,14,15);RectTiles(10,13,13,13);RectTiles(11,12,12,12);
            RectTiles(18,21,15,15);
            RectTiles(22,24,14,15);RectTiles(23,23,13,13);
            RectTiles(22,23,12,13);RectTiles(24,24,13,13);
            RectTiles(22,24,10,10);
            RectTiles(11,13,9,9);RectTiles(12,12,5,8);RectTiles(11,13,3,4);
            RectTiles(17,20,9,9);RectTiles(18,19,8,8);
            RectTiles(17,20,5,5);

            Box("Ground_Underframe",V(13,0.15f,0.9f),V(27,0.7f,1.1f),steel,structures);
            foreach(float x in new[]{0.65f,10.65f,12.5f,17.4f,20.4f,25.5f})
                Box("Structural_Column",V(x,x==17.4f?5.45f:x==20.4f?4.8f:8,1.0f),V(x==12.5f?1.1f:0.75f,x==17.4f?10.9f:x==20.4f?9.7f:16.3f,1.15f),steel,structures);
            Box("Upper_Left_Underframe",V(5.0f,14.85f,1),V(8.8f,0.45f,1.2f),steelDark,structures);
            Box("Upper_Center_Underframe",V(12,12.85f,1),V(7.0f,0.40f,1.1f),steel,structures);
            Box("Upper_Right_Underframe",V(20.2f,14.8f,1),V(6.0f,0.48f,1.2f),steelDark,structures);
            Brace("Left_Upper_Diagonal",V(7.8f,14.8f,0.9f),V(9.5f,13.25f,0.9f),0.42f,steel,structures);
            Brace("Center_Upper_Diagonal",V(16.8f,14.8f,0.9f),V(14.6f,12.55f,0.9f),0.42f,steel,structures);
            Brace("Right_Upper_Diagonal",V(21.3f,14.8f,0.9f),V(23.6f,12.4f,0.9f),0.40f,steel,structures);
            for(int i=0;i<6;i++) {
                float y=1.4f+i*2.0f;
                Brace("Central_Diagonal_Brace",V(11.4f,y,0.8f),V(13.6f,y+1.8f,0.8f),0.18f,steelDark,structures);
            }
            Brace("Island_Support",V(17.2f,8.7f,0.9f),V(19.3f,7.3f,0.9f),0.30f,steelDark,structures);
            Brace("Right_Lower_Diagonal",V(19.6f,4.6f,0.9f),V(21.5f,2.7f,0.9f),0.28f,steelDark,structures);
            for(int i=0;i<5;i++)Brace("Foundation_Brace",V(1+i*5,-0.5f,1),V(3+i*5,-2.1f,1),0.3f,steelDark,structures);
            foreach(float x in new[]{1.7f,4.2f,8.1f,15.65f,18.9f,21.7f,24.9f})Handle(x,16.08f);
            Pipe(new[]{V(0.72f,13.6f,-0.6f),V(1.1f,13.6f,-0.6f),V(1.1f,11.25f,-0.6f),V(0.75f,11.25f,-0.6f)},0.075f,steelLight,structures);
            Pipe(new[]{V(0.48f,0.45f,-0.6f),V(0.48f,-0.1f,-0.6f),V(1.8f,-0.1f,-0.6f),V(1.8f,0.45f,-0.6f)},0.07f,steelLight,structures);
            Pipe(new[]{V(24.1f,0.55f,-0.6f),V(25.6f,0.55f,-0.6f),V(25.6f,1.25f,-0.6f)},0.07f,steelLight,structures);
            Cage(V(10.7f,5.6f,0.2f),1.12f);
            Cage(V(11.2f,10.85f,0.9f),1.0f);
            Box("Electrical_Box",V(24.2f,0.42f,-0.8f),V(0.8f,0.68f,0.25f),steel,structures);
            Box("Electrical_Box_Panel",V(24.2f,0.42f,-0.95f),V(0.5f,0.38f,0.06f),steelLight,structures);
            Box("Central_Junction_Box",V(12.75f,7.3f,-0.64f),V(0.48f,0.60f,0.24f),steelLight,structures);
            Box("Cable_Switch_Box",V(12.65f,5.4f,-0.64f),V(0.35f,0.42f,0.24f),steel,structures);
            Pipe(new[]{V(12.7f,7.0f,-0.78f),V(12.7f,6.6f,-0.78f),V(12.85f,6.3f,-0.78f),V(12.7f,5.6f,-0.78f)},0.021f,steelDark,structures);

            Conveyor("Left_Conveyor",6.0f,8.85f,4.35f);
            Conveyor("Central_Conveyor",12.1f,10.35f,6.35f);
            Conveyor("Right_Conveyor",19.2f,6.45f,4.5f);
            Laser("Left_Short",0.83f,4.1f,6.70f);
            Laser("Left_Conveyor_Link",8.28f,10.62f,8.83f);
            Laser("Lower_Left_A",3.8f,5.9f,4.68f);
            Laser("Lower_Left_B",5.9f,8.62f,4.68f);
            Laser("Lower_Left_C",8.62f,10.78f,4.68f);
            Laser("Upper_Right_A",16.8f,19.7f,10.92f);
            Laser("Upper_Right_B",19.7f,22.1f,10.92f);
            Laser("Mid_Right",21.3f,25.15f,8.12f);
            Laser("Lower_Right",16.55f,19.15f,3.55f);
            Laser("Top_Gap",15.2f,16.5f,15.55f);PassThroughFrame();
            Hazard(10.25f,1.52f);Hazard(17.7f,10.02f);
            JumpPad(13.1f,5.02f,0);JumpPad(14.5f,1.52f,0);
            for(int i=0;i<3;i++)Box("Golden_Extension_Block",V(21.7f+i*0.82f,5.50f,0),V(0.8f,0.65f,0.92f),gold,mechanisms);
            Box("Extension_End_Stop",V(21.24f,5.55f,0),V(0.22f,0.74f,1.0f),steelDark,mechanisms);
            Lever(24.3f,3.55f);
            StartPortal(1.40f,1.58f);ExitDoor(24.52f,11.05f);

            Vine(2.05f,16.0f,3.9f,0);Vine(.50f,14.4f,6.2f,1);Vine(.48f,8.1f,7.7f,2);
            Vine(8.7f,16f,1.6f,3);Vine(21.9f,16.0f,1.9f,4);Vine(25.25f,16.0f,4.0f,5);
            Vine(25.35f,11.2f,7.3f,6);Vine(25.32f,4.0f,4.3f,7);
            Vine(20.0f,.9f,1.6f,8);Vine(24.8f,1.0f,1.7f,9);

            BackgroundFactory();
            RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.58f,.62f,.60f);RenderSettings.reflectionIntensity=0;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=new Color(.75f,.78f,.48f);
            RenderSettings.fogStartDistance=48;RenderSettings.fogEndDistance=78;
            var sun=Light("Warm_Greenhouse_Directional_Sun",LightType.Directional,V(2,18,-4),new Color(1,.99f,.94f),1.22f);
            sun.transform.rotation=Quaternion.Euler(42,35,0);sun.shadows=LightShadows.Soft;
            sun.shadowStrength=.72f;sun.shadowBias=.02f;sun.shadowNormalBias=.12f;
            sun.shadowResolution=LightShadowResolution.VeryHigh;RenderSettings.sun=sun;
            var data=sun.gameObject.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            if(!data)data=sun.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            data.usePipelineSettings=false;
            var fill=Light("Golden_Window_Bounce",LightType.Point,V(9,10,5.8f),new Color(1,.94f,.53f),1.4f);
            fill.range=14;fill.shadows=LightShadows.None;
            var lamp=Light("Green_Pendant_Warm_Spot",LightType.Spot,V(6.2f,12.95f,6.8f),new Color(1,.94f,.67f),1.1f);
            lamp.transform.LookAt(V(6.2f,7.5f,3.5f));lamp.spotAngle=70;lamp.range=11;lamp.shadows=LightShadows.Soft;
            var spawn=Light("Start_Portal_Green_Light",LightType.Point,V(1.4f,2.1f,-.6f),new Color(.42f,1,.13f),1.0f);
            spawn.range=3.0f;spawn.shadows=LightShadows.None;

            var cameraObject=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener),typeof(ArtStyleTest.FreeFlyCamera),typeof(UnityEngine.Rendering.Universal.UniversalAdditionalCameraData));
            cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();
            camera.transform.position=V(13,11.0f,-48);
            camera.transform.LookAt(V(13,8.30f,0));
            camera.fieldOfView=20.4f;camera.nearClipPlane=0.1f;camera.farClipPlane=130;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.39f,.46f,.29f);
            camera.allowHDR=false;camera.allowMSAA=true;
            var serialized=new SerializedObject(cameraObject.GetComponent<ArtStyleTest.FreeFlyCamera>());
            serialized.FindProperty("showControls").boolValue=false;
            serialized.FindProperty("moveSpeed").floatValue=8;serialized.ApplyModifiedPropertiesWithoutUndo();
            if(!EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath))throw new IOException("Scene save failed.");
            AssetDatabase.SaveAssets();Validate();
        }
        static void PassThroughFrame()
        {
            var group=Group("Pass_Through_Platform_Model");group.SetParent(mechanisms,false);
            foreach(float x in new[]{15.18f,16.12f})
                Box("Passable_Dark_Side_Rail",V(x,15.53f,0.36f),V(.18f,.42f,1.02f),steel,group);
            for(int i=0;i<5;i++)
                Box("Passable_Ivory_Grate",V(15.29f+i*.17f,15.39f,0.36f),V(.075f,.08f,.90f),ivory,group);
        }
        public static void PolishExtras()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            cube=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Beveled_Unit_Cube.asset");
            cream=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Cream_Block.mat");
            creamLight=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Cream_Block_Light.mat");
            creamDark=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Cream_Block_Shade.mat");
            steel=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Structure_Slate.mat");
            ivory=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Ivory_Markings.mat");
            terrain=GameObject.Find("01_Cube_Roads_And_Walls").transform;
            mechanisms=GameObject.Find("03_Conveyors_And_Mechanism_Models").transform;
            if(!GameObject.Find("Tile_22_13"))RectTiles(22,22,13,13);
            if(!GameObject.Find("Tile_24_13"))RectTiles(24,24,13,13);
            if(!GameObject.Find("Pass_Through_Platform_Model"))PassThroughFrame();
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Faceted_Leaf.asset");
            var revised=LeafMesh();EditorUtility.CopySerialized(revised,existing);UnityEngine.Object.DestroyImmediate(revised);EditorUtility.SetDirty(existing);
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Validate();
        }
        public static void Validate()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            var camera=Camera.main;
            if(!camera||!camera.GetComponent<ArtStyleTest.FreeFlyCamera>())throw new Exception("Missing free camera.");
            var renderers=UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach(var renderer in renderers) {
                if(!renderer.GetComponent<MeshFilter>().sharedMesh)throw new Exception("Missing mesh: "+renderer.name);
                foreach(var material in renderer.sharedMaterials)
                    if(!material||!material.shader||!material.shader.isSupported)throw new Exception("Invalid material: "+renderer.name);
            }
            foreach(var t in GameObject.Find("01_Cube_Roads_And_Walls").GetComponentsInChildren<Transform>())
                if(t.name.StartsWith("Tile_")&&Vector3.Distance(t.localScale,Vector3.one)>0.001f)throw new Exception("Non-cubic terrain: "+t.name);
            var target=new RenderTexture(1536,1000,24);
            var previous=RenderTexture.active;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var preview=new Texture2D(1536,1000,TextureFormat.RGB24,false);
            preview.ReadPixels(new Rect(0,0,1536,1000),0,0);preview.Apply();
            File.WriteAllBytes(Root+"/Preview/OvergrownFactory_Design_Study.png",preview.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(preview);target.Release();UnityEngine.Object.DestroyImmediate(target);
            File.WriteAllText(Root+"/Validation.txt","Unity: "+Application.unityVersion+"\nScene reopened: "+scene.path+
                "\nMeshes/materials supported; renderers: "+renderers.Length+"\nTerrain tiles: unit cubes."+
                "\nCamera render: 1536x1000. FreeFlyCamera attached. No player or game logic.\n");
            AssetDatabase.Refresh();Debug.Log("OVERGROWN_FACTORY_VALIDATION_OK: "+renderers.Length);
        }
        static Material Mat(string name,string html)
        {
            Color color;ColorUtility.TryParseHtmlString(html,out color);
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};
            m.SetFloat("_Metallic",0);m.SetFloat("_Smoothness",0);m.SetFloat("_SpecularHighlights",0);m.SetFloat("_EnvironmentReflections",0);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            AssetDatabase.CreateAsset(m,Root+"/Materials/"+name+".mat");return m;
        }
        static Material Additive(string name,Color color)
        {
            var shader=Shader.Find("ArtStyleTest/Window Light Beam");
            if(!shader||!shader.isSupported)throw new Exception("Existing light shader unavailable.");
            var m=new Material(shader){name=name};m.SetColor("_Color",color);
            AssetDatabase.CreateAsset(m,Root+"/Materials/"+name+".mat");return m;
        }
        static void SaveMesh(Mesh mesh,string name){AssetDatabase.CreateAsset(mesh,Root+"/Meshes/"+name+".asset");}
        static GameObject Shape(string name,Mesh mesh,Vector3 pos,Vector3 scale,Material material,Transform parent)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
            go.transform.position=pos;go.transform.localScale=scale;
            go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;return go;
        }
        static GameObject Box(string name,Vector3 pos,Vector3 scale,Material material,Transform parent){return Shape(name,cube,pos,scale,material,parent);}
        static void RectTiles(int left,int right,int bottom,int top)
        {
            for(int y=bottom;y<=top;y++)for(int x=left;x<=right;x++){
                string key=x+"_"+y;if(!tiles.Add(key))continue;
                var material=(x*11+y*7)%7==0?creamDark:(x+y)%5==0?creamLight:cream;
                float offset=(x>=1&&x<=18&&y==1)||(x>=19&&x<=24&&y>=1&&y<=3)?-0.5f:0f;
                Box("Tile_"+key,V(x+0.5f,y+0.5f+offset,0),Vector3.one,material,terrain);
            }
        }
        static void Rod(string name,Vector3 a,Vector3 b,float radius,Material material,Transform parent)
        {
            var go=Shape(name,cylinder,(a+b)*0.5f,V(radius*2,(b-a).magnitude,radius*2),material,parent);
            go.transform.rotation=Quaternion.FromToRotation(Vector3.up,b-a);
        }
        static void Brace(string name,Vector3 a,Vector3 b,float width,Material material,Transform parent)
        {
            var go=Box(name,(a+b)*0.5f,V(width,(b-a).magnitude,width),material,parent);
            go.transform.rotation=Quaternion.FromToRotation(Vector3.up,b-a);
        }
        static void Pipe(Vector3[] points,float radius,Material material,Transform parent)
        {
            for(int i=0;i<points.Length-1;i++){Rod("Pipe",points[i],points[i+1],radius,material,parent);Shape("Pipe_Joint",sphere,points[i],Vector3.one*radius*2.1f,material,parent);}
        }
        static void Handle(float x,float y)
        {
            Pipe(new[]{V(x-0.62f,y,0.1f),V(x-0.62f,y+0.64f,0.1f),V(x+0.62f,y+0.64f,0.1f),V(x+0.62f,y,0.1f)},0.052f,ivory,structures);
            Box("Handle_Mount",V(x,y+0.20f,0.4f),V(1.08f,0.45f,0.24f),steel,structures);
        }
        static void Cage(Vector3 p,float size)
        {
            Box("Steel_Cage",p,Vector3.one*size,steel,structures);
            Box("Cage_Inset",p+Vector3.back*size*0.51f,V(size*0.78f,size*0.78f,0.035f),steelDark,structures);
            Brace("Cage_Diagonal",p+V(-size*.32f,-size*.32f,-size*.55f),p+V(size*.32f,size*.32f,-size*.55f),size*.12f,steelLight,structures);
        }
        static void Disc(string name,Vector3 p,float diameter,float depth,Material material,Transform parent)
        {
            var go=Shape(name,cylinder,p,V(diameter,depth,diameter),material,parent);go.transform.rotation=Quaternion.Euler(90,0,0);
        }
        static void Conveyor(string name,float x,float y,float length)
        {
            var group=Group(name);group.SetParent(mechanisms,false);
            Box("Belt_Upper_Surface",V(x,y+0.26f,0),V(length-0.45f,0.22f,1.15f),belt,group);
            Box("Dark_Frame",V(x,y-0.08f,0),V(length-0.6f,0.65f,1.17f),steelDark,group);
            Box("Teal_Central_Module",V(x,y,-0.70f),V(length-0.78f,0.69f,0.22f),teal,group);
            foreach(float side in new[]{-1f,1f}) {
                float px=x+side*(length*0.5f-0.40f);
                Disc(side<0?"Left_End_Wheel":"Right_End_Wheel",V(px,y,-0.69f),0.80f,0.24f,steelDark,group);
                Disc("Teal_End_Frame",V(px,y,-0.84f),0.70f,0.08f,teal,group);
                Disc("Ivory_Roller_Rim",V(px,y,-0.895f),0.52f,0.035f,ivory,group);
                Disc("Roller_Teal_Core",V(px,y,-0.923f),0.28f,0.045f,belt,group);
                Box("Roller_Mount",V(px,y-0.38f,0),V(0.35f,0.14f,0.75f),bronzeFallback(),group);
            }
            int arrows=Mathf.Max(2,Mathf.RoundToInt((length-1.3f)/1.15f));
            for(int i=0;i<arrows;i++){
                float px=x-(length-1.8f)*0.5f+(length-1.8f)*(i+0.5f)/arrows;
                Shape("Direction_Chevron",chevron,V(px,y,-0.965f),Vector3.one,ivory,group);
                Shape("Direction_Chevron",chevron,V(px+0.17f,y,-0.965f),Vector3.one,ivory,group);
            }
        }
        static Material bronzeFallback(){return steelDark;}
        static void Laser(string name,float left,float right,float y)
        {
            var group=Group("Light_Bridge_"+name);group.SetParent(mechanisms,false);
            Box("Bright_Rectangular_Core",V((left+right)*.5f,y,-0.38f),V(right-left-0.08f,0.20f,0.20f),core,group);
            var glow=Box("Thin_Hard_Edge_Halo",V((left+right)*.5f,y,-0.35f),V(right-left-0.02f,0.32f,0.34f),halo,group);
            glow.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            foreach(float x in new[]{left,right}) {
                Box("Dark_Emitter",V(x,y,-0.05f),V(0.34f,0.72f,0.80f),steelDark,group);
                Box("Emitter_Gold_Edge",V(x,y,-0.48f),V(0.09f,0.54f,0.045f),gold,group);
                foreach(float dy in new[]{-.23f,.23f})Disc("Emitter_Bolt",V(x+0.08f,y+dy,-0.48f),0.07f,0.025f,gold,group);
            }
        }
        static void Hazard(float x,float baseY)
        {
            var group=Group("Saw_Hazard_Model");group.SetParent(mechanisms,false);
            Box("Warning_Base",V(x,baseY+0.14f,0),V(1.0f,0.28f,0.9f),steelDark,group);
            for(int i=0;i<3;i++){
                var stripe=Box("Coral_Warning_Stripe",V(x-0.32f+i*0.30f,baseY+0.14f,-0.49f),V(0.13f,0.29f,0.02f),warningRed,group);
                stripe.transform.rotation=Quaternion.Euler(0,0,22);
            }
            Shape("Grey_Saw",gear,V(x,baseY+0.50f,0),V(0.90f,0.90f,1),steelLight,group);
            Disc("Saw_Dark_Hub",V(x,baseY+0.50f,-0.14f),0.32f,0.06f,steel,group);
            Disc("Saw_Axle",V(x,baseY+0.50f,-0.19f),0.15f,0.04f,ivory,group);
        }
        static void JumpPad(float x,float y,float z)
        {
            var group=Group("Coral_Bounce_Pad_Model");group.SetParent(mechanisms,false);
            Shape("Bounce_Pad_Base",cylinder,V(x,y+0.08f,z),V(0.95f,0.16f,0.95f),steelLight,group);
            Shape("Bounce_Dome",dome,V(x,y+0.16f,z),V(0.86f,1.05f,0.86f),red,group);
            Box("Bounce_Dark_Window",V(x,y+0.36f,z-0.42f),V(0.46f,0.21f,0.045f),steelDark,group);
        }
        static void Lever(float x,float y)
        {
            var group=Group("Golden_Lever_Switch_Model");group.SetParent(mechanisms,false);
            Box("Switch_Base",V(x,y+0.12f,0),V(1,0.24f,0.9f),steelDark,group);
            Box("Switch_Gold_Rim",V(x,y+0.26f,0),V(0.94f,0.08f,0.82f),gold,group);
            Rod("Lever_Stem",V(x,y+0.28f,0),V(x-0.14f,y+0.75f,0),0.056f,steelDark,group);
            Shape("Lever_Gold_Knob",sphere,V(x-0.14f,y+0.75f,0),Vector3.one*0.25f,gold,group);
            Box("Switch_Front_Button",V(x,y+0.11f,-0.49f),V(0.40f,0.06f,0.03f),ivory,group);
        }
        static void StartPortal(float x,float y)
        {
            var material=Additive("Portal_Green_Glow",new Color(0.45f,1f,0.16f,0.34f));
            var group=Group("Green_Start_Portal_Model");group.SetParent(mechanisms,false);
            Shape("Portal_Ground_Glow",cylinder,V(x,y+0.018f,0),V(1.15f,0.035f,1.15f),material,group);
            var random=new System.Random(71);
            for(int i=0;i<12;i++){
                var p=V(x-0.48f+(float)random.NextDouble()*0.96f,y+0.12f+(float)random.NextDouble()*1.55f,-0.10f);
                Shape("Floating_Green_Mote",sphere,p,Vector3.one*(i%3==0?.12f:.065f),material,group);
            }
            Shape("Subtle_Green_Light_Column",cylinder,V(x,y+0.8f,0.10f),V(0.92f,1.6f,0.92f),Additive("Portal_Air_Glow",new Color(0.45f,1f,0.16f,0.04f)),group).GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
        }
        static void ExitDoor(float x,float y)
        {
            var group=Group("Green_Exit_Door_Model");group.SetParent(mechanisms,false);
            Box("Exit_Dark_Housing",V(x,y+0.75f,0.40f),V(0.87f,1.50f,0.8f),steelDark,group);
            var glow=Additive("Exit_Green_Light",new Color(0.43f,1,0.13f,0.43f));
            foreach(float dx in new[]{-0.30f,0.30f})Box("Exit_Green_Door_Edge",V(x+dx,y+0.58f,-0.04f),V(0.05f,1.02f,0.055f),glow,group);
            Box("Exit_Green_Door_Header",V(x,y+1.08f,-0.04f),V(0.64f,0.06f,0.055f),glow,group);
            Shape("Exit_Green_Indicator",sphere,V(x,y+1.31f,-0.05f),Vector3.one*.13f,glow,group);
            Box("Exit_Green_Glyph",V(x,y+1.19f,-0.05f),V(0.22f,0.06f,0.04f),glow,group);
        }
        static void Vine(float x,float y,float length,int seed)
        {
            var random=new System.Random(seed*29+131);
            var group=Group("Vine_"+seed);group.SetParent(plants,false);
            Vector3 previous=V(x,y,-0.62f);
            int count=Mathf.CeilToInt(length/.38f);
            for(int i=0;i<count;i++){
                float drop=Mathf.Min(length,(i+1)*.38f);
                var p=V(x+Mathf.Sin(i*.55f+seed)*.15f,y-drop,-0.67f-(float)random.NextDouble()*.10f);
                Rod("Vine_Stem",previous,p,.019f,stem,group);
                float side=i%2==0?1:-1;
                var leafObject=Shape("LowPoly_Leaf",leafMesh,p+V(side*.14f,-.05f,-.03f),
                    V(.83f+(float)random.NextDouble()*.25f,.93f+(float)random.NextDouble()*.25f,1),
                    i%3==0?leafLight:leaf,group);
                leafObject.transform.rotation=Quaternion.Euler((float)random.NextDouble()*24-12,0,side*32);
                previous=p;
            }
        }
        static Material warningRed;
        static void BackgroundFactory()
        {
            var silhouette=Mat("Distant_Olive_Silhouette","#798B50");
            var distant=Mat("Far_Machine_Silhouette","#8E9C5D");
            var shrub=Mat("Bush_Shadow_Green","#617C3F");
            var grass=Mat("Grass_Sunlit_Olive","#92A640");
            var boxMat=Mat("Distant_Box_Sage","#9DA765");
            var wallIcon=Mat("Wall_Process_Diagram","#78894E");
            Box("Olive_Factory_Back_Wall",V(13,8.2f,10),V(31,21,.5f),wall,background);
            Box("Rear_Sunlit_Floor",V(13,1.6f,7.3f),V(31,.20f,9),creamDark,background);
            Clerestory(5.65f,13.1f,5.8f,1.7f);
            Clerestory(12.6f,13.2f,4.0f,1.65f);
            Clerestory(21.0f,13.1f,5.7f,1.7f);
            foreach(float x in new[]{1.3f,9.3f,17.8f,25.1f})
                Box("Soft_Background_Pillar",V(x,8.3f,9.1f),V(.45f,17,.45f),distant,background);
            Box("Rear_Upper_Crossbeam",V(13,14.7f,8.9f),V(29,.35f,.4f),distant,background);
            Box("Left_Far_Machine",V(3.1f,6.1f,8.6f),V(2.15f,4.6f,1.5f),silhouette,background);
            Box("Left_Far_Machine_Step",V(2.3f,7.0f,8.6f),V(1.45f,2.4f,1.5f),silhouette,background);
            Box("Left_Far_Machine_Top",V(3.45f,8.65f,8.6f),V(1.05f,1.2f,1.1f),silhouette,background);
            Box("Right_Far_Machine",V(23.6f,7.0f,8.7f),V(2.1f,6.0f,1.3f),distant,background);
            Box("Right_Far_Machine_Head",V(23.5f,10.1f,8.7f),V(1.35f,.8f,1.3f),distant,background);
            FarConveyor(7.0f,5.1f,5.7f,silhouette,boxMat);
            Shelf(15.5f,3.0f,2.4f,3.1f,silhouette,boxMat);
            Box("Far_Upper_Cabinet",V(20.6f,10.7f,9.0f),V(3.6f,3.3f,.30f),distant,background);
            Box("Far_Upper_Cabinet_Panel",V(20.6f,10.7f,8.8f),V(3.25f,2.95f,.08f),wall,background);
            Pendant(6.2f,13.0f,silhouette);
            Diagram(wallIcon);
            Bush(2.7f,2.0f,5.6f,3.0f,shrub);
            Bush(5.4f,2.5f,5.8f,2.7f,silhouette);
            Bush(10.5f,2.6f,5.7f,2.8f,shrub);
            Bush(16.9f,2.2f,5.8f,3.2f,silhouette);
            Bush(23.6f,2.7f,6.4f,3.2f,shrub);
            Bush(.9f,11.8f,6.5f,2.5f,silhouette);
            SmallGrass(1.5f,1.6f,-.1f,.60f,grass);
            SmallGrass(13.6f,1.57f,-.15f,.55f,grass);
            SmallGrass(18.3f,1.57f,.1f,.65f,grass);
            SmallGrass(23.6f,.45f,-.2f,.55f,grass);
            foreach(Vector3 p in new[]{V(3.3f,2.1f,3.3f),V(8.2f,2.2f,3.3f),V(15.8f,3.1f,3.3f)})
                Pipe(new[]{p+V(-.85f,0,0),p+V(-.85f,.75f,0),p+V(.85f,.75f,0),p+V(.85f,0,0)},.045f,silhouette,background);
            for(int x=1;x<=18;x++)
                Box("Ground_Golden_Guidance_Line",V(x+.5f,1.0f,-.528f),V(.96f,.06f,.018f),gold,structures);
            SunShaft("Left_Clerestory_Diagonal_Sunlight",V(3.2f,13.8f,5.2f),V(5.8f,13.8f,5.2f),V(17.0f,2.0f,5.2f),V(14.4f,2.0f,5.2f),1);
            SunShaft("Center_Clerestory_Diagonal_Sunlight",V(9.5f,14.2f,5.6f),V(11.7f,14.2f,5.6f),V(23.2f,3.0f,5.6f),V(21.0f,3.0f,5.6f),.7f);
            SunShaft("Lower_Warm_Sunlight",V(2.5f,8.6f,4.5f),V(4.6f,8.6f,4.5f),V(11.9f,1.9f,4.5f),V(9.8f,1.9f,4.5f),.4f);
            TileSunlightAccents();
        }
        static void Clerestory(float x,float y,float width,float height)
        {
            Box("High_Window_Luminous_Panes",V(x,y,9.45f),V(width,height,.04f),glass,background);
            foreach(float offset in new[]{-width*.5f,-width*.25f,0,width*.25f,width*.5f})
                Box("High_Window_Mullion",V(x+offset,y,9.34f),V(.08f,height+.14f,.15f),rear,background);
            foreach(float offset in new[]{-height*.5f,0,height*.5f})
                Box("High_Window_Transom",V(x,y+offset,9.34f),V(width+.12f,.08f,.15f),rear,background);
        }
        static void FarConveyor(float x,float y,float width,Material material,Material boxes)
        {
            Box("Distant_Conveyor_Frame",V(x,y,8.0f),V(width,.25f,1),material,background);
            for(int i=0;i<8;i++)Disc("Distant_Conveyor_Roller",V(x-width*.45f+i*width*.9f/7,y,7.42f),.28f,.10f,material,background);
            foreach(float side in new[]{-1f,1f})Box("Distant_Conveyor_Leg",V(x+side*width*.35f,y-1.0f,8),V(.15f,2,1),material,background);
            for(int i=0;i<3;i++)Box("Distant_Carton",V(x-1.8f+i*1.6f,y+.56f,7.8f),V(.85f,.85f,.85f),boxes,background);
        }
        static void Shelf(float x,float y,float width,float height,Material frame,Material boxes)
        {
            foreach(float side in new[]{-1f,1f})Box("Storage_Rack_Post",V(x+side*width*.5f,y+height*.5f,7.2f),V(.14f,height+.30f,.55f),frame,background);
            for(int i=0;i<3;i++)Box("Storage_Rack_Shelf",V(x,y+i*height*.47f,7.2f),V(width+.15f,.14f,1.10f),frame,background);
            for(int row=0;row<2;row++)for(int i=0;i<2;i++)Box("Rack_Carton",V(x-.5f+i,y+.46f+row*height*.47f,7.1f),V(.82f,.82f,.82f),boxes,background);
        }
        static void Pendant(float x,float y,Material shade)
        {
            Rod("Hanging_Lamp_Cable",V(x,y+.45f,7.0f),V(x,y+2.2f,7.0f),.035f,shade,background);
            Shape("Green_Pendant_Shade",dome,V(x,y,7.0f),V(1.20f,.90f,1.20f),shade,background);
            Box("Pendant_Warm_Light",V(x,y+.012f,7),V(.9f,.025f,.70f),glass,background);
        }
        static void Diagram(Material ink)
        {
            float y=10.95f,z=8.9f;
            Shape("Wall_Diagram_Part_Head",dome,V(5.45f,y+.04f,z),V(.55f,.55f,.06f),ink,background);
            Box("Wall_Diagram_Part_Body",V(5.45f,y-.14f,z),V(.48f,.24f,.06f),ink,background);
            foreach(float side in new[]{-1f,1f}){
                Box("Wall_Diagram_Part_Arm",V(5.45f+side*.36f,y-.12f,z),V(.10f,.23f,.06f),ink,background);
                Box("Wall_Diagram_Eye",V(5.45f+side*.10f,y+.15f,z-.04f),V(.045f,.08f,.018f),creamLight,background);
            }
            Rod("Wall_Diagram_Antenna",V(5.45f,y+.30f,z),V(5.45f,y+.44f,z),.025f,ink,background);
            ProcessArrow(6.45f,y+.02f,z,ink);
            Box("Wall_Diagram_Open_Box",V(7.35f,y-.07f,z),V(.62f,.48f,.05f),ink,background);
            foreach(float side in new[]{-1f,1f}){
                var flap=Box("Wall_Diagram_Box_Flap",V(7.35f+side*.22f,y+.23f,z),V(.45f,.12f,.05f),ink,background);
                flap.transform.rotation=Quaternion.Euler(0,0,side*22);
            }
            ProcessArrow(8.30f,y+.02f,z,ink);
            Box("Wall_Diagram_Closed_Box",V(9.17f,y,z),V(.66f,.66f,.06f),ink,background);
            Box("Wall_Diagram_Box_Seam",V(9.17f,y+.15f,z-.04f),V(.035f,.30f,.015f),wall,background);
        }
        static void ProcessArrow(float x,float y,float z,Material material)
        {
            Box("Process_Diagram_Arrow_Shaft",V(x,y,z),V(.34f,.095f,.04f),material,background);
            var v=new List<Vector3>();var t=new List<int>();
            Triangle(v,t,V(x+.10f,y+.17f,z-.025f),V(x+.34f,y,z-.025f),V(x+.10f,y-.17f,z-.025f),Vector3.back);
            var mesh=Finish("Process_Diagram_Arrow",v,t);SaveMesh(mesh,"Process_Diagram_Arrow_"+x.ToString("0"));
            Shape("Process_Diagram_Arrowhead",mesh,Vector3.zero,Vector3.one,material,background);
        }
        static void Bush(float x,float y,float z,float width,Material material)
        {
            var root=Group("Distant_Foliage_Cluster");root.SetParent(background,false);
            for(int i=0;i<9;i++){
                float a=(-70f+i*17.5f)*Mathf.Deg2Rad;
                var go=Shape("Broad_Leaf_Silhouette",leafMesh,V(x+Mathf.Sin(a)*width*.25f,y+Mathf.Cos(a)*width*.17f,z),
                    V(width*.65f,width*.95f,.8f),material,root);
                go.transform.rotation=Quaternion.Euler(0,0,-a*Mathf.Rad2Deg);
            }
        }
        static void SmallGrass(float x,float y,float z,float scale,Material material)
        {
            var root=Group("Grass_On_Platform_Edge");root.SetParent(plants,false);
            for(int i=0;i<7;i++){
                var go=Shape("Pointed_Grass_Leaf",leafMesh,V(x+(i-3)*scale*.08f,y+scale*.20f,z),
                    V(scale*.7f,scale*1.5f,.65f),material,root);
                go.transform.rotation=Quaternion.Euler(0,0,(i-3)*16);
            }
        }
        static List<Vector3> ClipLightBand(List<Vector3> input,float edge,bool lower)
        {
            var output=new List<Vector3>();
            for(int i=0;i<input.Count;i++){
                var a=input[i];var b=input[(i+1)%input.Count];
                float da=a.x+a.y-edge,db=b.x+b.y-edge;
                bool ia=lower?da>=0:da<=0,ib=lower?db>=0:db<=0;
                if(ia)output.Add(a);
                if(ia!=ib)output.Add(Vector3.Lerp(a,b,da/(da-db)));
            }
            return output;
        }
        static void TileSunlightAccents()
        {
            var material=Additive("Golden_Light_On_Cube_Faces",new Color(1,.95f,.57f,.16f));
            var vertices=new List<Vector3>();var indices=new List<int>();
            foreach(var tile in terrain.GetComponentsInChildren<Transform>()){
                if(!tile.name.StartsWith("Tile_")||tile.position.x<9||tile.position.x>16||tile.position.y<12)continue;
                var p=tile.position;
                foreach(float band in new[]{23.5f,25.4f}){
                    var polygon=new List<Vector3>{p+V(-.455f,-.455f,-.54f),p+V(.455f,-.455f,-.54f),p+V(.455f,.455f,-.54f),p+V(-.455f,.455f,-.54f)};
                    polygon=ClipLightBand(polygon,band,true);
                    if(polygon.Count>0)polygon=ClipLightBand(polygon,band+.68f,false);
                    for(int i=1;i<polygon.Count-1;i++)Triangle(vertices,indices,polygon[0],polygon[i],polygon[i+1],Vector3.back);
                }
            }
            var mesh=Finish("Clipped_Sunlight_On_Unit_Cubes",vertices,indices);SaveMesh(mesh,"Clipped_Sunlight_On_Unit_Cubes");
            var go=Shape("Hard_Edge_Golden_Tile_Lighting",mesh,Vector3.zero,Vector3.one,material,lighting);
            go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
        }

        static void Window(float x,float y,float width,float height)
        {
            Box("Warm_Window_Frame",V(x,y,8.5f),V(width+.26f,height+.26f,.15f),creamDark,background);
            Box("Golden_Window_Panes",V(x,y,8.38f),V(width,height,.035f),glass,background);
            Box("Window_Central_Mullion",V(x,y,8.30f),V(.085f,height,.12f),wall,background);
            foreach(float offset in new[]{-height/6f,height/6f})Box("Window_Horizontal_Mullion",V(x,y+offset,8.30f),V(width,.085f,.12f),wall,background);
        }
        static Light Light(string name,LightType type,Vector3 p,Color color,float intensity)
        {
            var go=new GameObject(name,typeof(Light));go.transform.SetParent(lighting,false);go.transform.position=p;
            var light=go.GetComponent<Light>();light.type=type;light.color=color;light.intensity=intensity;
            light.lightmapBakeType=LightmapBakeType.Realtime;light.renderMode=LightRenderMode.ForcePixel;return light;
        }
        static void SunShaft(string name,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float opacity)
        {
            var mesh=new Mesh{name=name};mesh.vertices=new[]{a,b,c,d};
            mesh.colors=new[]{new Color(1,1,1,opacity),new Color(1,1,1,opacity),new Color(1,1,1,opacity*.4f),new Color(1,1,1,opacity*.4f)};
            mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
            SaveMesh(mesh,name);
            var go=Shape(name,mesh,Vector3.zero,Vector3.one,beam,lighting);
            go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;go.GetComponent<MeshRenderer>().receiveShadows=false;
        }

        static void Quad(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),outward)<0){var swap=b;b=d;d=swap;}
            int i=v.Count;v.AddRange(new[]{a,b,c,d});t.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
        }
        static void Triangle(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),outward)<0){var swap=b;b=c;c=swap;}
            int i=v.Count;v.AddRange(new[]{a,b,c});t.AddRange(new[]{i,i+1,i+2});
        }
        static Mesh Finish(string name,List<Vector3> v,List<int> t)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static Mesh BeveledCube(float bevel)
        {
            var v=new List<Vector3>();var t=new List<int>();float h=.5f-bevel;
            var axes=new[]{Vector3.right,Vector3.up,Vector3.forward};
            foreach(var axis in axes)foreach(float sign in new[]{-1f,1f}){
                var normal=axis*sign;var u=Mathf.Abs(normal.x)>.5f?Vector3.up:Vector3.right;
                var w=Vector3.Cross(normal,u);var p=normal*.5f;
                Quad(v,t,p-u*h-w*h,p+u*h-w*h,p+u*h+w*h,p-u*h+w*h,normal);
            }
            for(int run=0;run<3;run++)foreach(float s1 in new[]{-1f,1f})foreach(float s2 in new[]{-1f,1f}){
                var r=axes[run];var a=axes[(run+1)%3]*s1;var b=axes[(run+2)%3]*s2;
                Quad(v,t,-r*h+a*.5f+b*h,r*h+a*.5f+b*h,r*h+a*h+b*.5f,-r*h+a*h+b*.5f,a+b);
            }
            foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
                Triangle(v,t,V(x*.5f,y*h,z*h),V(x*h,y*.5f,z*h),V(x*h,y*h,z*.5f),V(x,y,z));
            return Finish("Beveled_Unit_Cube",v,t);
        }
        static Vector3 DomePoint(int side,int ring)
        {
            float phi=Mathf.PI*.5f*ring/4f,theta=Mathf.PI*2*side/12f;
            return V(Mathf.Sin(phi)*Mathf.Cos(theta),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(theta))*.5f;
        }
        static Mesh DomeMesh()
        {
            var v=new List<Vector3>();var t=new List<int>();
            for(int y=0;y<4;y++)for(int x=0;x<12;x++){
                var a=DomePoint(x,y);var b=DomePoint(x+1,y);var c=DomePoint(x+1,y+1);var d=DomePoint(x,y+1);
                if(y==0)Triangle(v,t,a,c,d,a+c+d);else Quad(v,t,a,b,c,d,a+b+c+d);
            }
            for(int x=0;x<12;x++)Triangle(v,t,Vector3.zero,DomePoint(x,4),DomePoint(x+1,4),Vector3.down);
            return Finish("Faceted_Hemisphere",v,t);
        }
        static Mesh LeafMesh()
        {
            var v=new List<Vector3>();var t=new List<int>();
            var edges=new[]{V(0,.25f,0),V(-.12f,.13f,0),V(-.14f,-.09f,0),V(0,-.23f,0),V(.14f,-.09f,0),V(.12f,.13f,0)};
            for(int i=0;i<edges.Length;i++){
                Triangle(v,t,edges[i],edges[(i+1)%edges.Length],V(0,0,-.045f),Vector3.back);
                Triangle(v,t,edges[i],edges[(i+1)%edges.Length],V(0,0,.025f),Vector3.forward);
            }
            return Finish("Faceted_Leaf",v,t);
        }
        static Mesh ChevronMesh()
        {
            var v=new List<Vector3>();var t=new List<int>();
            Quad(v,t,V(-.10f,.20f,0),V(.06f,0,0),V(-.02f,-.02f,0),V(-.16f,.15f,0),Vector3.back);
            Quad(v,t,V(-.16f,-.15f,0),V(-.02f,.02f,0),V(.06f,0,0),V(-.10f,-.20f,0),Vector3.back);
            return Finish("Direction_Chevron",v,t);
        }
        static Mesh GearMesh()
        {
            var v=new List<Vector3>();var t=new List<int>();var outline=new Vector3[48];
            for(int i=0;i<48;i++){
                float angle=Mathf.PI*2*i/48f;float radius=i%4<2?.5f:.415f;
                outline[i]=V(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,0);
            }
            for(int i=0;i<48;i++){
                var a=outline[i];var b=outline[(i+1)%48];var front=Vector3.back*.10f;var back=Vector3.forward*.10f;
                Triangle(v,t,front,a+front,b+front,Vector3.back);
                Triangle(v,t,back,b+back,a+back,Vector3.forward);
                Quad(v,t,a+front,b+front,b+back,a+back,a+b);
            }
            return Finish("LowPoly_Saw_Wheel",v,t);
        }
    }
}

