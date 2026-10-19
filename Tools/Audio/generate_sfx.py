"""Deterministic original muted mechanical SFX. Python stdlib only; no downloaded samples.

Run from any directory. WAVs and their stable Unity GUIDs are preserved on regeneration.
The resource catalog is generated from this same manifest, including shared clip references.
"""
from pathlib import Path
import argparse
import json
import math
import random
import struct
import uuid
import wave

ROOT = Path(__file__).resolve().parents[2]
RATE = 44100
TAU = math.tau
RNG = random.Random(20261019)
FILES = {}
REPORT = []


def guid(path):
    meta = Path(str(path) + '.meta')
    if meta.exists():
        for line in meta.read_text(encoding='utf-8').splitlines():
            if line.startswith('guid: '):
                return line[6:]
    return uuid.uuid5(uuid.NAMESPACE_URL, 'taptap-sfx/' + path.relative_to(ROOT).as_posix()).hex


def folders(path):
    path.mkdir(parents=True, exist_ok=True)
    for folder in [path, *path.parents]:
        if folder == ROOT / 'Assets' or ROOT / 'Assets' not in folder.parents:
            break
        meta = Path(str(folder) + '.meta')
        if not meta.exists():
            meta.write_text(f'fileFormatVersion: 2\nguid: {guid(folder)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')


def soften(samples, loop=False):
    # Two gently rolled-off poles tame click/noise brightness. Warm up a full repeated
    # period for loops so the filter has the same state on either side of the seam.
    alpha = 1-math.exp(-TAU*1250/RATE)
    first = second = 0.0
    output = []
    for cycle in range(2 if loop else 1):
        for sample in samples:
            first += alpha*(sample-first)
            second += alpha*(first-second)
            if not loop or cycle == 1:
                output.append(second)
    return output


