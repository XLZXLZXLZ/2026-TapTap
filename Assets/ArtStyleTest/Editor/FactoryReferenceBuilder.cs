using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArtStyleTestTools
{
    public static class FactoryReferenceBuilder
    {
        const string Root="Assets/ArtStyleTest/ReferenceFactory";
        public const string ScenePath="Assets/ArtStyleTest/Scenes/Factory_Reference_Study.unity";
        static Material bronze,top,dark,green,back,window,cream,black,grass,grassLight,beam;
        static Mesh cylinder,sphere,grassMesh;
        static Transform architecture,platforms,machines,details,vegetation,lightGroup;
        static int serial;
        public static void Generate()
        {
            if (File.Exists(ScenePath)||Directory.Exists(Root)) throw new InvalidOperationException("Reference study exists; refusing overwrite.");
            Directory.CreateDirectory(Root+"/Materials");
            Directory.CreateDirectory(Root+"/Meshes");
            Directory.CreateDirectory(Root+"/Preview");
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            serial=0;
            bronze=Mat("Oxidized_Bronze","#766747");
            top=Mat("Sunlit_Ochre","#AA8C53");
            dark=Mat("Deep_Industrial_Shadow","#293D33");
            green=Mat("Aged_Teal_Paint","#4B715C");
            back=Mat("Distant_Green_Wall","#416650");
            cream=Mat("Robot_Warm_Ivory","#DBE0C9");
            black=Mat("Robot_Charcoal","#24312E");
            grass=Mat("Grass_Olive","#829442");
            grassLight=Mat("Grass_YellowGreen","#A4AB4D");
            window=Mat("Window_Pale_Gold","#FFF5AC");
            window.EnableKeyword("_EMISSION");
            window.SetColor("_EmissionColor",new Color(1.05f,1.03f,0.50f));
            var beamShader=Shader.Find("ArtStyleTest/Window Light Beam");
            if (!beamShader||!beamShader.isSupported) throw new InvalidOperationException("Existing beam shader unavailable.");
            beam=new Material(beamShader){name="Hazy_Golden_Window_Shaft"};
            beam.SetColor("_Color",new Color(1,0.88f,0.48f,0.055f));
            AssetDatabase.CreateAsset(beam,Root+"/Materials/Hazy_Golden_Window_Shaft.mat");
            cylinder=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/ArtStyleTest/Meshes/Cylinder_12Sides.asset");
            sphere=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/ArtStyleTest/Meshes/Sphere_Faceted.asset");
            if (!cylinder||!sphere) throw new InvalidOperationException("Existing test meshes missing.");
            architecture=Group("01_Tall_Factory_Architecture");
            platforms=Group("02_Layered_Catwalks");
            machines=Group("03_Machines_And_Crates");
            details=Group("04_Ladders_Pipes_Cables");
            vegetation=Group("05_Reclaimed_Grass");
            lightGroup=Group("06_Warm_Light_And_Teal_Haze");

            Box("Far_Wall",V(0,6,12),V(28,16,0.5f),back,architecture);
            Box("Distant_Floor",V(0,-0.6f,4),V(30,0.5f,22),green,architecture);
            Window(-5.7f,3.5f,4);
            Window(2.1f,3.0f,3);
            Window(9.2f,3.8f,3);
            foreach(float x in new[]{-10.2f,-2.8f,5.2f,11.2f})
            {
                Box("Rear_Pillar",V(x,6.3f,9),V(0.65f,14,0.8f),green,architecture);
                Box("Rear_Pillar_Base",V(x,0.1f,9),V(1.1f,1,1.2f),dark,architecture);
            }
            Box("Rear_Crossbeam",V(0,5.4f,8.7f),V(26,0.3f,0.45f),green,architecture);
            Box("Rear_Lower_Crossbeam",V(0,2.4f,8.7f),V(26,0.22f,0.4f),green,architecture);

            Platform("Foreground",0,1.65f,-2.7f,23,2.2f);
            Platform("Robot_Stage",6.4f,2.32f,-0.15f,7.5f,2.8f);
            Platform("Middle_Walkway",0,2.95f,3.5f,16,2.1f);
            Platform("Left_Offset_Deck",-7.25f,2.35f,0.0f,5.7f,2.9f);
            Platform("Upper_Left_Catwalk",-7.5f,5.65f,4.7f,7.6f,1.15f);
            Platform("Upper_Right_Catwalk",5.5f,5.65f,4.4f,5.8f,1.25f);
            foreach(float x in new[]{-9.5f,-6.2f,-0.7f,4.2f,8.5f})
            {
                Box("Foreground_Support",V(x,0.35f,-2.4f),V(0.38f,2.1f,0.5f),dark,platforms);
                Box("Support_Boot",V(x,-0.5f,-2.4f),V(0.75f,0.2f,0.8f),bronze,platforms);
            }
            foreach(float x in new[]{-6.5f,0.3f,6.6f})
                Box("Middle_Support",V(x,0.8f,3.5f),V(0.45f,4,0.5f),green,platforms);
            // A broken front panel and a chipped silhouette interrupt the long straight platform.
            var chip=Box("Loose_Front_Panel",V(-3.5f,1.1f,-3.84f),V(1.8f,0.7f,0.07f),bronze,platforms);
            chip.transform.rotation=Quaternion.Euler(0,0,-6);
            Box("Detached_Panel_Right",V(5.2f,1.1f,-3.85f),V(1.5f,0.65f,0.08f),bronze,platforms).transform.rotation=Quaternion.Euler(0,0,-5);
            Box("Small_Red_Service_Cover",V(-4.9f,1.9f,-2.55f),V(1.0f,0.28f,0.65f),Mat("Faded_Red","#92604B"),machines);

            // The central column deliberately bisects the space, as in the reference.
            Box("Hero_Central_Column",V(-1.3f,5.6f,2.4f),V(0.78f,12,0.85f),bronze,architecture);
            Box("Column_Shadow_Side",V(-0.94f,5.6f,2.34f),V(0.10f,12,0.92f),dark,architecture);
            for(int i=0;i<6;i++)
                Box("Column_Section",V(-1.3f,1.3f+i*1.7f,1.955f),V(0.8f,0.055f,0.035f),green,architecture);
            Duct("Bent_Left_Duct",new[]{V(-4.0f,5.7f,4.4f),V(-3.15f,6.35f,4.4f),V(-3.15f,11.5f,4.4f)},0.65f,bronze,details);
            Duct("Rear_Thin_Pipe",new[]{V(-9.7f,8.5f,7),V(-5.6f,6.9f,7),V(-5.6f,2.0f,7)},0.26f,green,details);
            Duct("Cross_Factory_Pipe",new[]{V(-9.7f,7.8f,7.7f),V(1.0f,7.8f,7.7f),V(1.0f,3.7f,7.7f),V(-3.5f,3.7f,7.7f)},0.24f,green,details);
            Duct("Descending_Gold_Duct",new[]{V(2.0f,12,5),V(2.0f,8.4f,5),V(1.45f,7.4f,5)},0.5f,bronze,details);
            Box("Duct_Connector",V(2.0f,9.5f,5),V(0.78f,0.85f,0.78f),top,details);
            Box("Right_Gantry_Upright",V(7.4f,5.7f,2.5f),V(0.62f,10.8f,0.85f),dark,architecture);
            Box("Right_Gantry_Top",V(9.0f,9.1f,2.5f),V(3.9f,0.5f,0.85f),dark,architecture);
            Duct("Right_Return_Duct",new[]{V(10.0f,8.0f,4.5f),V(8.5f,6.6f,4.5f),V(8.5f,5.65f,4.5f)},0.68f,bronze,details);

            // Raised control cabin with a four-pane pale green window.
            Box("Control_Cabin",V(3.55f,4.25f,3.45f),V(2.1f,2.2f,1.65f),bronze,machines);
            Box("Cabin_Roof",V(3.55f,5.42f,3.45f),V(2.45f,0.20f,1.95f),dark,machines);
            Box("Cabin_Foot",V(3.55f,3.12f,3.45f),V(2.2f,0.15f,1.75f),dark,machines);
            Box("Cabin_Window_Frame",V(3.8f,4.25f,2.58f),V(0.74f,0.8f,0.06f),dark,machines);
            var glass=Mat("Cabin_Mint_Window","#A5D9AB");
            glass.EnableKeyword("_EMISSION");glass.SetColor("_EmissionColor",new Color(0.15f,0.3f,0.15f));
            Box("Cabin_Window",V(3.8f,4.25f,2.54f),V(0.62f,0.67f,0.025f),glass,machines);
            Box("Cabin_Window_Vertical",V(3.8f,4.25f,2.51f),V(0.055f,0.7f,0.035f),bronze,machines);
            Box("Cabin_Window_Horizontal",V(3.8f,4.25f,2.51f),V(0.66f,0.055f,0.035f),bronze,machines);
            Rod("Cabin_External_Pipe",V(2.42f,3.05f,2.5f),V(2.42f,5.4f,2.5f),0.055f,top,details);
            Ladder(V(5.45f,2.32f,1.9f),V(4.9f,5.85f,2.5f),0.68f,11);
            Ladder(V(-5.35f,2.95f,4),V(-5.35f,5.55f,4),0.5f,10);
            Ladder(V(2.7f,-0.2f,3),V(2.7f,2.95f,3),0.45f,10);
            Crate(V(4.8f,5.95f,4.4f),V(0.66f,0.6f,0.55f));
            Crate(V(6.6f,5.95f,4.4f),V(0.75f,0.6f,0.55f));
            Crate(V(-5.5f,3.12f,3.2f),V(0.3f,0.33f,0.3f));
            Crate(V(2.05f,3.15f,2.9f),V(0.3f,0.4f,0.3f));
            Desk(-7.2f,2.35f,0.3f);
            Desk(-3.1f,2.95f,3.3f);
            Cable(new[]{V(-6.0f,11,4.5f),V(-6.0f,6.7f,4.5f),V(-6.3f,4.2f,4.5f),V(-6.3f,3.1f,4.5f)});
            Cable(new[]{V(-1.0f,11,4.8f),V(-0.8f,8,4.8f),V(-1.0f,6.5f,4.8f)});
            SignalRobot();
            Robot(V(5.5f,2.38f,-0.35f));

            grassMesh=GrassMesh();
            AssetDatabase.CreateAsset(grassMesh,Root+"/Meshes/Grass_Clump.asset");
            var random=new System.Random(412);
            for(int i=0;i<26;i++)
            {
                float x=-11+i*0.9f;
                var clump=Shape("Floor_Grass",grassMesh,V(x,-0.31f,0.8f+(float)random.NextDouble()*4),V(0.8f,0.8f,0.8f),i%3==0?grassLight:grass,vegetation);
                clump.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);
            }
            for(int i=0;i<12;i++)
            {
                float x=-3.2f+i*0.23f;
                Shape("Deck_Weeds",grassMesh,V(x,1.69f,-1.65f),V(0.24f,0.30f,0.24f),grassLight,vegetation);
            }
            foreach(float x in new[]{-8.0f,-0.6f,2.0f,8.2f})
                Shape("Rear_Deck_Grass",grassMesh,V(x,3.0f,4.1f),V(0.45f,0.45f,0.45f),grass,vegetation);

            RenderSettings.skybox=null;
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(0.21f,0.33f,0.27f);
            RenderSettings.reflectionIntensity=0;
            RenderSettings.fog=true;
            RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=new Color(0.22f,0.67f,0.53f);
            RenderSettings.fogStartDistance=27;
            RenderSettings.fogEndDistance=48;
            var sun=Light("Golden_Window_Sun",LightType.Directional,V(0,10,0),new Color(1,0.94f,0.72f),0.85f);
            sun.transform.rotation=Quaternion.Euler(53,-28,0);
            sun.shadows=LightShadows.Soft;sun.shadowStrength=0.88f;sun.shadowBias=0.025f;sun.shadowNormalBias=0.18f;
            sun.shadowResolution=LightShadowResolution.VeryHigh;
            RenderSettings.sun=sun;
            var spot=Light("Robot_Warm_Key",LightType.Spot,V(4.0f,8.5f,-3),new Color(1,0.96f,0.75f),4.0f);
            spot.transform.LookAt(V(5.4f,2.8f,-0.2f));spot.range=14;spot.spotAngle=48;
            spot.shadows=LightShadows.Soft;spot.shadowStrength=0.65f;
            var fill=Light("Rear_Teal_Fill",LightType.Point,V(-1,4.3f,7.8f),new Color(0.43f,1,0.75f),2);
            fill.range=15;fill.shadows=LightShadows.None;
            Shaft("Central_High_Window_Shaft",V(0.0f,11,7.3f),V(3.2f,11,7.3f),V(-1.7f,-0.2f,0.3f),V(1.5f,-0.2f,0.3f),1);
            Shaft("Left_Window_Shaft",V(-7.0f,10.7f,8.0f),V(-5.0f,10.7f,8.0f),V(-9.2f,0.2f,1.1f),V(-7.2f,0.2f,1.1f),0.5f);
            Shaft("Robot_Window_Shaft",V(7.0f,11,7),V(9.0f,11,7),V(4.5f,2.35f,-0.1f),V(6.5f,2.35f,-0.1f),0.65f);
            AddReferenceAccents();
            var camGO=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener),typeof(ArtStyleTest.FreeFlyCamera));
            camGO.tag="MainCamera";
            var cam=camGO.GetComponent<Camera>();
            cam.transform.position=V(0,7.15f,-28);
            cam.transform.LookAt(V(0,3.5f,1));
            cam.orthographic=false;
            cam.fieldOfView=19.6f;
            cam.nearClipPlane=0.1f;cam.farClipPlane=100;
            cam.clearFlags=CameraClearFlags.SolidColor;
            cam.backgroundColor=new Color(0.23f,0.46f,0.36f);
            cam.allowHDR=false;cam.allowMSAA=true;
            PolishObjects();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
            AssetDatabase.SaveAssets();
            Validate();
        }
        public static void Refine()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            var cam=Camera.main;
            cam.transform.position=V(0,7.15f,-28);cam.transform.LookAt(V(0,3.5f,1));
            RenderSettings.ambientLight=new Color(0.21f,0.33f,0.27f);
            RenderSettings.fogColor=new Color(0.22f,0.67f,0.53f);
            RenderSettings.fogStartDistance=27;RenderSettings.fogEndDistance=48;
            var sun=GameObject.Find("Golden_Window_Sun").GetComponent<Light>();
            sun.color=new Color(1,0.94f,0.72f);sun.intensity=0.85f;
            var spot=GameObject.Find("Robot_Warm_Key").GetComponent<Light>();
            spot.color=new Color(1,0.96f,0.75f);spot.intensity=4;
            Color color;ColorUtility.TryParseHtmlString("#AA8C53",out color);
            AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Sunlit_Ochre.mat").color=color;
            foreach(var go in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if(go.name=="Luminous_Window") {
                    go.position=V(go.position.x,5.4f,go.position.z);
                    go.localScale=V(go.localScale.x,5.8f,go.localScale.z);
                }
                if(go.name=="Window_Vertical_Mullion") {
                    go.position=V(go.position.x,5.4f,go.position.z);
                    go.localScale=V(go.localScale.x,6.0f,go.localScale.z);
                }
                if(go.name=="Window_Horizontal_Mullion") {
                    float y=go.position.y;
                    go.position=V(go.position.x,y>10?8.4f:y>7?6.9f:y>4?5.25f:2.5f,go.position.z);
                }
            }
            var grassAsset=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Grass_Clump.asset");
            var normals=new Vector3[grassAsset.vertexCount];
            for(int i=0;i<normals.Length;i++)normals[i]=Vector3.up;
            grassAsset.normals=normals;EditorUtility.SetDirty(grassAsset);
            AddReferenceAccents();
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Validate();
        }

        static void PolishObjects()
        {
            if(GameObject.Find("08_Reference_Composition_Polished"))return;
            new GameObject("08_Reference_Composition_Polished");
            foreach(var t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                string n=t.name;
                float offset=0;
                if(n.StartsWith("Middle_")||n.StartsWith("Upper_")||n.StartsWith("Control_Cabin")||n.StartsWith("Cabin_")||
                   n.StartsWith("Crate_")||n=="Small_Industrial_Crate"||n.StartsWith("Signal_")||
                   n=="Rear_Deck_Grass"||n=="Hanging_Cable"||n.StartsWith("Descending_Gold_Duct")||n=="Duct_Connector"||
                   n.StartsWith("Bent_Left_Duct"))offset=-0.55f;
                if(n.StartsWith("Robot_Stage"))offset=-0.45f;
                if(n.StartsWith("Left_Offset_Deck"))offset=-0.12f;
                if(n.StartsWith("Bench_")||n=="Utility_Bench")offset=t.position.x<-6?-0.12f:-0.55f;
                if(offset!=0)t.position+=Vector3.up*offset;
            }
            GameObject.Find("07_Small_Ivory_Robot").transform.position+=Vector3.down*0.45f;
            foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                if(r.name.StartsWith("Ladder_"))UnityEngine.Object.DestroyImmediate(r.gameObject);
            details=GameObject.Find("04_Ladders_Pipes_Cables").transform;
            bronze=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Oxidized_Bronze.mat");
            cylinder=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/ArtStyleTest/Meshes/Cylinder_12Sides.asset");
            Ladder(V(5.45f,2.40f,1.9f),V(4.9f,5.30f,2.5f),0.68f,11);
            Ladder(V(-5.35f,2.40f,4),V(-5.35f,5.0f,4),0.5f,10);
            Ladder(V(2.7f,-0.2f,3),V(2.7f,2.40f,3),0.45f,10);
        }
        public static void PolishComposition()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            PolishObjects();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();Validate();
        }
        public static void Validate()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            var cam=Camera.main;
            if(!cam||!cam.GetComponent<ArtStyleTest.FreeFlyCamera>())throw new Exception("Missing free camera.");
            var renderers=UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach(var renderer in renderers)
            {
                if(!renderer.GetComponent<MeshFilter>().sharedMesh)throw new Exception("Missing mesh: "+renderer.name);
                foreach(var mat in renderer.sharedMaterials)
                    if(!mat||!mat.shader||!mat.shader.isSupported)throw new Exception("Unsupported material: "+renderer.name);
            }
            var rt=new RenderTexture(1600,900,24);
            var previous=RenderTexture.active;
            cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
            File.WriteAllBytes(Root+"/Preview/Factory_Reference_Study.png",image.EncodeToPNG());
            cam.targetTexture=null;RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            File.WriteAllText(Root+"/Validation.txt","Unity: "+Application.unityVersion+
                "\nScene reopened: "+scene.path+"\nMesh renderers: "+renderers.Length+
                "\nAll meshes/materials supported; camera rendered at 1600x900."+
                "\nBuilt-in; Standard solid materials; existing simple additive beam shader."+
                "\nFreeFlyCamera attached. Play keyboard/mouse not physically simulated.\n");
            AssetDatabase.Refresh();
            Debug.Log("FACTORY_REFERENCE_VALIDATION_OK: "+renderers.Length);
        }
        static void AddReferenceAccents()
        {
            var beamShader=Shader.Find("ArtStyleTest/Window Light Beam");
            var luminous=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Window_Pale_Gold.mat");
            luminous.shader=beamShader;luminous.SetColor("_Color",new Color(1f,0.97f,0.65f,0.34f));EditorUtility.SetDirty(luminous);
            var rusty=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Oxidized_Bronze.mat");
            Color color;ColorUtility.TryParseHtmlString("#86624A",out color);rusty.color=color;EditorUtility.SetDirty(rusty);
            foreach(var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                if(renderer.name.StartsWith("Robot_Stage") || renderer.transform.root.name=="07_Small_Ivory_Robot")
                    renderer.gameObject.layer=27;
            var key=GameObject.Find("Robot_Warm_Key").GetComponent<Light>();
            key.type=LightType.Directional;key.intensity=1.1f;key.color=new Color(1f,0.98f,0.73f);
            key.transform.rotation=Quaternion.Euler(52,-34,0);key.cullingMask=1<<27;
            foreach(var transform in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if(transform.name=="Robot_Eye_Rim") {
                    float radius=transform.localPosition.x>0?0.30f:0.14f;transform.localScale=V(radius,0.09f,radius);
                }
                if(transform.name=="Robot_Dark_Lens") {
                    float radius=transform.localPosition.x>0?0.30f:0.14f;transform.localScale=V(radius*0.85f,radius*0.85f,0.09f);
                }
            }
            var grassAsset=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Grass_Clump.asset");
            var grassMat=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Grass_YellowGreen.mat");
            var foliageRoot=GameObject.Find("05_Reclaimed_Grass").transform;
            for(int i=0;i<15;i++)
                Shape("Foreground_Grass",grassAsset,V(i<5?-9f+i*0.65f:1.8f+(i-5)*0.8f,-0.32f,-4.8f),
                    V(0.75f,0.9f+(i%3)*0.15f,0.75f),grassMat,foliageRoot);
            var accent=new Material(beamShader){name="Sharp_Window_Sunlight"};
            accent.SetColor("_Color",new Color(1,0.93f,0.52f,0.35f));
            AssetDatabase.CreateAsset(accent,Root+"/Materials/Sharp_Window_Sunlight.mat");
            var vertices=new List<Vector3>();var triangles=new List<int>();
            foreach(float x in new[]{-1.0f,2.5f,6.4f,8.2f})
            {
                int n=vertices.Count;
                vertices.AddRange(new[]{V(x,1.719f,-3.75f),V(x+0.7f,1.719f,-3.75f),
                    V(x+2.0f,1.719f,-1.62f),V(x+1.3f,1.719f,-1.62f)});
                triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            var patches=new Mesh{name="Angular_Window_Sunlight_Patches"};
            patches.SetVertices(vertices);patches.SetTriangles(triangles,0);
            var colors=new Color[vertices.Count];for(int i=0;i<colors.Length;i++)colors[i]=Color.white;patches.colors=colors;
            patches.RecalculateNormals();patches.RecalculateBounds();
            AssetDatabase.CreateAsset(patches,Root+"/Meshes/Angular_Window_Sunlight_Patches.asset");
            var patchObject=Shape("Sharp_Foreground_Window_Light",patches,Vector3.zero,Vector3.one,accent,
                GameObject.Find("06_Warm_Light_And_Teal_Haze").transform);
            patchObject.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            // Irregular geometry marks chipped edges without relying on textures.
            var crackVertices=new List<Vector3>();var crackIndices=new List<int>();
            foreach(float x in new[]{-5.5f,-2.5f,2.2f,4.9f,8.1f})
            {
                int n=crackVertices.Count;
                crackVertices.AddRange(new[]{V(x,1.68f,-3.86f),V(x+0.38f,1.68f,-3.86f),
                    V(x+0.21f,1.08f,-3.86f),V(x+0.12f,1.29f,-3.86f)});
                crackIndices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3,n+2,n+1,n,n+3,n+2,n});
            }
            var cracks=new Mesh{name="Broken_Frame_Outline"};
            cracks.SetVertices(crackVertices);cracks.SetTriangles(crackIndices,0);cracks.RecalculateNormals();cracks.RecalculateBounds();
            AssetDatabase.CreateAsset(cracks,Root+"/Meshes/Broken_Frame_Outline.asset");
            Shape("Chipped_Front_Edges",cracks,Vector3.zero,Vector3.one,
                AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Deep_Industrial_Shadow.mat"),
                GameObject.Find("02_Layered_Catwalks").transform);
        }

        static Vector3 V(float x,float y,float z){return new Vector3(x,y,z);}
        static Transform Group(string name){return new GameObject(name).transform;}
        static Material Mat(string name,string hex)
        {
            Color color;ColorUtility.TryParseHtmlString(hex,out color);
            var m=new Material(Shader.Find("Standard")){name=name,color=color};
            m.SetFloat("_Metallic",0);m.SetFloat("_Glossiness",0);
            m.SetFloat("_SpecularHighlights",0);m.SetFloat("_GlossyReflections",0);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            AssetDatabase.CreateAsset(m,Root+"/Materials/"+name+".mat");return m;
        }
        static GameObject Box(string name,Vector3 pos,Vector3 scale,Material mat,Transform parent)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name=name;go.transform.SetParent(parent,false);go.transform.position=pos;go.transform.localScale=scale;
            go.GetComponent<MeshRenderer>().sharedMaterial=mat;return go;
        }
        static GameObject Shape(string name,Mesh mesh,Vector3 pos,Vector3 scale,Material mat,Transform parent)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(parent,false);go.transform.position=pos;go.transform.localScale=scale;
            go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;return go;
        }
        static void Rod(string name,Vector3 a,Vector3 b,float radius,Material mat,Transform parent)
        {
            var go=Shape(name,cylinder,(a+b)*0.5f,V(radius*2,(b-a).magnitude,radius*2),mat,parent);
            go.transform.rotation=Quaternion.FromToRotation(Vector3.up,b-a);
        }
        static void Window(float center,float width,int divisions)
        {
            Box("Luminous_Window",V(center,5.4f,10.9f),V(width,5.8f,0.08f),window,architecture);
            for(int i=0;i<=divisions;i++)
                Box("Window_Vertical_Mullion",V(center-width*0.5f+width*i/divisions,5.4f,10.8f),V(0.15f,6.0f,0.18f),back,architecture);
            foreach(float y in new[]{2.5f,5.25f,6.9f,8.4f})
                Box("Window_Horizontal_Mullion",V(center,y,10.78f),V(width+0.16f,0.16f,0.20f),back,architecture);
        }
        static void Platform(string name,float x,float y,float z,float width,float depth)
        {
            Box(name+"_Dark_Core",V(x,y-0.42f,z),V(width,0.78f,depth),dark,platforms);
            Box(name+"_Top",V(x,y,z),V(width,0.12f,depth),top,platforms);
            Box(name+"_Lower_Flange",V(x,y-0.75f,z-depth*0.5f-0.015f),V(width,0.15f,0.23f),dark,platforms);
            int count=Mathf.CeilToInt(width/1.65f);float step=width/count;
            for(int i=0;i<count;i++)
            {
                float px=x-width*0.5f+(i+0.5f)*step;
                Box(name+"_Recessed_Panel",V(px,y-0.40f,z-depth*0.5f-0.02f),V(step-0.13f,0.56f,0.06f),i%4==0?green:bronze,platforms);
                Box(name+"_Vertical_Rib",V(px-step*0.5f,y-0.38f,z-depth*0.5f-0.10f),V(0.075f,0.72f,0.12f),bronze,platforms);
                Box(name+"_Plate_Seam",V(px-step*0.5f,y+0.066f,z),V(0.025f,0.018f,depth),dark,platforms);
                if(i%3==0)Box(name+"_Top_Strip",V(px,y+0.08f,z-depth*0.4f),V(step-0.28f,0.02f,0.06f),bronze,platforms);
            }
            Box(name+"_Front_Lip",V(x,y-0.02f,z-depth*0.5f-0.10f),V(width,0.12f,0.12f),bronze,platforms);
        }
        static void Duct(string name,Vector3[] points,float size,Material mat,Transform parent)
        {
            for(int i=0;i<points.Length-1;i++)
            {
                Vector3 d=points[i+1]-points[i];
                var go=Box(name,(points[i]+points[i+1])*0.5f,V(size,d.magnitude,size),mat,parent);
                go.transform.rotation=Quaternion.FromToRotation(Vector3.up,d);
                Box(name+"_Joint",points[i],V(size*1.22f,size*1.22f,size*1.22f),mat,parent);
            }
        }
        static void Ladder(Vector3 bottom,Vector3 upper,float width,int rungs)
        {
            Rod("Ladder_Left",bottom+Vector3.left*width*0.5f,upper+Vector3.left*width*0.5f,0.035f,bronze,details);
            Rod("Ladder_Right",bottom+Vector3.right*width*0.5f,upper+Vector3.right*width*0.5f,0.035f,bronze,details);
            for(int i=0;i<=rungs;i++)
            {
                var p=Vector3.Lerp(bottom,upper,(float)i/rungs);
                Rod("Ladder_Rung",p+Vector3.left*width*0.5f,p+Vector3.right*width*0.5f,0.027f,bronze,details);
            }
        }
        static void Crate(Vector3 p,Vector3 size)
        {
            Box("Small_Industrial_Crate",p,size,top,machines);
            Box("Crate_Top_Lip",p+Vector3.up*size.y*0.42f,V(size.x*1.07f,0.055f,size.z*1.07f),bronze,machines);
            for(int i=0;i<3;i++)
                Box("Crate_Stencil",p+V(-size.x*0.2f+i*size.x*0.18f,0,-size.z*0.51f),V(0.025f,size.y*0.3f,0.015f),bronze,machines);
        }
        static void Desk(float x,float y,float z)
        {
            Box("Utility_Bench",V(x,y+0.26f,z),V(1.15f,0.12f,0.48f),bronze,machines);
            foreach(float dx in new[]{-0.5f,0.5f})Box("Bench_Leg",V(x+dx,y+0.1f,z),V(0.065f,0.3f,0.28f),dark,machines);
            Box("Bench_Luminous_Strip",V(x,y+0.32f,z-0.1f),V(0.8f,0.025f,0.08f),window,machines);
        }
        static void Cable(Vector3[] points)
        {
            for(int i=0;i<points.Length-1;i++)Rod("Hanging_Cable",points[i],points[i+1],0.018f,dark,details);
        }
        static void SignalRobot()
        {
            Rod("Signal_Post",V(-3.2f,2.95f,3),V(-3.2f,4.0f,3),0.065f,green,machines);
            Shape("Signal_Rounded_Head",sphere,V(-3.2f,4.1f,3),V(0.66f,0.7f,0.45f),green,machines);
            foreach(float dx in new[]{-0.16f,0.16f})
            {
                var eye=Shape("Signal_Eye",cylinder,V(-3.2f+dx,4.15f,2.76f),V(0.17f,0.06f,0.17f),dark,machines);
                eye.transform.rotation=Quaternion.Euler(90,0,0);
            }
            Rod("Signal_Antenna",V(-3.5f,4.43f,3),V(-3.2f,4.65f,3),0.025f,bronze,machines);
            Rod("Signal_Antenna",V(-3.2f,4.65f,3),V(-2.9f,4.43f,3),0.025f,bronze,machines);
        }
        static void Robot(Vector3 p)
        {
            var robot=Group("07_Small_Ivory_Robot");
            // Build around the world origin, then move the whole rig onto its platform.
            Box("Robot_Torso",V(0,0.72f,0),V(0.38f,0.54f,0.32f),cream,robot);
            Box("Robot_Front_Console",V(0,0.75f,-0.17f),V(0.25f,0.37f,0.045f),black,robot);
            Box("Robot_Console_Trim",V(-0.08f,0.75f,-0.20f),V(0.055f,0.3f,0.025f),bronze,robot);
            Box("Robot_Console_Light",V(0.065f,0.83f,-0.20f),V(0.045f,0.07f,0.02f),window,robot);
            Shape("Robot_Head",sphere,V(0,1.26f,-0.015f),V(0.70f,0.70f,0.58f),cream,robot);
            foreach(float dx in new[]{-0.17f,0.17f})
            {
                float radius=dx>0?0.30f:0.14f;
                var rim=Shape("Robot_Eye_Rim",cylinder,V(dx,1.29f,-0.275f),V(radius,0.09f,radius),bronze,robot);
                rim.transform.rotation=Quaternion.Euler(90,0,0);
                var eye=Shape("Robot_Dark_Lens",sphere,V(dx,1.29f,-0.33f),V(radius*0.85f,radius*0.85f,0.09f),black,robot);
                Shape("Robot_Lens_Glint",sphere,V(dx-0.035f,1.33f,-0.381f),V(0.055f,0.055f,0.016f),cream,robot);
            }
            Rod("Robot_Neck",V(0,0.96f,0),V(0,1.1f,0),0.075f,black,robot);
            foreach(float side in new[]{-1f,1f})
            {
                Shape("Robot_Shoulder",sphere,V(side*0.25f,0.9f,0),V(0.15f,0.15f,0.15f),black,robot);
                Rod("Robot_Upper_Arm",V(side*0.26f,0.86f,0),V(side*0.31f,0.61f,-0.02f),0.055f,cream,robot);
                Shape("Robot_Elbow",sphere,V(side*0.31f,0.59f,-0.02f),V(0.12f,0.12f,0.12f),black,robot);
                Rod("Robot_Forearm",V(side*0.31f,0.58f,-0.02f),V(side*0.29f,0.42f,-0.10f),0.055f,cream,robot);
                Shape("Robot_Hand",sphere,V(side*0.29f,0.40f,-0.1f),V(0.13f,0.14f,0.13f),bronze,robot);
                Rod("Robot_Upper_Leg",V(side*0.11f,0.48f,0),V(side*0.13f,0.28f,0),0.06f,cream,robot);
                Shape("Robot_Knee",sphere,V(side*0.13f,0.28f,0),V(0.12f,0.12f,0.12f),black,robot);
                Rod("Robot_Shin",V(side*0.13f,0.25f,0),V(side*0.14f,0.1f,-0.03f),0.055f,cream,robot);
                Box("Robot_Foot",V(side*0.14f,0.07f,-0.06f),V(0.17f,0.12f,0.26f),black,robot);
            }
            robot.position=p;robot.rotation=Quaternion.Euler(0,-10,0);
        }
        static Mesh GrassMesh()
        {
            var v=new List<Vector3>();var t=new List<int>();var random=new System.Random(73);
            for(int i=0;i<28;i++)
            {
                float x=(float)random.NextDouble()*1.4f-0.7f,z=(float)random.NextDouble()*0.8f-0.4f;
                float height=0.2f+(float)random.NextDouble()*0.5f,width=0.035f+(float)random.NextDouble()*0.05f;
                int start=v.Count;
                v.Add(V(x-width,0,z));v.Add(V(x+width,0,z));v.Add(V(x+width*0.5f,height*0.68f,z+0.025f));v.Add(V(x+(float)random.NextDouble()*0.2f-0.1f,height,z+0.06f));
                t.AddRange(new[]{start,start+2,start+1,start,start+3,start+2,start+1,start+2,start,start+2,start+3,start});
            }
            var mesh=new Mesh{name="Angular_Grass_Blades"};mesh.SetVertices(v);mesh.SetTriangles(t,0);var normals=new Vector3[v.Count];for(int i=0;i<normals.Length;i++)normals[i]=Vector3.up;mesh.normals=normals;mesh.RecalculateBounds();return mesh;
        }
        static Light Light(string name,LightType type,Vector3 pos,Color color,float intensity)
        {
            var go=new GameObject(name,typeof(Light));go.transform.SetParent(lightGroup,false);go.transform.position=pos;
            var l=go.GetComponent<Light>();l.type=type;l.color=color;l.intensity=intensity;l.lightmapBakeType=LightmapBakeType.Realtime;l.renderMode=LightRenderMode.ForcePixel;return l;
        }
        static void Shaft(string name,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float opacity)
        {
            var mesh=new Mesh{name=name};
            mesh.vertices=new[]{a,b,d,c,a+Vector3.forward*0.8f,b+Vector3.forward*0.8f,d+Vector3.forward*0.8f,c+Vector3.forward*0.8f};
            mesh.colors=new[]{new Color(1,1,1,opacity),new Color(1,1,1,opacity),new Color(1,1,1,0),new Color(1,1,1,0),
                new Color(1,1,1,opacity),new Color(1,1,1,opacity),new Color(1,1,1,0),new Color(1,1,1,0)};
            mesh.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7,0,4,7,0,7,3,1,5,6,1,6,2};
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Root+"/Meshes/"+name+".asset");
            var go=Shape(name,mesh,Vector3.zero,Vector3.one,beam,lightGroup);
            go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            go.GetComponent<MeshRenderer>().receiveShadows=false;
        }
    }
}
