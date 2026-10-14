using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ArtStyleTestTools
{
    public static class LowPolySceneBuilder
    {
        const string Root = "Assets/ArtStyleTest";
        public const string ScenePath = Root + "/Scenes/LowPoly_ArtStyle_Test.unity";
        static Material wall, floor, machine, cardboard, foliage, dark, wood, cream;
        static Mesh cylinder, sphere;
        static Transform room, props, plants, samples, lighting;

        [MenuItem("Tools/Art Style Test/Create Low Poly Test Scene")]
        public static void Generate()
        {
            if (File.Exists(ScenePath) || Directory.Exists(Root + "/Materials") || Directory.Exists(Root + "/Meshes"))
                throw new InvalidOperationException("Test assets already exist. Generation stopped without overwriting.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("URP Lit shader unavailable.");
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Meshes");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            wall = Mat("Wall_DustyRose", "#A99592");
            floor = Mat("Floor_Charcoal", "#41464A");
            machine = Mat("Machine_SlateBlue", "#74858A");
            cardboard = Mat("Cardboard_Tan", "#D5B995");
            foliage = Mat("Plant_Sage", "#839C83");
            dark = Mat("Belt_Pipe_Dark", "#343D40");
            wood = Mat("Wood_FadedBrown", "#9A806B");
            cream = Mat("Trim_WarmCream", "#C7BDA8");
            cylinder = CylinderMesh(12);
            sphere = SphereMesh(12, 6);
            AssetDatabase.CreateAsset(cylinder, Root + "/Meshes/Cylinder_12Sides.asset");
            AssetDatabase.CreateAsset(sphere, Root + "/Meshes/Sphere_Faceted.asset");
            room = Group("01_Room_Modules");
            props = Group("02_Factory_Props");
            plants = Group("03_Plants");
            samples = Group("04_Material_Light_Samples");
            lighting = Group("05_Lighting");
            Box("Floor_12x10", new Vector3(0,-0.2f,0), new Vector3(12,0.4f,10), floor, room);
            // Back wall: a real 4 x 2 opening, with no glass or texture.
            Box("BackWall_Left", new Vector3(-4.75f,2,5), new Vector3(2.5f,4,0.4f), wall, room);
            Box("BackWall_Right", new Vector3(3.25f,2,5), new Vector3(5.5f,4,0.4f), wall, room);
            Box("Window_Sill_Wall", new Vector3(-1.5f,0.75f,5), new Vector3(4,1.5f,0.4f), wall, room);
            Box("Window_Header_Wall", new Vector3(-1.5f,3.75f,5), new Vector3(4,0.5f,0.4f), wall, room);
            Box("RightWall_10x4", new Vector3(6,2,0), new Vector3(0.4f,4,10), wall, room);
            Box("LeftWall_Partial", new Vector3(-6,2,3), new Vector3(0.4f,4,4), wall, room);
            Box("Window_Ledge", new Vector3(-1.5f,1.5f,4.75f), new Vector3(4.4f,0.16f,0.8f), cream, room);
            Box("Window_Mullion", new Vector3(-1.5f,2.5f,5), new Vector3(0.12f,2,0.25f), cream, room);
            Box("Window_Crossbar", new Vector3(-1.5f,2.5f,5), new Vector3(4,0.12f,0.25f), cream, room);
            foreach (float x in new[] {-5f,5f})
            foreach (float z in new[] {-2f,4f})
            {
                Box("Industrial_Pillar", new Vector3(x,2,z), new Vector3(0.5f,4,0.5f), machine, room);
                Box("Pillar_Foot", new Vector3(x,0.15f,z), new Vector3(0.8f,0.3f,0.8f), dark, room);
            }
            Box("Overhead_Crossbeam", new Vector3(0,3.9f,4), new Vector3(10.5f,0.2f,0.4f), machine, room);
            // Four-unit conveyor with restrained roller and leg details.
            Box("Conveyor_Frame", new Vector3(-1.5f,0.9f,0), new Vector3(4,0.3f,1.4f), machine, props);
            Box("Conveyor_Belt", new Vector3(-1.5f,1.08f,0), new Vector3(4,0.08f,1), dark, props);
            foreach (float x in new[] {-3f,0f})
            foreach (float z in new[] {-0.5f,0.5f})
                Box("Conveyor_Leg", new Vector3(x,0.4f,z), new Vector3(0.18f,0.8f,0.18f), machine, props);
            for (int i = 0; i < 7; i++)
                Box("Belt_Slat", new Vector3(-3.2f+i*0.55f,1.13f,0), new Vector3(0.06f,0.025f,0.96f), machine, props);
            Rod("Conveyor_Roller_Left", new Vector3(-3.55f,0.94f,-0.65f), new Vector3(-3.55f,0.94f,0.65f), 0.12f, cream, props);
            Rod("Conveyor_Roller_Right", new Vector3(0.55f,0.94f,-0.65f), new Vector3(0.55f,0.94f,0.65f), 0.12f, cream, props);
            Crate("Box_Large", new Vector3(3,0.75f,3), new Vector3(1.5f,1.5f,1.5f), 0);
            Crate("Box_Stacked", new Vector3(3,2,3), Vector3.one, -8);
            Crate("Box_Small", new Vector3(4.4f,0.4f,2.7f), Vector3.one*0.8f, 12);
            Crate("Box_On_Belt", new Vector3(-1.2f,1.63f,0), Vector3.one, 7);
            Crate("Box_Foreground", new Vector3(-4,0.5f,-2.8f), Vector3.one, -14);
            Box("Workbench_Top", new Vector3(3,1.25f,0), new Vector3(2,0.2f,1), wood, props);
            foreach (float x in new[] {2.2f,3.8f})
            foreach (float z in new[] {-0.35f,0.35f})
                Box("Workbench_Leg", new Vector3(x,0.6f,z), new Vector3(0.15f,1.2f,0.15f), machine, props);
            Box("Workbench_Shelf", new Vector3(3,0.4f,0), new Vector3(1.8f,0.12f,0.8f), machine, props);
            // A modest unfinished toy made only from simple shapes.
            Shape("Toy_Body", sphere, new Vector3(2.8f,1.55f,0), Vector3.one*0.42f, cream, props);
            Shape("Toy_Head", sphere, new Vector3(2.8f,1.9f,0), Vector3.one*0.3f, wall, props);
            Box("Small_Tool_Tray", new Vector3(3.5f,1.42f,0), new Vector3(0.55f,0.12f,0.4f), machine, props);
            for (int i=0;i<3;i++)
            {
                float x=4.6f+i*0.4f;
                Rod("Back_Pipe_Vertical", new Vector3(x,0.3f,4.5f), new Vector3(x,3.2f,4.5f),0.10f,dark,props);
                Rod("Back_Pipe_Outlet", new Vector3(x,3.2f,4.5f), new Vector3(x,3.2f,3.5f),0.10f,dark,props);
                Shape("Pipe_Elbow",sphere,new Vector3(x,3.2f,4.5f),Vector3.one*0.22f,dark,props);
                for(int j=0;j<2;j++)
                    Shape("Pipe_Collar",cylinder,new Vector3(x,0.7f+j*1.5f,4.5f),new Vector3(0.26f,0.08f,0.26f),machine,props);
            }
            Plant(new Vector3(-4.9f,0,2.4f), 1);
            Plant(new Vector3(4.9f,0,-2.8f), 0.75f);
            // Clearly separated lit-shader test primitives.
            Box("Sample_Platform",new Vector3(0,0.15f,-3.2f),new Vector3(4,0.3f,1.6f),cream,samples).transform.rotation = Quaternion.Euler(0,30,0);
            Box("Sample_Cube",new Vector3(-1.25f,0.8f,-2.478f),Vector3.one,machine,samples);
            Shape("Sample_Sphere",sphere,new Vector3(0,0.8f,-3.2f),Vector3.one,wall,samples);
            Shape("Sample_Cylinder",cylinder,new Vector3(1.25f,0.8f,-3.922f),new Vector3(0.9f,1,0.9f),foliage,samples);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f,0.50f,0.53f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.fog = false;
            var sun = LightObject("Soft_Natural_Directional", LightType.Directional);
            sun.color = new Color(1,0.94f,0.85f);
            sun.intensity = 0.95f;
            sun.transform.rotation = Quaternion.Euler(48,-35,0);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
            sun.shadowBias = 0.035f;
            sun.shadowNormalBias = 0.25f;
            sun.shadowResolution = LightShadowResolution.High;
            RenderSettings.sun = sun;
            var spot = LightObject("Warm_Industrial_Spot",LightType.Spot);
            spot.transform.position = new Vector3(3,3.5f,0.2f);
            spot.transform.LookAt(new Vector3(3,0.7f,0));
            spot.color = new Color(1,0.76f,0.48f);
            spot.intensity = 2.2f;
            spot.range = 7;
            spot.spotAngle = 62;
            spot.innerSpotAngle = 35;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.5f;
            spot.shadowBias = 0.03f;
            spot.shadowNormalBias = 0.2f;
            Box("Lamp_Wall_Bracket",new Vector3(4.4f,3.8f,0.2f),new Vector3(2.8f,0.1f,0.1f),machine,lighting);
            Box("Industrial_Lamp_Housing",new Vector3(3,3.65f,0.2f),new Vector3(0.6f,0.25f,0.4f),dark,lighting);
            Box("Industrial_Lamp_Face",new Vector3(3,3.5f,0.2f),new Vector3(0.45f,0.03f,0.3f),cream,lighting);
            var cameraObject = new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<ArtStyleTest.FreeFlyCamera>();
            var cam = cameraObject.GetComponent<Camera>();
            cam.transform.position = new Vector3(-14,11,-17);
            cam.transform.LookAt(new Vector3(0,1.4f,0));
            cam.orthographic = false;
            cam.fieldOfView = 38;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f,0.28f,0.30f);
            cam.allowHDR = false;
            cam.allowMSAA = true;
            WindowLightBeamBuilder.AddToScene();
            if (!EditorSceneManager.SaveScene(scene,ScenePath)) throw new IOException("Scene save failed.");
            AssetDatabase.SaveAssets();
            Validate();
        }

        [MenuItem("Tools/Art Style Test/Open Low Poly Test Scene")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) throw new FileNotFoundException(ScenePath);
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach (var r in renderers)
            {
                if (r.name.StartsWith("Window_Light_Beam_"))
                {
                    if (r.sharedMaterials.Length != 3 || r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name != "ArtStyleTest/Window Light Beam" || !m.shader.isSupported))
                        throw new InvalidOperationException("Invalid beam material: " + r.name);
                    continue;
                }
                if (!r.sharedMaterial || !r.sharedMaterial.shader || r.sharedMaterial.shader.name != "Standard")
                    throw new InvalidOperationException("Invalid material: " + r.name);
                if (r.sharedMaterial.mainTexture) throw new InvalidOperationException("Unexpected texture: " + r.name);
                if (r.GetComponent<MeshFilter>().sharedMesh == null) throw new InvalidOperationException("Missing mesh: " + r.name);
            }
            var cam=Camera.main;
            if (cam == null || UnityEngine.Object.FindObjectsOfType<Light>().Length != 2)
                throw new InvalidOperationException("Camera/light validation failed.");
            var target=new RenderTexture(1600,1000,24);
            var previous=RenderTexture.active;
            cam.targetTexture=target;
            cam.Render();
            RenderTexture.active=target;
            var preview=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            preview.ReadPixels(new Rect(0,0,1600,1000),0,0);
            preview.Apply();
            Directory.CreateDirectory(Root+"/Preview");
            File.WriteAllBytes(Root+"/Preview/LowPoly_ArtStyle_Test.png",preview.EncodeToPNG());
            cam.targetTexture=null;
            RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(preview);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            File.WriteAllText(Root+"/Validation.txt",
                "Unity: "+Application.unityVersion+"\nPipeline: Built-in\nScene reopened: "+scene.path+
                "\nMesh renderers: "+renderers.Length+"\nMaterials: Standard plus simple additive window-light shader, no textures\nLights: 2\nCamera render: 1600x1000\n");
            AssetDatabase.Refresh();
            Debug.Log("ART_STYLE_TEST_VALIDATION_OK: "+renderers.Length+" renderers");
        }

        static Transform Group(string name) { return new GameObject(name).transform; }
        static Material Mat(string name,string hex)
        {
            Color color; ColorUtility.TryParseHtmlString(hex,out color);
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit")) { name=name,color=color };
            m.SetFloat("_Metallic",0); m.SetFloat("_Smoothness",0);
            m.SetFloat("_SpecularHighlights",0); m.SetFloat("_EnvironmentReflections",0);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            AssetDatabase.CreateAsset(m,Root+"/Materials/"+name+".mat"); return m;
        }
        static GameObject Box(string name,Vector3 pos,Vector3 scale,Material mat,Transform parent)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name=name; go.transform.SetParent(parent,false);
            go.transform.position=pos; go.transform.localScale=scale;
            go.GetComponent<MeshRenderer>().sharedMaterial=mat; return go;
        }
        static GameObject Shape(string name,Mesh mesh,Vector3 pos,Vector3 scale,Material mat,Transform parent)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(parent,false); go.transform.position=pos; go.transform.localScale=scale;
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial=mat; return go;
        }
        static void Rod(string name,Vector3 a,Vector3 b,float radius,Material mat,Transform parent)
        {
            var go=Shape(name,cylinder,(a+b)*0.5f,new Vector3(radius*2,(b-a).magnitude,radius*2),mat,parent);
            go.transform.rotation=Quaternion.FromToRotation(Vector3.up,b-a);
        }
        static void Crate(string name,Vector3 pos,Vector3 size,float yaw)
        {
            var group=new GameObject(name).transform; group.SetParent(props,false); group.position=pos;
            var body=Box("Cardboard",pos,size,cardboard,group);
            Box("Top_Seam",pos+Vector3.up*(size.y*0.5f+0.006f),new Vector3(size.x*0.08f,0.012f,size.z),wood,group);
            Box("Front_Seam",pos+Vector3.back*(size.z*0.5f+0.006f),new Vector3(size.x*0.08f,size.y,0.012f),wood,group);
            group.rotation=Quaternion.Euler(0,yaw,0);
        }
        static void Plant(Vector3 p,float scale)
        {
            Shape("Plant_Pot",cylinder,p+Vector3.up*0.22f*scale,new Vector3(0.6f,0.44f,0.6f)*scale,wood,plants);
            Rod("Plant_Stem",p+Vector3.up*0.4f*scale,p+Vector3.up*1.4f*scale,0.035f*scale,wood,plants);
            for(int i=0;i<5;i++)
            {
                float angle=i*137.5f*Mathf.Deg2Rad;
                var offset=new Vector3(Mathf.Cos(angle)*0.2f,0.7f+i*0.15f,Mathf.Sin(angle)*0.2f)*scale;
                var leaf=Shape("Faceted_Leaf",sphere,p+offset,new Vector3(0.6f,0.22f,0.32f)*scale,foliage,plants);
                leaf.transform.rotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,20);
            }
        }
        static Light LightObject(string name,LightType type)
        {
            var go=new GameObject(name,typeof(Light)); go.transform.SetParent(lighting,false);
            var l=go.GetComponent<Light>(); l.type=type; l.lightmapBakeType=LightmapBakeType.Realtime;
            l.renderMode=LightRenderMode.ForcePixel; return l;
        }
        static Mesh Faceted(string name,System.Collections.Generic.List<Vector3> vertices)
        {
            var mesh=new Mesh {name=name}; mesh.SetVertices(vertices);
            mesh.SetTriangles(Enumerable.Range(0,vertices.Count).ToArray(),0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static void Triangle(System.Collections.Generic.List<Vector3> v,Vector3 a,Vector3 b,Vector3 c)
        { v.Add(a);v.Add(b);v.Add(c); }
        static Mesh CylinderMesh(int sides)
        {
            var v=new System.Collections.Generic.List<Vector3>();
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                var p=new Vector3(Mathf.Cos(a)*0.5f,-0.5f,Mathf.Sin(a)*0.5f);
                var q=new Vector3(Mathf.Cos(b)*0.5f,-0.5f,Mathf.Sin(b)*0.5f);
                Triangle(v,p,p+Vector3.up,q+Vector3.up); Triangle(v,p,q+Vector3.up,q);
                Triangle(v,Vector3.up*0.5f,q+Vector3.up,p+Vector3.up);
                Triangle(v,Vector3.down*0.5f,p,q);
            }
            return Faceted("Cylinder_12Sides",v);
        }
        static Vector3 SpherePoint(int x,int y,int sides,int rings)
        {
            float phi=Mathf.PI*y/rings,theta=Mathf.PI*2*x/sides;
            return new Vector3(Mathf.Sin(phi)*Mathf.Cos(theta),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(theta))*0.5f;
        }
        static Mesh SphereMesh(int sides,int rings)
        {
            var v=new System.Collections.Generic.List<Vector3>();
            for(int y=0;y<rings;y++) for(int x=0;x<sides;x++)
            {
                var a=SpherePoint(x,y,sides,rings);var b=SpherePoint(x+1,y,sides,rings);
                var c=SpherePoint(x,y+1,sides,rings);var d=SpherePoint(x+1,y+1,sides,rings);
                if(y>0) Triangle(v,a,b,c);
                if(y<rings-1) Triangle(v,b,d,c);
            }
            return Faceted("Sphere_Faceted",v);
        }
    }
}
