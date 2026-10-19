using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class ReturnJourneyAudioRegressionTests
    {
        private const string Pending = "TapTap.ReturnJourneyAudioRegression";
        private static readonly List<string> results = new List<string>();
        private static readonly Dictionary<SoundId,int> counts = new Dictionary<SoundId,int>();
        private static string Output => Path.GetFullPath("Logs/Audio/ReturnJourney");
        private static int step, frame, pausedSamples;
        private static double deadline, since;
        private static AudioManager audio;
        private static PlayerController player;
        private static ReturnJourneyPresentation presentation;
        private static ReturnJourneyPlatform platform;
        private static ReturnJourneyTrigger departure;
        private static ReturnJourneyDoor door;
        private static ProgressKey key;
        private static SawAudioEmitter saw, secondSaw;
        private static GameObject sawRoot, secondSawRoot, regionRoot;

        static ReturnJourneyAudioRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false)) return;
                step=0; frame=-1; deadline=EditorApplication.timeSinceStartup+90;
                since=EditorApplication.timeSinceStartup;
                results.Clear(); counts.Clear();
                EditorApplication.update+=Tick;
            };
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated project copy.");
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(Pending,true);
            EditorApplication.isPlaying=true;
        }
        private static int Count(SoundId id) => counts.TryGetValue(id,out int value)?value:0;
        private static void Heard(SoundId id) => counts[id]=Count(id)+1;
        private static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
        private static void Pass(string message) { results.Add("PASS: "+message); Debug.Log("JOURNEY AUDIO PASS: "+message); }
        private static void Next() { step++; since=EditorApplication.timeSinceStartup; }
        private static AudioSource Source(SoundId id) => audio.GetComponentsInChildren<AudioSource>()
            .FirstOrDefault(source=>source.clip!=null && audio.Library.Find(id).Clips.Contains(source.clip));

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || frame==Time.frameCount) return;
                frame=Time.frameCount;
                if (EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Step "+step);
                double elapsed=EditorApplication.timeSinceStartup-since;
                if(step==0)
                {
                    audio=AudioManager.Instance;
                    audio.SoundPlayed+=Heard;
                    foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
                    {
                        SoundDefinition sound=audio.Library.Find(id);
                        Check(sound!=null && sound.Clips.All(clip=>clip!=null && clip.length>0),"Missing catalog cue "+id);
                    }
                    Pass("All 26 cues import and resolve clips, including the five new cues");
                    var floor=new GameObject("Floor");
                    floor.transform.position=Vector3.down*.5f;
                    floor.AddComponent<BoxCollider2D>().size=new Vector2(100,1);
                    floor.AddComponent<WorldSurface>();
                    player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab"))
                        .GetComponent<PlayerController>();
                    player.GetComponent<PlayerInput>().SetExternalInput(0,false);
                    player.TeleportJoined(new Vector2(0,.5f));
                    RewindManager.Current.UseKeyboard=false;
                    presentation=new GameObject("Electrical sequence").AddComponent<ReturnJourneyPresentation>();
                    platform=new GameObject("Worn platform").AddComponent<ReturnJourneyPlatform>();
                    platform.transform.position=new Vector3(2,0,0);
                    platform.View=new GameObject("View").transform;
                    platform.View.SetParent(platform.transform,false);
                    platform.DropDuration=.18f;
                    departure=new GameObject("Begin Return").AddComponent<ReturnJourneyTrigger>();
                    departure.enabled=false; // Arm explicitly after both warning sequences.
                    departure.Cue=ReturnJourneyCue.BeginReturn;
                    departure.Player=player; departure.Presentation=presentation; departure.Platform=platform;
                    departure.transform.position=new Vector3(50,0,0);
                    key=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Interactables/ProgressKey.prefab"))
                        .GetComponent<ProgressKey>();
                    key.transform.position=new Vector3(3,1,0);
                    key.ZoomInDuration=key.ZoomOutDuration=.02f; key.ZoomHoldDuration=0;
                    Physics2D.SyncTransforms();
                    CaptureKey(key.GetComponent<MeshRenderer>(),"key-behind-wall");
                    Next();
                }
                else if(step==1 && elapsed>.25)
                {
                    presentation.PlayWarning();
                    Check(Count(SoundId.PowerFlicker)==1,"First flash missing: count="+Count(SoundId.PowerFlicker)
                        +" playing="+presentation.IsWarningPlaying+" blend="+presentation.Blend+" time="+Time.timeScale
                        +" gameplay="+audio.CanPlayGameplay+" stage="+GameStageProgression.StageIn(player.gameObject.scene)
                        +" inputLock="+GameplayActionLocks.IsLocked(GameplayAction.Input,player.gameObject.scene));
                    Next();
                }
                else if(step==2 && elapsed>.06)
                {
                    Time.timeScale=0;
                    Next();
                }
                else if(step==3 && elapsed>.05)
                {
                    pausedSamples=Source(SoundId.PowerFlicker).timeSamples;
                    Next();
                }
                else if(step==4 && elapsed>.25)
                {
                    Check(Count(SoundId.PowerFlicker)==1,"Pause emitted the second flash early");
                    Check(Source(SoundId.PowerFlicker).timeSamples==pausedSamples,"Flash audio advanced while paused");
                    Time.timeScale=1;
                    Next();
                }
                else if(step==5 && !presentation.IsWarningPlaying)
                {
                    Check(Count(SoundId.PowerFlicker)==2,"First warning should contain exactly two flash sounds");
                    presentation.PlayWarning();
                    Next();
                }
                else if(step==6 && !presentation.IsWarningPlaying)
                {
                    Check(Count(SoundId.PowerFlicker)==4,"Two warnings should contain exactly four flash sounds");
                    Pass("Four flash starts produce four electrical pulses; pausing freezes the timeline and audio");
                    departure.enabled=true;
                    Check(departure.Trigger(),"Third departure trigger rejected");
                    Check(Count(SoundId.PowerDown)==1,"Third cue should start one motor power-down");
                    Check(Count(SoundId.PlatformDetach)==0,"Platform detached before the power-down entry finished");
                    Next();
                }
                else if(step==7 && !departure.IsPlaying)
                {
                    Check(platform.HasCollapsed && Count(SoundId.PlatformDetach)==1,"Missing platform detachment cue");
                    platform.Collapse();
                    Check(Count(SoundId.PlatformDetach)==1,"Platform replayed detachment");
                    Pass("Third trigger powers down once, then the actual platform collapse sounds once");
                    Check(key.TryCollect(player),"Key collection failed");
                    Next();
                }
                else if(step==8 && GameStageProgression.StageIn(player.gameObject.scene)==GameStage.CarryingKey
                    && !GameStageProgression.InputBlocked && elapsed>.25)
                {
                    CaptureKey(key.CarryVisual.GetComponent<MeshRenderer>(),"carried-key-behind-wall");
                    var carryRenderer=key.CarryVisual.GetComponent<MeshRenderer>();
                    Check(carryRenderer.sharedMaterial.GetFloat("_GlowIntensity")>1f,"Carried key has no HDR glow");
                    Check(key.CarryVisual.Find("Particle Effects/White Glow")!=null,"Missing editable carry particles");
                    var glow=key.CarryVisual.GetComponentInChildren<ParticleSystem>();
                    Check(glow!=null && glow.isPlaying && glow.particleCount>0,"Carried key does not emit particles: playing="
                        +(glow!=null && glow.isPlaying)+" count="+(glow!=null?glow.particleCount:-1));
                    CaptureCarryBloom(carryRenderer);
                    door=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ReturnJourney/ReturnJourneyDoor.prefab"))
                        .GetComponent<ReturnJourneyDoor>();
                    door.transform.position=new Vector3(8,.5f,0); door.Player=player;
                    door.KeyTravelDuration=.10f; door.OpenDuration=.40f;
                    Check(door.TryOpen(),"Door did not accept carried key");
                    Next();
                }
                else if(step==9 && Count(SoundId.DoorUnlock)>0)
                {
                    Check(Count(SoundId.DoorUnlock)==1 && door.IsOpening,"Unlock missing or late");
                    Check(GameplayActionLocks.IsLocked(GameplayAction.Input,player.gameObject.scene),"Door did not lock input");
                    Check(!audio.Play(SoundId.Bounce),"Ordinary gameplay cue bypassed cinematic lock");
                    Time.timeScale=0;
                    Next();
                }
                else if(step==10 && elapsed>.05)
                {
                    pausedSamples=Source(SoundId.DoorUnlock).timeSamples;
                    Next();
                }
                else if(step==11 && elapsed>.20)
                {
                    Check(Source(SoundId.DoorUnlock).timeSamples==pausedSamples,"Unlock advanced during pause");
                    Time.timeScale=1;
                    Next();
                }
                else if(step==12 && !door.IsOpening)
                {
                    Check(door.IsOpen && Count(SoundId.DoorUnlock)==1,"Door completion replayed or lost unlock");
                    Check(key.CarryVisual==null && GameObject.Find("Carried Progress Key")==null,"Consumed key left its glow or particles behind");
                    Pass("Unlock occurs at insertion despite the cinematic input lock, pauses, and never replays on completion");
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Terrain/Grid/GridSpikes.prefab");
                    regionRoot=new GameObject("Saw discovery region");
                    regionRoot.AddComponent<RegionRevealView>();
                    sawRoot=Object.Instantiate(prefab,regionRoot.transform);
                    sawRoot.transform.position=new Vector3(3,0,0);
                    saw=sawRoot.GetComponentInChildren<SawAudioEmitter>();
                    Check(saw!=null,"Real saw prefab has no emitter");
                    Check(saw.GainAt(player.Body.Motor.Position,1,out _)==0,"Undiscovered saw leaked audio");
                    regionRoot.GetComponent<RegionRevealView>().SetDiscovered(true,true);
                    Check(saw.GainAt(player.Body.Motor.Position,1,out _)>0,"Nearby saw inaudible");
                    Check(saw.GainAt(new Vector2(30,0),1,out _)==0,"Far saw audible");
                    secondSawRoot=Object.Instantiate(prefab,regionRoot.transform);
                    secondSawRoot.transform.position=new Vector3(4,0,0);
                    secondSaw=secondSawRoot.GetComponentInChildren<SawAudioEmitter>();
                    Next();
                }
                else if(step==13 && elapsed>.25)
                {
                    Check(audio.GetComponentsInChildren<AudioSource>().Count(source=>source.loop
                        && source.clip==audio.Library.Find(SoundId.SawLoop).Clips[0])==1,"Adjacent saws multiplied loop sources");
                    saw.MotorAnimation.enabled=false;
                    Check(saw.GainAt(player.Body.Motor.Position,1,out _)==0,"Stopped saw remained audible");
                    sawRoot.transform.position=secondSawRoot.transform.position=new Vector3(30,0,0);
                    Next();
                }
                else if(step==14 && elapsed>.25)
                {
                    Check(Source(SoundId.SawLoop)==null,"Saw loop leaked outside listening range");
                    Pass("Real animated saw prefab: proximity fade, discovery, stopped motor and aggregate loop cleanup");
                    Finish(null);
                }
            }
            catch(Exception error){Finish(error);}
        }

        private static void CaptureKey(MeshRenderer renderer,string name)
        {
            Check(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"Key occlusion test requires graphics");
            Check(renderer.sharedMaterial.shader==Resources.Load<Material>(ProgressKey.OverlayMaterialPath).shader,"Key does not use foreground shader");
            Check(renderer.sharedMaterial.renderQueue>=3150,"Key render queue is behind magnetic effects");
            Check(!ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader),"Overlay shader failed compilation");
            int oldLayer=renderer.gameObject.layer;
            renderer.gameObject.layer=31;
            var cameraObject=new GameObject("Key regression camera");
            var camera=cameraObject.AddComponent<Camera>();
            Vector3 centre=renderer.bounds.center;
            camera.transform.position=centre-Vector3.forward*8;
            camera.orthographic=true; camera.orthographicSize=.8f;
            camera.cullingMask=1<<31;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.layer=31; wall.transform.position=centre-Vector3.forward*.5f;
            wall.transform.localScale=new Vector3(2,2,.3f);
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor",new Color(.07f,.09f,.12f));
            wall.GetComponent<Renderer>().sharedMaterial=material;
            var target=new RenderTexture(384,384,24);
            camera.targetTexture=target;
            var image=new Texture2D(384,384,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active;
            try
            {
                camera.Render(); RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,384,384),0,0); image.Apply();
                int visible=image.GetPixels32().Count(c=>c.r>200 && c.g>200 && c.b>200);
                Check(visible>100,"Key still hidden behind opaque wall: "+visible);
                File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
                renderer.enabled=false;
                camera.Render(); RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,384,384),0,0); image.Apply();
                Check(image.GetPixels32().Count(c=>c.r>200 && c.g>200 && c.b>200)==0,"Wall control image contains false key pixels");
                Pass(name+": GPU capture shows the key through the wall ("+visible+" key pixels), control image verifies occlusion");
            }
            finally
            {
                renderer.enabled=true; renderer.gameObject.layer=oldLayer;
                RenderTexture.active=previous; camera.targetTexture=null;
                Object.DestroyImmediate(image); Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(wall); Object.DestroyImmediate(material);
            }
        }
        private static void CaptureCarryBloom(MeshRenderer renderer)
        {
            int oldLayer=renderer.gameObject.layer;
            renderer.gameObject.layer=31;
            var cameraObject=new GameObject("Carry glow capture");
            var camera=cameraObject.AddComponent<Camera>();
            camera.transform.position=renderer.bounds.center-Vector3.forward*8;
            camera.orthographic=true; camera.orthographicSize=.8f;
            camera.cullingMask=1<<31; camera.allowHDR=true;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            var data=camera.GetUniversalAdditionalCameraData();
            data.volumeLayerMask=1<<31;
            var volumeObject=new GameObject("Carry glow volume"); volumeObject.layer=31;
            var volume=volumeObject.AddComponent<Volume>(); volume.isGlobal=true; volume.priority=10000;
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom=profile.Add<Bloom>(); bloom.intensity.Override(.5f); bloom.threshold.Override(.9f);
            volume.sharedProfile=profile;
            var target=new RenderTexture(384,384,24,RenderTextureFormat.ARGBHalf);
            camera.targetTexture=target;
            var image=new Texture2D(384,384,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active;
            try
            {
                data.renderPostProcessing=false;
                camera.Render(); RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,384,384),0,0); image.Apply();
                Color32[] baseline=image.GetPixels32();
                File.WriteAllBytes(Path.Combine(Output,"carried-key-without-bloom.png"),image.EncodeToPNG());
                data.renderPostProcessing=true;
                camera.Render(); RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,384,384),0,0); image.Apply();
                Color32[] glowing=image.GetPixels32();
                int halo=Enumerable.Range(0,baseline.Length).Count(i=>baseline[i].r<10 && glowing[i].r>30);
                File.WriteAllBytes(Path.Combine(Output,"carried-key-with-bloom.png"),image.EncodeToPNG());
                Check(halo>100,"HDR key did not create a visible Bloom halo: "+halo);
                Pass("Carried prefab emits editable particles, HDR Bloom adds "+halo+" halo pixels");
            }
            finally
            {
                renderer.gameObject.layer=oldLayer; RenderTexture.active=previous; camera.targetTexture=null;
                Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(volumeObject); Object.DestroyImmediate(bloom); Object.DestroyImmediate(profile);
            }
        }

        private static void Finish(Exception error)
        {
            Time.timeScale=1;
            SessionState.SetBool(Pending,false);
            EditorApplication.update-=Tick;
            if(audio!=null)audio.SoundPlayed-=Heard;
            if(error!=null){results.Add("FAIL: "+error);Debug.LogException(error);}
            File.WriteAllLines(Path.Combine(Output,"results.txt"),results);
            EditorApplication.Exit(error==null?0:1);
        }
    }
}
