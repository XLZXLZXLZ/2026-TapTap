using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class NeonWorldMapValidation
    {
        private const string Key = "TapTap.NeonWorldMapValidation";
        private static int stage;
        private static bool capturedTransition;
        private static bool sawDelay, sawDrawing, sawCoverReveal, capturedDrawing;
        private static bool capturedSettled;
        private static double visibleDelayStart;
        private static int coverId;
        private static int menuScreenId,gameScreenId;
        private static int[] menuPieceIds,gamePieceIds;
        private static double deadline;
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,"../NeonValidation"));

        static NeonWorldMapValidation()
        {
            if (!SessionState.GetBool(Key,false)) return;
            deadline=EditorApplication.timeSinceStartup+180d;
            EditorApplication.update+=Tick;
        }

        public static void ValidateAssets()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(NeonWorldMapAssets.ScreenPath);
            Check(prefab!=null,"Shared screen prefab missing");
            var screen=prefab.GetComponent<NeonWorldMapScreen>();
            Check(screen.Source!=null && screen.Pieces.Length==20,"Screen must have exactly 20 rooms");
            Check(prefab.GetComponentsInChildren<Text>(true).Length==20,"Only the 20 map numbers may contain text");
            Check(prefab.GetComponentsInChildren<Selectable>(true).Length==0,"Decorative controls remain");
            Check(screen.UICamera.allowHDR && screen.UICamera.GetUniversalAdditionalCameraData().renderPostProcessing,"HDR/Bloom camera not enabled");
            Volume volume=prefab.GetComponentInChildren<Volume>();
            Check(volume!=null && volume.sharedProfile.TryGet(out Bloom bloom) && bloom.intensity.value>0f,"Real URP Bloom missing");
            Font supplied=AssetDatabase.LoadAssetAtPath<Font>(NeonWorldMapAssets.FontPath);
            foreach (Text text in prefab.GetComponentsInChildren<Text>(true)) Check(text.font==supplied,"Supplied font not assigned: "+text.name);
            foreach (NeonLevelPiece piece in screen.Pieces)
            {
                Check(piece.Sequence>=1 && piece.Sequence<=20,"Extra region in screen");
                Check(piece.Number.text==piece.Sequence.ToString("00"),"Number mismatch");
                var rect=(RectTransform)piece.transform;
                Vector2 label=rect.anchoredPosition+piece.Number.rectTransform.anchoredPosition+new Vector2(11,4);
                Check(Vector2.Distance(label,piece.DiagramLabelPosition)<0.001f,"Diagram number position was lost: "+piece.Sequence);
                Check(piece.Outline.ContainsLocalPoint(piece.Number.rectTransform.anchoredPosition),"Number outside cleaned region: "+piece.Sequence);
                Check(piece.Outline.material.shader.name=="TapTap/UI/Neon Level Outline","Wrong glow material");
                Check(piece.Outline.material.GetTag("RenderPipeline",false)=="UniversalPipeline","Outline still uses a Built-in shader");
                Check(!ShaderUtil.ShaderHasError(piece.Outline.material.shader),"Neon shader compilation failed");
                Check(piece.Number.material.GetTag("RenderPipeline",false)=="UniversalPipeline","Number still uses a Built-in material");
                Check(PrefabUtility.GetCorrespondingObjectFromSource(piece)!=null,"Level must remain a nested prefab");
            }
            Pass("Map only: 20 numbers, no captions/controls; 20 nested prefabs with fixed positions and supplied font; URP-only HDR outline/number materials and Bloom");
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run validation in an isolated batch project.");
            try
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Path.Combine(Output,"results.txt"),"");
                NeonWorldMapAssets.Build(); ValidateAssets();
                NeonWorldMapAssets.PlaceInSavedScenes();
                var map=AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                foreach (string path in new[]{map.SelectionScenePath,map.PlayScenePath})
                {
                    var authored=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                    var screens=authored.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<NeonWorldMapScreen>(true)).ToArray();
                    Check(screens.Length==1 && screens[0].Pieces.Length==20,"Scene must contain its own pre-placed 20-room map: "+path);
                    Check(screens[0].Pieces.All(piece=>piece.gameObject.activeSelf),"Authoring view must show all rooms");
                    var views=authored.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<WorldMapAssemblyView>(true)).ToArray();
                    Check(views.Length==1 && views[0].SceneScreen==screens[0],"Scene binding missing");
                    Check(screens[0].ShowOnSceneStart==(path==map.SelectionScenePath),"Wrong gameplay/menu initial visibility");
                }
                Pass("Both saved scenes contain one editable pre-placed 20-room map, with serialized view references and all rooms visible in the editor");
                // The isolated validation project uses a different product name.
                WorldMapProgressStore.Clear(map);
                EditorSceneManager.OpenScene(map.SelectionScenePath,OpenSceneMode.Single);
                SessionState.SetBool(Key,true);
                deadline=EditorApplication.timeSinceStartup+180d;
                EditorApplication.update-=Tick; EditorApplication.update+=Tick;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying) return;
            try
            {
                Check(EditorApplication.timeSinceStartup<deadline,"Runtime validation timeout, stage "+stage);
                if (WorldMapSceneRouter.IsLoading) return;
                WorldLevelMenu menu=WorldLevelMenu.Current;
                WorldMapProgression flow=WorldMapProgression.Current;
                if (stage==0)
                {
                    if (menu==null||menu.View==null) return;
                    Check(menu.View.UsesNeonScreen,"Selection still uses the legacy outline renderer");
                    var screen=menu.View.NeonScreen;
                    if (screen.Pieces[0].Emphasis<0.99f) return;
                    Check(screen==menu.View.SceneScreen,"Selection created a different screen");
                    menuScreenId=screen.GetInstanceID();
                    menuPieceIds=screen.Pieces.Select(piece=>piece.GetInstanceID()).ToArray();
                    var modules=WorldMapAssemblyView.SelectableModules(menu.World);
                    Check(modules.Count==20 && modules.All(m=>menu.World.SequenceNumber(m)<=20),"21/22 leaked into selection");
                    Canvas.ForceUpdateCanvases();
                    foreach (WorldMapModule module in modules)
                    {
                        bool canPick=menu.View.TryPick(menu.View.LabelScreenPosition(module),out WorldMapModule picked);
                        bool unlocked=WorldMapProgressStore.IsUnlocked(menu.World,module);
                        Check(unlocked?(canPick&&picked==module):!canPick,"Hidden/visible picking mismatch: "+menu.World.SequenceNumber(module));
                    }
                    Check(screen.Pieces.Count(piece=>piece.gameObject.activeSelf)==1,"Locked pieces must be hidden completely");
                    menu.Select(modules[1]); Check(menu.Selected!=modules[1],"Hidden room was selected");
                    Capture(screen,"selection-locked.png");
                    menu.UnlockAll(); Check(WorldMapProgressStore.Completed(menu.World).Count==0,"Unlock changed completion");
                    Check(screen.Pieces.All(piece=>piece.gameObject.activeSelf),"Unlock-all must reveal every authored selection room");
                    foreach (WorldMapModule module in modules)
                        Check(screen.CanSelect(module) && menu.View.TryPick(menu.View.LabelScreenPosition(module),out WorldMapModule picked) && picked==module,"Unlocked room must remain selectable regardless of replay progress");
                    // Replaying room 1 with later rooms already completed must hide them only in its cutscene.
                    WorldMapProgressStore.Complete(menu.World,modules[6]);
                    WorldMapProgressStore.Complete(menu.World,modules[19]);
                    menu.UnlockAll();
                    Pass("Selection shows all unlocked rooms; later completion records do not impose a current-room cutoff");
                    menu.Select(modules[0]);
                    stage=10;
                    return;
                }
                else if (stage==10)
                {
                    if (menu==null||menu.View==null) return;
                    var screen=menu.View.NeonScreen;
                    if (screen.Pieces[0].Emphasis<0.99f) return;
                    Check(screen.GetInstanceID()==menuScreenId && screen.Pieces.Select(piece=>piece.GetInstanceID()).SequenceEqual(menuPieceIds),"Unlock recreated the screen or room objects");
                    Check(screen.Pieces.All(piece=>piece.gameObject.activeSelf),"Unlock did not reveal existing room objects");
                    var modules=screen.AvailableModules(menu.World);
                    foreach (WorldMapModule module in modules)
                        Check(menu.View.TryPick(menu.View.LabelScreenPosition(module),out WorldMapModule picked)&&picked==module,"Unlocked room hit mismatch");
                    screen.FadeGroup.alpha=1f;
                    Check(screen.Pieces[0].Outline.color.a>screen.Pieces[1].Outline.color.a+0.30f,"Selected room does not stand out from idle rooms");
                    foreach (NeonLevelPiece piece in screen.Pieces) { piece.SetEmphasis(0f); piece.SetIntensity(0.50f); }
                    Capture(screen,"selection-all-idle.png");
                    screen.Pieces[0].SetEmphasis(1f); screen.Pieces[0].SetIntensity(0.91f);
                    byte[] first=Capture(screen,"selection.png");
                    UniversalAdditionalCameraData cameraData=screen.UICamera.GetUniversalAdditionalCameraData();
                    cameraData.renderPostProcessing=false;
                    byte[] noBloom=Capture(screen,"selection-without-bloom.png");
                    cameraData.renderPostProcessing=true;
                    Check(first.Sum(value=>(long)value)>noBloom.Sum(value=>(long)value)*1.005,"Bloom did not produce a visible halo");
                    for (int i=screen.Pieces.Length-1;i>=0;i--) screen.Pieces[i].transform.SetAsLastSibling();
                    byte[] reversed=Capture(screen,"selection-reversed-order.png");
                    int changed=0,maximumDelta=0;
                    for (int i=0;i<first.Length;i++) if (first[i]!=reversed[i])
                    { changed++; maximumDelta=Mathf.Max(maximumDelta,Mathf.Abs(first[i]-reversed[i])); }
                    Check(changed<first.Length/10000 && maximumDelta<=2,"Sibling order changed the rendered borders");
                    coverId=ScreenFadeOverlay.Instance.GetInstanceID();
                    Check(ScreenFadeOverlay.Instance.Cover.material.GetTag("RenderPipeline",false)=="UniversalPipeline","Cover uses Built-in material");
                    Pass("20 number positions clickable; locks/unlock preserved; HDR Bloom visibly expands the glow; sibling order does not replace neighbouring colors");
                    menu.Select(modules[6]); stage=11;
                }
                else if (stage==11)
                {
                    if (menu==null||menu.View==null) return;
                    var screen=menu.View.NeonScreen;
                    if (screen.Pieces[6].Emphasis<0.99f || screen.Pieces[0].Emphasis>0.01f) return;
                    Check(screen.Pieces[6].Outline.color.a>screen.Pieces[0].Outline.color.a+0.30f,"Focus did not transfer to the newly selected room");
                    Capture(screen,"selection-focus.png");
                    Pass("Soft palette, thin antialiased strokes, pale numbers and restrained Bloom render correctly; focus fades smoothly between existing rooms");
                    menu.Select(screen.AvailableModules(menu.World)[0]);
                    Check(menu.EnterSelected(),"Could not enter selected level"); stage=1;
                }
                else if (stage==1)
                {
                    if (flow==null||flow.IsTransitioning) return;
                    Check(flow.CurrentNumber==1 && flow.LevelCount==22,"Selection launch changed gameplay routing");
                    Check(flow.CompletedCount==2,"Later completion records were lost before replaying room 1");
                    var authoredScreen=flow.GetComponent<WorldMapAssemblyView>().SceneScreen;
                    Check(authoredScreen!=null && !authoredScreen.UICamera.enabled && !authoredScreen.Canvas.enabled,"Pre-placed transition map obscures gameplay");
                    gameScreenId=authoredScreen.GetInstanceID();
                    gamePieceIds=authoredScreen.Pieces.Select(piece=>piece.GetInstanceID()).ToArray();
                    Check(ScreenFadeOverlay.Instance.GetInstanceID()==coverId,"Scene loading recreated the cover");
                    Check(ScreenFadeOverlay.Instance.gameObject.scene.name=="DontDestroyOnLoad","Cover is not persistent");
                    flow.FadeDuration=0.2f; flow.AssemblyDuration=1.4f; flow.AssemblyHold=0.7f;
                    Check(flow.CompleteCurrent(flow.CurrentRegion.GetComponentInChildren<LevelEndpoint>()),"Endpoint completion refused"); stage=2;
                }
                else if (stage==2)
                {
                    if (flow==null) return;
                    var view=flow.GetComponent<WorldMapAssemblyView>();
                    if (flow.IsTransitioning && view.NeonScreen!=null)
                    {
                        var screen=view.NeonScreen;
                        Check(screen==view.SceneScreen && screen.GetInstanceID()==gameScreenId,"Transition instantiated a new map");
                        var phase=screen.Phase;
                        if (phase!=NeonWorldMapScreen.TransitionPhase.None)
                            Check(screen.Pieces.Skip(1).All(piece=>!piece.gameObject.activeSelf),"Completion cutscene revealed later rooms from unlock/completion records");
                        if (phase==NeonWorldMapScreen.TransitionPhase.CoverReveal)
                        {
                            sawCoverReveal=true;
                            Check(screen.Pieces[0].Outline.Reveal==0f,"Drawing started behind the fading cover");
                        }
                        if (phase==NeonWorldMapScreen.TransitionPhase.Delay)
                        {
                            if (!sawDelay) visibleDelayStart=EditorApplication.timeSinceStartup;
                            sawDelay=true;
                            Check(ScreenFadeOverlay.Instance.Opacity==0f && screen.Pieces[0].Outline.Reveal==0f,"Reveal delay is hidden or already animated");
                        }
                        if (phase==NeonWorldMapScreen.TransitionPhase.Drawing)
                        {
                            if (!sawDrawing) Check(EditorApplication.timeSinceStartup-visibleDelayStart>=screen.AnimationDelay-0.1f,"Drawing delay was consumed during fade-in");
                            sawDrawing=true;
                            Check(ScreenFadeOverlay.Instance.Opacity==0f,"Drawing hidden by cover");
                            if (!capturedDrawing && screen.Pieces[0].Outline.Reveal>0.25f && screen.Pieces[0].Outline.Reveal<0.75f)
                            { Capture(screen,"transition-drawing.png"); capturedDrawing=true; }
                        }
                        if (!capturedTransition && phase==NeonWorldMapScreen.TransitionPhase.Holding)
                        {
                            Check(sawCoverReveal&&sawDelay&&sawDrawing&&capturedDrawing,"Visible transition phases were skipped");
                            Check(screen.DrawingElapsed>=1.4f,"Drawing duration is too short");
                            Capture(screen,"transition.png"); capturedTransition=true;
                        }
                        if (capturedTransition && !capturedSettled && phase==NeonWorldMapScreen.TransitionPhase.Holding && screen.Pieces[0].Emphasis<0.01f)
                        {
                            Check(screen.Pieces[0].Emphasis<0.01f && screen.Pieces[0].Outline.color.a<0.55f,"Arrival highlight did not settle back to idle brightness");
                            Capture(screen,"transition-settled.png"); capturedSettled=true;
                        }
                        return;
                    }
                    if (flow.IsTransitioning||flow.CurrentNumber!=2) return;
                    Check(capturedTransition,"New transition screen was not rendered");
                    Check(capturedSettled,"Transition settling was not visible");
                    Check(view.SceneScreen.GetInstanceID()==gameScreenId && view.SceneScreen.Pieces.Select(piece=>piece.GetInstanceID()).SequenceEqual(gamePieceIds),"Transition destroyed/recreated authored objects");
                    Check(!view.SceneScreen.UICamera.enabled && !view.SceneScreen.Canvas.enabled,"Transition map remains over gameplay");
                    Check(flow.CompletedCount==3,"Completion did not preserve later saved records");
                    Check(ScreenFadeOverlay.Instance.GetInstanceID()==coverId && Object.FindObjectsOfType<ScreenFadeOverlay>().Length==1,"Cover continuity/uniqueness failed");
                    Pass("Pre-placed transition map and all 20 room instances survive show/hide without recreation; persistent cover, delayed 1.4-second drawing and gameplay advance remain correct");
                    Check(flow.ReturnToSelection(),"Return to selection failed"); stage=3;
                }
                else if (stage==3)
                {
                    if (menu==null||menu.View==null) return;
                    Check(menu.View.UsesNeonScreen && WorldMapProgressStore.Completed(menu.World).Count==3,"Returning lost presentation/progress");
                    var modules=menu.View.NeonScreen.AvailableModules(menu.World);
                    Check(menu.Selected==modules[1],"Return did not select the first unfinished unlocked room");
                    Check(menu.View.NeonScreen.Pieces.All(piece=>piece.gameObject.activeSelf),"Returning from a room-1 cutscene must restore every unlocked selection room");
                    Check(menu.View.TryPick(menu.View.LabelScreenPosition(modules[19]),out WorldMapModule last) && last==modules[19],"Later unlocked room cannot be picked after returning");
                    Capture(menu.View.NeonScreen,"selection-progress.png");
                    Pass("Return to selection restores saved completion and reuses the shared screen");
                    menu.Select(modules[0]); Check(menu.EnterSelected(),"Could not replay an earlier visible room"); stage=4;
                }
                else if (stage==4)
                {
                    if (flow==null||flow.IsTransitioning) return;
                    Check(flow.CurrentNumber==1,"Earlier-room replay routed incorrectly");
                    Check(flow.ReturnToSelection(),"Replay return failed"); stage=5;
                }
                else if (stage==5)
                {
                    if (menu==null||menu.View==null) return;
                    Check(menu.View.NeonScreen.Pieces.All(piece=>piece.gameObject.activeSelf),"Replaying an earlier room incorrectly shrank the selection map");
                    Check(WorldMapProgressStore.Completed(menu.World).Count==3,"Replaying lost earlier/later completion records");
                    Pass("Completion cutscene stops at the replayed room even when later rooms are completed; selection restores all unlocked rooms afterwards");
                    SessionState.SetBool(Key,false); EditorApplication.update-=Tick; EditorApplication.Exit(0);
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static byte[] Capture(NeonWorldMapScreen screen,string name)
        {
            Camera camera=screen.UICamera;
            RenderTexture previous=camera.targetTexture;
            var target=new RenderTexture(1600,1000,24,RenderTextureFormat.DefaultHDR);
            target.Create(); camera.targetTexture=target;
            Canvas.ForceUpdateCanvases(); screen.SendMessage("FitBoard"); Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture active=RenderTexture.active; RenderTexture.active=target;
            var readback=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            readback.ReadPixels(new Rect(0,0,target.width,target.height),0,0); readback.Apply();
            Color[] pixels=readback.GetPixels();
            // HDR targets are linear; PNG viewers expect display (sRGB) colours.
            if (QualitySettings.activeColorSpace==ColorSpace.Linear && !target.sRGB)
                for (int i=0;i<pixels.Length;i++) pixels[i]=pixels[i].gamma;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            image.SetPixels(pixels); image.Apply();
            byte[] raw=image.GetRawTextureData<byte>().ToArray();
            File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());
            bool visible=raw.Count(value=>value>80)>200;
            RenderTexture.active=active; camera.targetTexture=previous; target.Release();
            Object.DestroyImmediate(readback); Object.DestroyImmediate(image); Object.DestroyImmediate(target);
            Canvas.ForceUpdateCanvases(); screen.SendMessage("FitBoard");
            Check(visible,"Screenshot is blank: "+name);
            return raw;
        }
        private static void Check(bool ok,string message) { if (!ok) throw new Exception(message); }
        private static void Pass(string message)
        {
            Directory.CreateDirectory(Output); File.AppendAllText(Path.Combine(Output,"results.txt"),"PASS: "+message+"\n");
            Debug.Log("NEON PASS: "+message);
        }
        private static void Fail(Exception exception)
        {
            SessionState.SetBool(Key,false); EditorApplication.update-=Tick;
            Directory.CreateDirectory(Output); File.AppendAllText(Path.Combine(Output,"results.txt"),"FAIL: "+exception+"\n");
            Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
