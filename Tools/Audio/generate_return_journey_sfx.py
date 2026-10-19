"""Second sound batch: restrained electrical/mechanical return-journey cues.

Appends missing catalog entries; existing audio mappings and mixer gains are preserved.
"""
import json
import math
from pathlib import Path
import random
import re
import struct
import wave

import generate_sfx as base

ROOT, RATE, TAU = base.ROOT, base.RATE, math.tau
RNG = random.Random(2026101902)


def effect(kind, duration, variant=0):
    samples = []
    phase = low = 0.0
    tune = [0.985, 1.0, 1.015][variant%3]
    for i in range(round(duration*RATE)):
        t = i/RATE
        noise = RNG.uniform(-1,1)
        low += .10*(noise-low)
        if kind == 'flicker':
            phase += TAU*(165*tune)/RATE
            bursts = sum(math.exp(-(t-onset)*70) for onset in [0,.055,.115] if t>=onset)
            s = (.32*math.sin(phase)+.10*math.sin(phase*2)+.65*low)*bursts
        elif kind == 'powerdown':
            progress = t/duration
            frequency = (45+335*(1-progress)**1.8)*tune
            phase += TAU*frequency/RATE
            slipping = .78+.22*math.cos(phase*.16)
            s = (.52*math.sin(phase)+.13*math.sin(phase*2)+.3*low)*slipping
            s *= (.95-.55*progress)*min(1,(duration-t)/.15)
        elif kind == 'detach':
            phase += TAU*(105+22*math.sin(TAU*3*t))*tune/RATE
            s = (.24*math.sin(phase)+.38*low)*math.exp(-t*7)
            for onset,freq,amp in [(.16,145,.7),(.24,210,.32)]:
                u = t-onset
                if u>=0:s += amp*math.sin(TAU*freq*u*tune)*(1-math.exp(-u*350))*math.exp(-u*28)
            for onset in [.22,.29,.37,.43]:
                u = t-onset
                if u>=0:s += .85*(noise-low)*math.exp(-u*80)*(1-math.exp(-u*500))
        else:
            s = .25*low*math.sin(math.pi*min(1,t/.30))**2 if t<.30 else 0
            for onset,freq,amp in [(.045,310,.28),(.12,275,.24),(.20,240,.22),(.285,185,.75)]:
                u = t-onset
                if u>=0:s += amp*math.sin(TAU*freq*u)*(1-math.exp(-u*220))*math.exp(-u*30)
        samples.append(s)
    return samples


def saw_loop():
    partials = [(f,RNG.uniform(0,TAU)) for f in [71.5,103,137.5,182,237.5,293,359.5,421,488.5,563,649.5,743,851.5,977,1113.5,1281]]
    samples=[]
    for i in range(RATE*2):
        t=i/RATE
        texture=sum(math.sin(TAU*f*t+p)/math.sqrt(f/100) for f,p in partials)/len(partials)
        s=.25*math.sin(TAU*96*t)+.045*math.sin(TAU*384*t)
        s+=texture*(.75+.12*math.cos(TAU*18*t))
        samples.append(s*(.96+.04*math.cos(TAU*2*t)))
    return samples


def ensure_meta(path, importer):
    meta=Path(str(path)+'.meta')
    if not meta.exists():
        meta.write_text(f'fileFormatVersion: 2\nguid: {base.guid(path)}\n{importer}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',encoding='utf-8')


