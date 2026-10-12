using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArtStyleTestTools
{
    public static class WindowLightBeamBuilder
    {
        const string Root = "Assets/ArtStyleTest";
        public static void AddToScene()
        {
            if (GameObject.Find("06_Window_Light_Beams") != null)
                throw new InvalidOperationException("Window beams already exist; refusing duplicate.");
            var shader = Shader.Find("ArtStyleTest/Window Light Beam");
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Window light shader unavailable.");
            var beam = MaterialAsset(shader, "WindowBeam_Warm", new Color(1f,0.9f,0.66f,0.20f));
            var source = MaterialAsset(shader, "WindowSource_Warm", new Color(1f,0.94f,0.78f,0.55f));
            var patch = MaterialAsset(shader, "WindowFloor_Light", new Color(1f,0.86f,0.58f,0.20f));
            var group = new GameObject("06_Window_Light_Beams").transform;
            var sun = GameObject.Find("Soft_Natural_Directional").GetComponent<Light>();
            // Rays point from the back-wall opening into the room and toward the floor.
            sun.transform.rotation = Quaternion.Euler(48,145,0);
            sun.color = new Color(1f,0.94f,0.82f);
            sun.intensity = 0.85f;
            RenderSettings.ambientLight = new Color(0.30f,0.32f,0.34f);
            for (int i=0; i<2; i++)
            {
                float left = i == 0 ? -3.36f : -1.36f;
                float right = i == 0 ? -1.64f : 0.36f;
                var mesh = ShaftMesh(left,right,1.64f,3.36f,4.96f,sun.transform.forward);
                string path = Root + "/Meshes/WindowBeam_" + (i+1) + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) throw new InvalidOperationException("Existing mesh: "+path);
                AssetDatabase.CreateAsset(mesh,path);
                var go = new GameObject("Window_Light_Beam_"+(i+1),typeof(MeshFilter),typeof(MeshRenderer));
                go.transform.SetParent(group,false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] {beam,source,patch};
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
        }
        static Material MaterialAsset(Shader shader,string name,Color color)
        {
            string path=Root+"/Materials/"+name+".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path)) throw new InvalidOperationException("Existing material: "+path);
            var material=new Material(shader) {name=name};
            material.SetColor("_Color",color);
            AssetDatabase.CreateAsset(material,path);
            return material;
        }
        static Mesh ShaftMesh(float left,float right,float low,float high,float depth,Vector3 direction)
        {
            var start=new[] {
                new Vector3(left,low,depth),new Vector3(right,low,depth),
                new Vector3(right,high,depth),new Vector3(left,high,depth)};
            var end=new Vector3[4];
            for(int i=0;i<4;i++) end[i]=start[i]+direction*((0.035f-start[i].y)/direction.y);
            var vertices=new List<Vector3>();
            var colors=new List<Color>();
            var sides=new List<int>(); var sources=new List<int>(); var patches=new List<int>();
            for(int i=0;i<4;i++)
            {
                int next=(i+1)%4;
                Quad(vertices,colors,sides,start[i],start[next],end[next],end[i],0.80f,0.80f,0.08f,0.08f);
            }
            Quad(vertices,colors,sources,start[0],start[1],start[2],start[3],1,1,1,1);
            Quad(vertices,colors,patches,end[0],end[1],end[2],end[3],1,1,1,1);
            var mesh=new Mesh {name="WindowBeam_ClippedAtFloor",subMeshCount=3};
            mesh.SetVertices(vertices);mesh.SetColors(colors);
            mesh.SetTriangles(sides,0);mesh.SetTriangles(sources,1);mesh.SetTriangles(patches,2);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static void Quad(List<Vector3> vertices,List<Color> colors,List<int> indices,
            Vector3 a,Vector3 b,Vector3 c,Vector3 d,float aa,float ab,float ac,float ad)
        {
            int offset=vertices.Count;
            vertices.AddRange(new[]{a,b,c,d});
            colors.AddRange(new[]{new Color(1,1,1,aa),new Color(1,1,1,ab),new Color(1,1,1,ac),new Color(1,1,1,ad)});
            indices.AddRange(new[]{offset,offset+1,offset+2,offset,offset+2,offset+3});
        }

        public static void UpdateExistingTest()
        {
            var scene=EditorSceneManager.OpenScene(LowPolySceneBuilder.ScenePath);
            AddToScene();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            LowPolySceneBuilder.Validate();
            Debug.Log("WINDOW_BEAM_VALIDATION_OK");
        }
    }
}
