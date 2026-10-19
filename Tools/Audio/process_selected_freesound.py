"""Prepare selected mechanical cues; lock release variants are audition-only.

Sources are cached outside Assets. Freesound's public HQ MP3 previews are used;
the original WAV downloads require a signed-in account. Requires requirements.txt.
"""
import hashlib
import json
from pathlib import Path
import re
import subprocess
import urllib.request
import wave

import imageio_ffmpeg
import numpy as np
from scipy.signal import butter, resample_poly, sosfilt

import generate_sfx as base

ROOT = base.ROOT
RATE = 44100
CACHE = ROOT / 'Tools/Audio/Sources/Freesound'
SOURCES = [
    dict(id=321482, title='Metal Hit 2', author='dslrguide',
         page='https://freesound.org/people/dslrguide/sounds/321482/',
         url='https://cdn.freesound.org/previews/321/321482_5485024-hq.mp3',
         file='321482__dslrguide__metal-hit-2-hq.mp3'),
    dict(id=403252, title='Taser/High Voltage discharge', author='The_Chemical_Workshop',
         page='https://freesound.org/people/The_Chemical_Workshop/sounds/403252/',
         url='https://cdn.freesound.org/previews/403/403252_7809148-hq.mp3',
         file='403252__the_chemical_workshop__high-voltage-discharge-hq.mp3'),
    dict(id=405534, title='Door Lock.wav', author='nebulasnails',
         page='https://freesound.org/people/nebulasnails/sounds/405534/',
         url='https://cdn.freesound.org/previews/405/405534_2723982-hq.mp3',
         file='405534__nebulasnails__door-lock-hq.mp3'),
    dict(id=160215, title='Unlocking Door Lock', author='qubodup',
         page='https://freesound.org/people/qubodup/sounds/160215/',
         url='https://cdn.freesound.org/previews/160/160215_71257-hq.mp3',
         file='160215__qubodup__unlocking-door-lock-hq.mp3'),
]


def read_source(source):
    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / source['file']
    if not path.exists():
        with urllib.request.urlopen(source['url'], timeout=45) as response:
            data = response.read()
            if 'audio' not in response.headers.get('Content-Type', ''):
                raise ValueError('Expected audio from ' + source['url'])
        path.write_bytes(data)
    source.update(license='CC0 1.0',
                  license_url='https://creativecommons.org/publicdomain/zero/1.0/',
                  source_format='Public HQ MP3 preview; original WAV requires login',
                  sha256=hashlib.sha256(path.read_bytes()).hexdigest())
    decoded = subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-v', 'error',
        '-i', str(path), '-ar', str(RATE), '-ac', '1', '-f', 'f32le', '-'],
        check=True, capture_output=True).stdout
    return np.frombuffer(decoded, dtype='<f4').astype(np.float64)


def filter_audio(samples, highpass, lowpass):
    samples = samples - samples.mean()
    samples = sosfilt(butter(2, highpass, 'highpass', fs=RATE, output='sos'), samples)
    return sosfilt(butter(3, lowpass, 'lowpass', fs=RATE, output='sos'), samples)


def excerpt_at_attack(samples, duration):
    """Select a compact mechanical attack rather than dragging keys or room noise."""
    energy = np.convolve(samples ** 2, np.ones(round(.006 * RATE)), 'same')
    # Start at the strongest attack, with enough lead-in for a clean fade.
    peak = int(np.argmax(energy))
    start = max(0, peak - round(.012 * RATE))
    count = round(duration * RATE)
    return np.pad(samples[start:start + count], (0, max(0, start + count - len(samples)))), start


def short_unlock(samples):
    # Two compact real-mechanism clicks replace the long key/door recording.
    # A quieter, lower response gives a clear release without a bright UI chime.
    click, start = excerpt_at_attack(samples, .14)
    click = filter_audio(click, 120, 2600)
    click = finish(click, .34, .003, .035)
    response = resample_poly(click, 10, 9)
    output = np.zeros(round(.25 * RATE))
    output[:len(click)] += click
    offset = round(.072 * RATE)
    output[offset:offset + len(response)] += response * .55
    output = filter_audio(output, 100, 2600)
    return finish(output, .40, .003, .04), start


