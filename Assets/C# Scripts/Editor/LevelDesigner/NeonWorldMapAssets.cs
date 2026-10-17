using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    public static class NeonWorldMapAssets
    {
        public const string Root = "Assets/Prefabs/UI/WorldMap";
        public const string ScreenPath = "Assets/Resources/WorldMapSelection/WorldCharacterSelection.prefab";
        public const string FontPath = "Assets/Fonts/ResourceHanRoundedCN-Bold.ttf";
        [Serializable] private class Layout { public Vector2 mapOrigin, mapSize; public Room[] levels; }
        [Serializable] private class Room { public int sequence; public string moduleId; public Vector2 origin, label; public Vector2[] polygon; }

        [MenuItem("Tools/TapTap/UI/Build Neon World Selection Prefabs")]
        public static void Build()
        {
            Layout layout = JsonUtility.FromJson<Layout>(File.ReadAllText("Assets/EditorData/WorldCharacterSelection.json"));
            if (layout.levels.Length != 20) throw new InvalidOperationException("Expected the approved 20-room layout.");
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) throw new InvalidOperationException("The supplied Resource Han Rounded font is missing.");
            Ensure(Root + "/Levels"); Ensure("Assets/Resources/WorldMapSelection"); Ensure("Assets/Art/Materials/UI");
            Material material=Material("Assets/Art/Materials/UI/NeonLevelOutline.mat","TapTap/UI/Neon Level Outline");
            material.SetFloat("_Emission",3.2f); material.SetFloat("_TextMode",0f);
            Material numberMaterial=Material("Assets/Art/Materials/UI/NeonLevelNumber.mat","TapTap/UI/Neon Level Outline");
            numberMaterial.SetFloat("_Emission",1.35f); numberMaterial.SetFloat("_TextMode",1f);
            Material("Assets/Resources/WorldMapSelection/ScreenCover.mat","TapTap/UI/Screen Cover");
            const string profilePath="Assets/Art/Materials/UI/WorldMapBloom.asset";
            VolumeProfile profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile==null) { profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,profilePath); }
            if (!profile.TryGet(out Bloom bloom))
            {
                bloom=profile.Add<Bloom>(true); AssetDatabase.AddObjectToAsset(bloom,profile);
            }
            bloom.active=true; bloom.threshold.Override(0.9f); bloom.intensity.Override(0.78f);
            bloom.scatter.Override(0.55f); bloom.highQualityFiltering.Override(true);
            EditorUtility.SetDirty(bloom); EditorUtility.SetDirty(profile);
            var piecePrefabs = new List<GameObject>();
            foreach (Room room in layout.levels)
            {
                RectTransform rect = Rect("Level_" + room.sequence.ToString("00"), null, Vector2.zero, new Vector2(74, 127));
                rect.pivot = Vector2.zero;
                NeonLevelPiece piece = rect.gameObject.AddComponent<NeonLevelPiece>();
                piece.Sequence = room.sequence; piece.ModuleId = room.moduleId;
                piece.DiagramLabelPosition = room.label;
                piece.Tint = Tint(room.sequence);
                piece.Outline = rect.gameObject.AddComponent<NeonLevelOutline>();
                Vector2[] points = RoundCorners(room.polygon);
                for (int i = 0; i < points.Length; i++) points[i] -= room.origin;
                piece.Outline.Polygon = points; piece.Outline.material = material;
                piece.Outline.color = piece.Tint; piece.Outline.raycastTarget = false;
                piece.Number = Label("Number", rect, room.label - room.origin, new Vector2(84, 56), room.sequence.ToString("00"), 32, font);
                piece.Number.rectTransform.localScale = Vector3.one * 0.1f;
                piece.Number.rectTransform.anchorMin = piece.Number.rectTransform.anchorMax = Vector2.zero;
                piece.Number.color = Color.Lerp(piece.Tint, Color.white, 0.75f);
                piece.Number.material=numberMaterial;
                piece.SetIntensity(0.50f);
                piecePrefabs.Add(PrefabUtility.SaveAsPrefabAsset(rect.gameObject, Root + "/Levels/Level_" + room.sequence.ToString("00") + ".prefab"));
                Object.DestroyImmediate(rect.gameObject);
            }
            RectTransform board = Rect("WorldCharacterBoard", null, Vector2.zero, layout.mapSize);
            board.pivot = Vector2.zero;
            foreach (Room room in layout.levels)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(piecePrefabs[room.sequence - 1], board);
                RectTransform rect = instance.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                rect.anchoredPosition = room.origin - layout.mapOrigin;
                PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            }
            GameObject boardPrefab = PrefabUtility.SaveAsPrefabAsset(board.gameObject, Root + "/WorldCharacterBoard.prefab");
            Object.DestroyImmediate(board.gameObject);
            var root = new GameObject("WorldCharacterSelection");
            NeonWorldMapScreen screen = root.AddComponent<NeonWorldMapScreen>();
            screen.Source = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            screen.MapSize = layout.mapSize;
            var cameraObject = new GameObject("UI Camera"); cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 0, -10);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5; camera.depth = 100;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 50;
            camera.allowHDR=true;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.002f,0.004f,0.008f);
            UniversalAdditionalCameraData cameraData=camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing=true; cameraData.volumeLayerMask=1<<31;
            cameraData.renderShadows=false;
            var volumeObject=new GameObject("Map Bloom",typeof(Volume));
            volumeObject.layer=31; volumeObject.transform.SetParent(root.transform,false);
            Volume volume=volumeObject.GetComponent<Volume>();
            volume.isGlobal=true; volume.priority=10000f; volume.sharedProfile=profile;
            screen.BloomVolume=volume;
            screen.UICamera = camera;
            RectTransform canvasRect = Rect("Canvas", root.transform, Vector2.zero, new Vector2(1920, 1080));
            Canvas canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            canvas.sortingOrder = 100;
            canvas.additionalShaderChannels=AdditionalCanvasShaderChannels.TexCoord1;
            CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            screen.FadeGroup = canvasRect.gameObject.AddComponent<CanvasGroup>(); screen.Canvas = canvas;
            RectTransform host = Rect("MapViewport", canvasRect, Vector2.zero, Vector2.zero);
            host.anchorMin = new Vector2(0.06f,0.06f); host.anchorMax = new Vector2(0.94f,0.94f);
            host.offsetMin = host.offsetMax = Vector2.zero; screen.MapHost = host;
            GameObject boardInstance = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, host);
            screen.Board = boardInstance.GetComponent<RectTransform>();
            screen.Board.anchorMin = screen.Board.anchorMax = Vector2.one * 0.5f;
            screen.Board.pivot = Vector2.zero;
            screen.Pieces = boardInstance.GetComponentsInChildren<NeonLevelPiece>(true);
            PrefabUtility.SaveAsPrefabAsset(root, ScreenPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("Built 20 neon level prefabs, their shared board, and the selection/transition screen.");
        }

        public static void BuildBatch()
        {
            try { Build(); NeonWorldMapValidation.ValidateAssets(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        // Called by scene builders as well as the explicit placement command.
        // Existing instances and their artist-authored overrides are retained.
        public static NeonWorldMapScreen EnsureSceneScreen(Scene scene,GameObject owner,WorldMapDefinition map,bool selection)
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath);
            if (prefab==null || prefab.GetComponent<NeonWorldMapScreen>().Source!=map) return null;
            NeonWorldMapScreen screen=null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (NeonWorldMapScreen existing in root.GetComponentsInChildren<NeonWorldMapScreen>(true))
                    if (existing.Source==map) screen=existing;
            if (screen==null)
                screen=((GameObject)PrefabUtility.InstantiatePrefab(prefab,scene)).GetComponent<NeonWorldMapScreen>();
            screen.ShowOnSceneStart=selection;
            PrefabUtility.RecordPrefabInstancePropertyModifications(screen);
            WorldMapAssemblyView view=owner.GetComponent<WorldMapAssemblyView>();
            if (view==null) view=owner.AddComponent<WorldMapAssemblyView>();
            view.SceneScreen=screen;
            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(scene);
            return screen;
        }

        [MenuItem("Tools/TapTap/UI/Place Neon Map In Saved Scenes")]
        public static void PlaceInSavedScenes()
        {
            var map=AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            foreach (string path in new[]{map.SelectionScenePath,map.PlayScenePath})
            {
                Scene scene=SceneManager.GetSceneByPath(path);
                bool opened=!scene.IsValid()||!scene.isLoaded;
                if (opened) scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    GameObject owner=null;
                    bool selection=path==map.SelectionScenePath;
                    foreach (GameObject root in scene.GetRootGameObjects())
                        if (selection?root.GetComponent<WorldLevelMenu>()!=null:root.GetComponent<WorldMapRoot>()!=null) owner=root;
                    if (owner==null) throw new InvalidOperationException("Missing map/menu root in "+path);
                    EnsureSceneScreen(scene,owner,map,selection);
                    EditorSceneManager.SaveScene(scene,path);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene,true); }
            }
        }

        private static Color Tint(int n) => n <= 5 ? new Color(1f,0.43f,0.39f) : n <= 12 ? new Color(0.40f,0.72f,0.90f) : new Color(1f,0.78f,0.40f);

        private static Vector2[] RoundCorners(Vector2[] points)
        {
            var result=new List<Vector2>();
            for (int i=0;i<points.Length;i++)
            {
                Vector2 p=points[i];
                bool round=p==new Vector2(85,131)||p==new Vector2(85,4)||p==new Vector2(11,4);
                if (!round) { result.Add(p); continue; }
                Vector2 before=(points[(i+points.Length-1)%points.Length]-p).normalized;
                Vector2 after=(points[(i+1)%points.Length]-p).normalized;
                Vector2 center=p+(before+after)*4f;
                Vector2 start=p+before*4f-center, end=p+after*4f-center;
                float angle=Mathf.Atan2(start.y,start.x), delta=Mathf.DeltaAngle(angle*Mathf.Rad2Deg,Mathf.Atan2(end.y,end.x)*Mathf.Rad2Deg)*Mathf.Deg2Rad;
                for (int step=0;step<=12;step++)
                {
                    float a=angle+delta*step/12f; result.Add(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*4f);
                }
            }
            return result.ToArray();
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer=31; rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*0.5f;
            rect.anchoredPosition=position; rect.sizeDelta=size;
            return rect;
        }
        private static Text Label(string name,Transform parent,Vector2 position,Vector2 size,string value,int fontSize,Font font)
        {
            Text text=Rect(name,parent,position,size).gameObject.AddComponent<Text>();
            text.font=font; text.fontSize=fontSize; text.text=value; text.alignment=TextAnchor.MiddleCenter;
            text.horizontalOverflow=HorizontalWrapMode.Overflow; text.verticalOverflow=VerticalWrapMode.Overflow;
            text.color=new Color(0.8f,0.92f,0.96f); text.raycastTarget=false;
            return text;
        }
        private static Material Material(string path,string shaderName)
        {
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader=Shader.Find(shaderName);
            if (shader==null) throw new InvalidOperationException("Missing URP shader: "+shaderName);
            if (material==null) { material=new Material(shader); AssetDatabase.CreateAsset(material,path); }
            else material.shader=shader;
            EditorUtility.SetDirty(material); return material;
        }
        private static void Ensure(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/'); Ensure(parent);
            AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
    }
}