def write_clip(category, name, samples, loop=False):
    path = ROOT / 'Assets/Audio/SFX' / category / (name + '.wav')
    folders(path.parent)
    samples = soften(samples, loop)
    # Preserve detail at a restrained level, rather than normalizing every impact to a loud transient.
    mean = sum(samples) / len(samples)
    samples = [s - mean for s in samples]
    if not loop:
        attack = min(int(RATE*0.014), len(samples)//3)
        release = min(int(RATE*0.030), len(samples)//3)
        for i in range(attack):
            samples[i] *= math.sin(math.pi*.5*i/attack)**2
        for i in range(release):
            samples[-1-i] *= math.sin(math.pi*.5*i/release)**2
        residual = sum(samples) / len(samples)
        weights = [math.sin(math.pi*i/(len(samples)-1))**2 for i in range(len(samples))]
        correction = residual * len(samples) / sum(weights)
        samples = [s-correction*w for s,w in zip(samples,weights)]
    peak = max(abs(s) for s in samples)
    target = 0.42 if loop else 0.52 if name.startswith('ImpactHeavy') else 0.46
    samples = [s*(target/peak if peak else 1) for s in samples]
    with wave.open(str(path), 'wb') as output:
        output.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        output.writeframes(struct.pack('<' + 'h' * len(samples), *[round(s * 32767) for s in samples]))
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        meta.write_text(f'''fileFormatVersion: 2
guid: {guid(path)}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 0
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData: Original procedural synthesis; Tools/Audio/generate_sfx.py
  assetBundleName: 
  assetBundleVariant: 
''', encoding='utf-8')
    FILES[name] = path
    REPORT.append(dict(file=path.relative_to(ROOT).as_posix(), seconds=round(len(samples)/RATE, 4),
                       loop=loop, peak=round(max(abs(s) for s in samples), 5),
                       rms=round(math.sqrt(sum(s*s for s in samples)/len(samples)), 5),
                       seam_jump=round(abs(samples[-1]-samples[0]), 6)))


def transient(kind, variant=0, duration=0.35):
    samples = []
    tune = [0.985, 1.00, 1.015][variant % 3]
    phase = 0.0
    filtered_noise = 0.0
    for i in range(round(duration * RATE)):
        t = i / RATE
        noise = RNG.uniform(-1, 1)
        filtered_noise += 0.045 * (noise - filtered_noise)
        if kind == 'release':
            f = (190+65*math.exp(-t*22))*tune
            phase += TAU*f/RATE
            s = .5*math.sin(phase)*math.exp(-t*26)+.18*math.sin(phase*1.65)*math.exp(-t*38)
            s += filtered_noise*.65*math.exp(-t*18)
        elif kind == 'join':
            f = (180+30*(1-math.exp(-t*25)))*tune
            phase += TAU*f/RATE
            s = .85*math.sin(phase)*math.exp(-t*27)+.2*math.sin(TAU*355*t*tune)*math.exp(-t*38)
            s += filtered_noise*.4*math.exp(-t*30)
        elif kind in ['light', 'heavy']:
            f = (220 if kind == 'light' else 95)*tune
            s = sum(math.sin(TAU*f*m*t)*a*math.exp(-t*d) for m,a,d in [(1,1,25),(1.6,.2,45),(2.8,.055,65)])
            s += filtered_noise*.45*math.exp(-t*35)
        elif kind == 'bounce':
            # A damped, cushioned recoil: no cartoon boing, vibrato or broad pitch slide.
            f = (145+35*math.exp(-t*24))*tune
            phase += TAU*f/RATE
            s = math.sin(phase)*math.exp(-t*18)+filtered_noise*.38*math.exp(-t*26)
        elif kind == 'fail':
            s = .55*math.sin(TAU*175*t*tune)*math.exp(-t*26)+filtered_noise*.3*math.exp(-t*24)
            if t >= .08:
                u = t-.08
                s += .28*math.sin(TAU*157*u*tune)*(1-math.exp(-u*180))*math.exp(-u*30)
        elif kind == 'switch':
            s = math.sin(TAU*260*t*tune)*math.exp(-t*48)+filtered_noise*.8*math.exp(-t*60)
            if t > .035:
                s += .22*math.sin(TAU*170*(t-.035))*math.exp(-(t-.035)*55)
        elif kind == 'phase':
            f = 140+35*t/duration
            phase += TAU*f/RATE
            s = (.3*math.sin(phase)+.08*math.sin(phase*2)+filtered_noise*.5)*math.sin(math.pi*t/duration)**2
        elif kind == 'piston':
            f = 130+35*t/duration
            phase += TAU*f/RATE
            s = (.6*filtered_noise+.25*math.sin(phase))*math.sin(math.pi*t/duration)
            if t > duration-.045:
                u=t-duration+.045
                s += .6*math.sin(TAU*140*u)*math.exp(-u*65)
        elif kind == 'death':
            f = (110+35*math.exp(-t*12))*tune
            phase += TAU*f/RATE
            s = (.45*math.sin(phase)+filtered_noise*.7)*math.exp(-t*8)*(.9+.1*math.sin(TAU*6*t))
        elif kind == 'respawn':
            # One subdued assembly bloom instead of a musical, ascending reward arpeggio.
            s = (.7*math.sin(TAU*196*t)+.12*math.sin(TAU*392*t)+filtered_noise*.2)
            s *= (1-math.exp(-t*45))*math.exp(-t*10)
        else:
            rising = kind in ['rewind_end', 'bullet_off']
            f = 130+20*(t/duration if rising else 1-t/duration)
            phase += TAU*f/RATE
            s = (math.sin(phase)*.22+filtered_noise*.8)*math.sin(math.pi*t/duration)**1.5
        samples.append(s)
    return samples


def loop_samples(kind, duration=2):
    samples = []
    texture = [(f, RNG.uniform(0,TAU)) for f in [93.5,127.5,173.5,213.5,278,337,421.5,509,641.5]]
    for i in range(RATE*duration):
        t = i/RATE
        air = sum(math.sin(TAU*f*t+phase)/math.sqrt(f/90) for f,phase in texture)/len(texture)
        if kind == 'magnet':
            s = (.32*math.sin(TAU*120*t)+.09*math.sin(TAU*240*t)+.02*math.sin(TAU*360*t)+.14*air)
            s *= .96+.04*math.cos(TAU*.5*t)
        elif kind == 'rewind':
            s = .32*math.sin(TAU*96*t)+.1*math.sin(TAU*192*t)+.24*air
            s *= .92+.08*math.cos(TAU*4*t)
        else:
            s = .4*math.sin(TAU*64*t)+.08*math.sin(TAU*128*t)+.25*air
            s += .045*math.sin(TAU*256*t)*(.5+.5*math.cos(TAU*4*t))**2
            s *= .96+.04*math.cos(TAU*2*t)
        samples.append(s)
    return samples


def write_preview():
    # Keep the original preview as an A/B reference; this path is the current softer edition.
    paths = [FILES[name] for name in [
        'MagnetRelease_01','MagnetJoin_01','ImpactLight_01','ImpactHeavy_01','Bounce_01',
        'RecallFailed_01','Death_01','Respawn_01','SwitchPress_01','PhaseChange_01','PistonMove_01',
        'RewindStart','RewindEnd','BulletTimeOn','BulletTimeOff','MagnetHum','RewindLoop','ConveyorLoop']]
    samples, labels = [], []
    for path in paths:
        with wave.open(str(path), 'rb') as reader:
            values = list(struct.unpack('<'+'h'*reader.getnframes(),reader.readframes(reader.getnframes())))
        labels.append(f'{len(samples)/RATE:05.2f}s  {path.stem}')
        if path.parent.name == 'Loops':
            for i in range(len(values)):
                values[i] = round(values[i]*.35*min(1,i/(RATE*.1),(len(values)-1-i)/(RATE*.1)))
        samples.extend(values)
        samples.extend([0]*round(RATE*.3))
    dest = ROOT/'Design/Audio'
    dest.mkdir(parents=True,exist_ok=True)
    with wave.open(str(dest/'SFX-Preview-Soft.wav'),'wb') as output:
        output.setparams((1,2,RATE,0,'NONE','not compressed'))
        output.writeframes(struct.pack('<'+'h'*len(samples),*samples))
    (dest/'SFX-Preview-Soft.txt').write_text('\n'.join(labels)+'\n',encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reset-library', action='store_true', help='Reset the editable sound catalog to generated defaults')
    options = parser.parse_args()
    families = [
        ('Character/Magnet','MagnetRelease','release',3,.22),
        ('Character/Magnet','MagnetJoin','join',3,.24),
        ('Character/Impact','ImpactLight','light',3,.24),
        ('Character/Impact','ImpactHeavy','heavy',3,.34),
        ('Character/Bounce','Bounce','bounce',3,.42),
        ('Character/Feedback','RecallFailed','fail',2,.22),
        ('Mechanisms','SwitchPress','switch',2,.16),
        ('Mechanisms','PhaseChange','phase',1,.27),
        ('Mechanisms','PistonMove','piston',2,.22),
        ('Character/Respawn','Death','death',2,.52),
        ('Character/Respawn','Respawn','respawn',1,.55),
    ]
    clips = {}
    for category,name,kind,count,duration in families:
        clips[name] = []
        for v in range(count):
            stem = f'{name}_{v+1:02}'
            write_clip(category,stem,transient(kind,v,duration))
            clips[name].append(stem)
    for name,kind in [('RewindStart','rewind_start'),('RewindEnd','rewind_end'),('BulletTimeOn','bullet_on'),('BulletTimeOff','bullet_off')]:
        write_clip('Time',name,transient(kind,duration=.28 if 'Rewind' in name else .20))
        clips[name]=[name]
    for name,kind in [('MagnetHum','magnet'),('RewindLoop','rewind'),('ConveyorLoop','conveyor')]:
        write_clip('Loops',name,loop_samples(kind),True)
        clips[name]=[name]
    entries = []
    names = list(clips)[:11]+['RewindStart','RewindEnd','BulletTimeOn','BulletTimeOff','Click','KeyPickup','Complete','MagnetHum','RewindLoop','ConveyorLoop']
    gains = [.45,.62,.38,.55,.54,.35,.5,.32,.34,.6,.48,.42,.38,.32,.32,.7,.62,.48,.12,.18,.10]
    for idx,name in enumerate(names):
        loop = name in ['MagnetHum','RewindLoop','ConveyorLoop']
        source = 'MagnetJoin' if name=='KeyPickup' else 'Respawn' if name=='Complete' else name
        paths = [ROOT/'Assets/Audio/SoftClick.wav'] if name=='Click' else [FILES[c] for c in clips[source]]
        # UI, time edges and completion survive pause; ordinary gameplay is suppressible.
        pause = name not in ['RewindStart','RewindEnd','BulletTimeOn','BulletTimeOff','Click','KeyPickup','Complete']
        priority = 95 if name in ['Death','MagnetJoin','KeyPickup','Respawn'] else 70 if name in ['RecallFailed','RewindStart','RewindEnd'] else 40
        entries.append(f'  - Id: {idx}\n    Clips:\n'+''.join(f'    - {{fileID: 8300000, guid: {guid(p)}, type: 3}}\n' for p in paths)+f'''    Volume: {gains[idx]}
    Pitch: {{x: {1 if loop else .97}, y: {1 if loop else 1.03}}}
    MinimumInterval: {0.12 if name in ['Death','Respawn','Bounce','RecallFailed','PistonMove'] else 0.04}
    MaxVoices: {2 if name=='PistonMove' else 3}
    Priority: {priority}
    Loop: {int(loop)}
    PauseWithGameplay: {int(pause)}
    AllowDuringRewind: {int(name in ['RewindStart','RewindEnd','RewindLoop'])}
''')
    script = ROOT/'Assets/C# Scripts/Audio/SoundLibrary.cs'
    path = ROOT/'Assets/Resources/Audio/GameplaySoundLibrary.asset'
    folders(path.parent)
    catalog_text = f'''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {guid(script)}, type: 3}}
  m_Name: GameplaySoundLibrary
  m_EditorClassIdentifier: 
  Sounds:
'''+''.join(entries)
    if not path.exists() or options.reset_library:
        path.write_text(catalog_text,encoding='utf-8')
    for p in [script,ROOT/'Assets/C# Scripts/Audio/AudioManager.cs',ROOT/'Assets/C# Scripts/Audio/PlayerAudioFeedback.cs']:
        meta = Path(str(p)+'.meta')
        if not meta.exists():
            meta.write_text(f'fileFormatVersion: 2\nguid: {guid(p)}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',encoding='utf-8')
    meta = Path(str(path)+'.meta')
    if not meta.exists():
        meta.write_text(f'fileFormatVersion: 2\nguid: {guid(path)}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',encoding='utf-8')
    report = ROOT/'Logs/Audio/synthesis-report.json'
    report.parent.mkdir(parents=True,exist_ok=True)
    report.write_text(json.dumps(REPORT,indent=2),encoding='utf-8')
    write_preview()
    print(f'Generated {len(REPORT)} WAVs, {len(entries)} configured cues; report: {report}')


if __name__ == '__main__':
    main()