def finish(samples, peak, attack, release):
    samples = samples.copy()
    for count, tail in [(round(attack * RATE), False), (round(release * RATE), True)]:
        count = min(count, len(samples) // 2)
        ramp = np.sin(np.linspace(0, np.pi / 2, count)) ** 2
        if tail:
            samples[-count:] *= ramp[::-1]
        else:
            samples[:count] *= ramp
    samples *= peak / max(np.max(np.abs(samples)), 1e-9)
    return samples


def write_asset(relative, samples, source):
    path = ROOT / 'Assets/Audio/SFX/ReturnJourney' / relative
    base.folders(path.parent)
    write_wav(path, samples)
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        # Reuse the established PCM import settings, with a unique stable GUID.
        template = (ROOT / 'Assets/Audio/SFX/ReturnJourney/Power/PowerFlicker_01.wav.meta').read_text('utf-8')
        template = re.sub(r'^guid: .*$', 'guid: ' + base.guid(path), template, flags=re.M)
        template = re.sub(r'^  userData: .*$',
            f"  userData: CC0 Freesound {source['id']}; Tools/Audio/process_selected_freesound.py", template, flags=re.M)
        meta.write_text(template, encoding='utf-8')
    return path


def write_wav(path, samples):
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as output:
        output.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        output.writeframes(np.round(samples * 32767).astype('<i2').tobytes())


def set_cue(catalog, cue_id, path):
    pattern = rf'(^  - Id: {cue_id}\n    Clips:\n)(?:    - [^\n]+\n)+'
    replacement = rf'\g<1>    - {{fileID: 8300000, guid: {base.guid(path)}, type: 3}}\n'
    catalog, count = re.subn(pattern, replacement, catalog, flags=re.M)
    if count != 1:
        raise ValueError(f'Expected one catalog entry for cue {cue_id}, found {count}')
    return catalog


def main():
    metal, discharge, lock, game_lock = [read_source(source) for source in SOURCES]
    # Find the impact onset, preserving a 3 ms lead-in and the original transient.
    onset = max(0, int(np.flatnonzero(np.abs(metal) > np.max(np.abs(metal)) * .10)[0]) - round(.003 * RATE))
    impact = resample_poly(metal[onset:], 10, 9)  # 0.9x playback speed; modestly lower pitch.
    impact = filter_audio(impact, 45, 2100)[:round(.90 * RATE)]
    impact = finish(impact, .58, .003, .12)
    # Take one energetic 160 ms excerpt, avoiding the recording's empty lead-in.
    length = round(.16 * RATE)
    energies = np.convolve(discharge ** 2, np.ones(round(.01 * RATE)) / round(.01 * RATE), 'same')
    peak = int(np.argmax(energies))
    start = max(0, min(peak - round(.03 * RATE), len(discharge) - length))
    flicker = filter_audio(discharge[start:start + length], 120, 1600)
    flicker = finish(flicker, .28, .008, .03)
    unlock, lock_start = short_unlock(lock)
    alternate, alternate_start = excerpt_at_attack(game_lock, .24)
    alternate = filter_audio(alternate, 120, 2400)
    alternate = finish(alternate, .40, .003, .04)
    platform = write_asset('Platform/PlatformBreak_MetalHit2.wav', impact, SOURCES[0])
    power = write_asset('Power/PowerFlicker_Discharge.wav', flicker, SOURCES[1])
    catalog_path = ROOT / 'Assets/Resources/Audio/GameplaySoundLibrary.asset'
    catalog = catalog_path.read_text('utf-8')
    catalog = set_cue(catalog, 23, platform)
    catalog = set_cue(catalog, 21, power)
    catalog_path.write_text(catalog, encoding='utf-8')
    SOURCES[0]['edit'] = dict(onset_seconds=onset/RATE, speed=.9, highpass_hz=45,
        lowpass_hz=2100, lowpass_order=3, output_seconds=len(impact)/RATE, peak=.58, fade_in_ms=3, fade_out_ms=120)
    SOURCES[1]['edit'] = dict(start_seconds=start/RATE, output_seconds=len(flicker)/RATE,
        highpass_hz=120, lowpass_hz=1600, lowpass_order=3, peak=.28, fade_in_ms=8, fade_out_ms=30)
    SOURCES[2]['edit'] = dict(start_seconds=lock_start/RATE, output_seconds=len(unlock)/RATE,
        highpass_hz=120, lowpass_hz=2600, lowpass_order=3, peak=.40, fade_in_ms=3,
        fade_out_ms=40, secondary_click_delay_ms=72, secondary_click_gain=.55, secondary_speed=.9)
    SOURCES[3]['edit'] = dict(start_seconds=alternate_start/RATE, output_seconds=len(alternate)/RATE,
        highpass_hz=120, lowpass_hz=2400, lowpass_order=3, peak=.40, fade_in_ms=3, fade_out_ms=40)
    SOURCES[3].update(output='Design/Audio/DoorUnlock-Short-Alternative-Preview.wav', usage='Audition alternative only')
    SOURCES[2].update(output='Design/Audio/DoorUnlock-Crisp-Preview.wav', usage='Audition only; not selected by user')
    for source, path, cue_id in zip(SOURCES, [platform, power], [23, 21]):
        source.update(output=path.relative_to(ROOT).as_posix(), cue_id=cue_id)
    (CACHE / 'selected_sources.json').write_text(json.dumps(SOURCES, indent=2) + '\n', encoding='utf-8')
    preview = ROOT / 'Design/Audio'
    # Preview files include the catalog gain, at full SFX/master volume.
    write_wav(preview / 'PlatformBreak-MetalHit2-Preview.wav', impact * .55)
    write_wav(preview / 'PowerFlicker-Discharge-Preview.wav', flicker * .46)
    write_wav(preview / 'DoorUnlock-Crisp-Preview.wav', unlock * .52)
    write_wav(preview / 'DoorUnlock-Short-Alternative-Preview.wav', alternate * .52)
    sequence = np.concatenate([flicker*.46, np.zeros(round(.32*RATE)), flicker*.46,
        np.zeros(round(.65*RATE)), impact*.55])
    write_wav(preview / 'Selected-ReturnJourney-SFX-Preview.wav', sequence)
    write_wav(preview / 'DoorUnlock-Comparison-Preview.wav', np.concatenate([
        unlock*.52, np.zeros(round(.6*RATE)), alternate*.52]))
    print(json.dumps(SOURCES, indent=2))


if __name__ == '__main__':
    main()