def configure_visuals():
    shader=ROOT/'Assets/Resources/Shaders/ProgressKeyOverlay.shader'
    ensure_meta(shader,'ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []')
    material=ROOT/'Assets/Resources/Materials/ProgressKeyOverlay.mat'
    base.folders(material.parent)
    if not material.exists():
        material.write_text(f'''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: ProgressKeyOverlay
  m_Shader: {{fileID: 4800000, guid: {base.guid(shader)}, type: 3}}
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs: []
    m_Ints: []
    m_Floats: []
    m_Colors:
    - _Color: {{r: 1, g: 1, b: 1, a: 1}}
  m_BuildTextureStacks: []
''',encoding='utf-8')
    ensure_meta(material,'NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000')
    key=ROOT/'Assets/Prefabs/Interactables/ProgressKey.prefab'
    text=key.read_text(encoding='utf-8')
    # Replace only the mesh renderer material, not glow or particle slots.
    text=re.sub(r'(  m_Materials:\n)  - \{[^\n]*\}',
        lambda match:match.group(1)+f'  - {{fileID: 2100000, guid: {base.guid(material)}, type: 2}}',text,count=1)
    text=text.replace('  m_SortingOrder: 12','  m_SortingOrder: 30',1)
    key.write_text(text,encoding='utf-8')
    script=ROOT/'Assets/C# Scripts/Audio/SawAudioEmitter.cs'
    ensure_meta(script,'MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}')
    prefab=ROOT/'Assets/Prefabs/Terrain/Grid/GridSpikes.prefab'
    text=prefab.read_text(encoding='utf-8')
    if base.guid(script) not in text:
        text=text.replace('  - component: {fileID: 7639399720879440477}',
            '  - component: {fileID: 7639399720879440477}\n  - component: {fileID: 8361000000000000025}',1)
        text+=f'''--- !u!114 &8361000000000000025
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 8447194003576018874}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {base.guid(script)}, type: 3}}
  m_Name:
  m_EditorClassIdentifier:
  FullVolumeRadius: 1.5
  ListeningRadius: 6
  SourceCollider: {{fileID: 0}}
  MotorAnimation: {{fileID: 7639399720879440477}}
'''
        prefab.write_text(text,encoding='utf-8')


def preview(paths):
    samples,labels=[],[]
    for path in paths:
        with wave.open(str(path),'rb') as source:
            values=list(struct.unpack('<'+'h'*source.getnframes(),source.readframes(source.getnframes())))
        labels.append(f'{len(samples)/RATE:05.2f}s  {path.stem}')
        if path.stem=='SawLoop':
            values=[round(v*.3*min(1,i/(RATE*.1),(len(values)-1-i)/(RATE*.1))) for i,v in enumerate(values)]
        samples+=values+[0]*round(RATE*.4)
    dest=ROOT/'Design/Audio'
    dest.mkdir(parents=True,exist_ok=True)
    with wave.open(str(dest/'ReturnJourney-SFX-Preview.wav'),'wb') as output:
        output.setparams((1,2,RATE,0,'NONE','not compressed'))
        output.writeframes(struct.pack('<'+'h'*len(samples),*samples))
    (dest/'ReturnJourney-SFX-Preview.txt').write_text('\n'.join(labels)+'\n',encoding='utf-8')


def main():
    specifications=[('Power','PowerFlicker','flicker',3,.20),('Power','PowerDown','powerdown',1,1.05),
                    ('Platform','PlatformDetach','detach',2,.70),('Door','DoorUnlock','unlock',1,.55)]
    clips={}
    for folder,name,kind,count,duration in specifications:
        clips[name]=[]
        for variant in range(count):
            stem=f'{name}_{variant+1:02}'
            base.write_clip('ReturnJourney/'+folder,stem,effect(kind,duration,variant))
            clips[name].append(base.FILES[stem])
    base.write_clip('Loops','SawLoop',saw_loop(),True)
    clips['SawLoop']=[base.FILES['SawLoop']]
    catalog=ROOT/'Assets/Resources/Audio/GameplaySoundLibrary.asset'
    text=catalog.read_text(encoding='utf-8')
    for idx,(name,volume,priority) in enumerate([('PowerFlicker',.46,75),('PowerDown',.58,92),
        ('PlatformDetach',.55,85),('DoorUnlock',.52,95),('SawLoop',.08,30)],21):
        if re.search(rf'^  - Id: {idx}\s*$',text,re.M):continue
        loop=name=='SawLoop'
        text+=f'  - Id: {idx}\n    Clips:\n'+''.join(f'    - {{fileID: 8300000, guid: {base.guid(p)}, type: 3}}\n' for p in clips[name])+f'''    Volume: {volume}
    Pitch: {{x: 1, y: 1}}
    MinimumInterval: {0 if name=='PowerFlicker' else .12}
    MaxVoices: {4 if name=='PowerFlicker' else 2}
    Priority: {priority}
    Loop: {int(loop)}
    PauseWithGameplay: 1
    AllowDuringRewind: 0
    AllowDuringCinematic: {int(not loop)}
'''
    catalog.write_text(text,encoding='utf-8')
    configure_visuals()
    report=ROOT/'Logs/Audio/return-journey-synthesis-report.json'
    report.parent.mkdir(parents=True,exist_ok=True)
    report.write_text(json.dumps(base.REPORT,indent=2),encoding='utf-8')
    preview([clips[name][0] for name in clips])
    print('Generated 8 WAVs; appended cues 21-25; key overlay and animated saw prefab configured.')


if __name__=='__main__':main()
