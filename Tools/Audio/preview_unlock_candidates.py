"""Audition-only lock candidates. Never writes Assets or the gameplay catalog."""
import json
from pathlib import Path

import numpy as np
from scipy.signal import resample_poly

import process_selected_freesound as audio


ROOT = audio.ROOT
DEST = ROOT / 'Design/Audio/Unlock-Candidates-R2'
SOURCES = [
    dict(label='N1', id=536414, title='Small Padlock Opening', author='Rudmer_Rotteveel',
         page='https://freesound.org/people/Rudmer_Rotteveel/sounds/536414/',
         url='https://cdn.freesound.org/previews/536/536414_4921277-hq.mp3',
         file='536414__small-padlock-opening-hq.mp3', duration=.28, lowpass=2600),
    dict(label='N2', id='oga-gun-reload-lock-click', title='Gun reload, lock or click sound', author='pauliuw',
         page='https://opengameart.org/content/gun-reload-lock-or-click-sound',
         url='https://opengameart.org/sites/default/files/gun_reload_lock_or_click_sound.mp3',
         file='pauliuw__gun_reload_lock_or_click_sound.mp3', duration=.32, lowpass=2300),
    dict(label='N3', id=405359, title='aekanmaz_unlock.wav', author='aekanmaz',
         page='https://freesound.org/people/aekanmaz/sounds/405359/',
         url='https://cdn.freesound.org/previews/405/405359_3487548-hq.mp3',
         file='405359__aekanmaz_unlock-hq.mp3', duration=.30, lowpass=2500),
]


def main():
    audio.CACHE = ROOT / 'Tools/Audio/Sources/UnlockCandidates-R2'
    DEST.mkdir(parents=True, exist_ok=True)
    sequence, labels = [], []
    timeline = 0.
    for source in SOURCES:
        raw = audio.read_source(source)
        if isinstance(source['id'], str):
            source['source_format'] = 'Original MP3 from OpenGameArt download'
        # Preserve each recording's own rhythm; tame brightness without layering old candidates.
        filtered = audio.filter_audio(raw, 100, source['lowpass'])
        onset = max(0, int(np.flatnonzero(abs(filtered) > max(abs(filtered)) * .08)[0]) - round(.003 * audio.RATE))
        samples = filtered[onset:]
        if source['label'] == 'N2':
            samples = resample_poly(samples, 10, 9)
        samples = samples[:round(source['duration'] * audio.RATE)]
        samples = audio.finish(samples, .34, .003, .03) * .52
        path = DEST / (source['label'] + '-Soft.wav')
        audio.write_wav(path, samples)
        assert np.isfinite(samples).all() and max(abs(samples)) < .99
        assert samples[0] == samples[-1] == 0
        source.update(output=path.relative_to(ROOT).as_posix(), usage='Audition only; user selection pending',
                      edit=dict(start_seconds=onset/audio.RATE, duration_seconds=len(samples)/audio.RATE,
                                highpass_hz=100, lowpass_hz=source['lowpass'], lowpass_order=3,
                                peak_before_preview_gain=.34, preview_gain=.52,
                                speed=.9 if source['label']=='N2' else 1., fade_in_ms=3, fade_out_ms=30))
        labels.append(f'{timeline:.2f}s  {source["label"]}  {source["title"]}')
        sequence += [samples, np.zeros(round(.65 * audio.RATE))]
        timeline += len(samples)/audio.RATE + .65
    audio.write_wav(DEST / 'N1-N2-N3-Comparison.wav', np.concatenate(sequence))
    (DEST / 'timings.txt').write_text('\n'.join(labels)+'\n', encoding='utf-8')
    (audio.CACHE / 'sources.json').write_text(json.dumps(SOURCES, indent=2)+'\n', encoding='utf-8')
    print('\n'.join(labels))


if __name__ == '__main__':
    main()
