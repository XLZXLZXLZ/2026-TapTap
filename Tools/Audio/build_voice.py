"""Render the game's broadcast masters and power-cut edits without altering sources.

Requires numpy, scipy and imageio-ffmpeg. Word timing JSON comes from
transcribe_voice.py; reviewed edit decisions live in voice_edits.json.
"""

import argparse
import hashlib
import html
import json
import math
import re
import subprocess
import uuid
from pathlib import Path

import imageio_ffmpeg
import numpy as np
from scipy import signal

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/Audio/Voice"
OUTPUT = SOURCE / "Processed"
REPORT = ROOT / "Design/AudioVoice"
SR = 48000
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
TARGET_LUFS = -19.0
REVISION = 4
POWERCUT_PITCH_SEMITONES = -1.5
PROFILES = {
    "Verity": dict(hp=110, lp=4800, presence=0.5, cabinet=0.030, hiss=-53),
    "Sarah": dict(hp=175, lp=4000, presence=0.9, cabinet=0.048, hiss=-51),
    "Sheila": dict(hp=140, lp=4300, presence=0.2, cabinet=0.036, hiss=-51),
}


def run_audio(audio, filters, output_rate=SR):
    result = subprocess.run(
        [FFMPEG, "-v", "error", "-f", "f32le", "-ar", str(SR), "-ac", "1",
         "-i", "pipe:0", "-af", filters, "-ar", str(output_rate),
         "-f", "f32le", "pipe:1"],
        input=np.asarray(audio, dtype="<f4").tobytes(), capture_output=True, check=True,
    )
    return np.frombuffer(result.stdout, dtype="<f4").copy()


def decode(path):
    result = subprocess.run(
        [FFMPEG, "-v", "error", "-i", str(path), "-ac", "1", "-ar", str(SR),
         "-f", "f32le", "pipe:1"], capture_output=True, check=True,
    )
    return np.frombuffer(result.stdout, dtype="<f4").copy()


def measure(audio):
    result = subprocess.run(
        [FFMPEG, "-hide_banner", "-f", "f32le", "-ar", str(SR), "-ac", "1",
         "-i", "pipe:0", "-af",
         "apad=whole_dur=3,loudnorm=I=-19:TP=-1.5:LRA=8:print_format=json",
         "-f", "null", "-"],
        input=np.asarray(audio, dtype="<f4").tobytes(), capture_output=True, check=True,
    )
    data = json.loads(re.search(r'\{\s*"input_i".*?\}', result.stderr.decode(), re.S)[0])
    return {key: float(value) for key, value in data.items() if key != "normalization_type"}


def master(audio):
    info = measure(audio)
    if not math.isfinite(info["input_i"]):
        raise ValueError("Unable to measure speech loudness")
    gain = TARGET_LUFS - info["input_i"]
    result = run_audio(audio, f"volume={gain:.6f}dB,alimiter=limit=0.831764:attack=5:release=80:level=false:latency=true")
    # A second static correction accounts for the occasional limiter reduction.
    measured = measure(result)
    if abs(measured["input_i"] - TARGET_LUFS) > 0.35:
        correction = min(TARGET_LUFS - measured["input_i"], -1.6 - measured["input_tp"])
        result *= 10 ** (correction / 20)
        measured = measure(result)
    return result, measured


def fade(audio, fade_in=0.008, fade_out=0.012):
    audio = audio.copy()
    for seconds, reverse in [(fade_in, False), (fade_out, True)]:
        n = min(int(seconds * SR), len(audio) // 2)
        if n:
            ramp = np.sin(np.linspace(0, np.pi / 2, n)) ** 2
            if reverse:
                audio[-n:] *= ramp[::-1]
            else:
                audio[:n] *= ramp
    return audio


def noise(n, rng, rms_db=-51, low=650, high=6500):
    if n <= 0:
        return np.zeros(0, dtype=np.float32)
    samples = rng.standard_normal(n)
    samples = signal.sosfilt(signal.butter(2, [low, high], btype="bandpass", fs=SR, output="sos"), samples)
    samples *= 10 ** (rms_db / 20) / max(np.sqrt(np.mean(samples * samples)), 1e-9)
    return samples.astype(np.float32)


def fault(seconds, rng, kind=0, strength=1.0):
    """Sustained irregular electrical arcing and supply sag, without drum-like hits."""
    if seconds <= 0:
        return np.zeros(0, dtype=np.float32)
    n = max(1, round(seconds * SR))
    t = np.arange(n) / SR
    # Irregular modulation avoids a repeated beat or a single percussive transient.
    knots = np.linspace(0, seconds, max(5, round(seconds * 27)))
    movement = np.interp(t, knots, rng.uniform(0.40, 1.0, len(knots)))
    movement = signal.sosfilt(signal.butter(2, 42, fs=SR, output="sos"), movement)
    arc = noise(n, rng, -25.0, 420, 7800)
    carrier_phase = 2 * np.pi * ((1520 + kind * 110) * t + 45 * np.sin(2 * np.pi * 2.3 * t))
    arc *= movement * (0.78 + 0.22 * np.sin(carrier_phase))
    # Audible power-supply harmonics, with little sub-bass to avoid a thumping sound.
    mains = sum(np.sin(2 * np.pi * frequency * t + kind * 0.7) * amplitude
                for frequency, amplitude in [(100, 0.003), (200, 0.007),
                                             (300, 0.004), (400, 0.003), (600, 0.002)])
    mains *= 0.65 + 0.35 * movement
    # A falling electrical whine lasts through the interruption rather than clicking.
    f0, f1 = 1950 + kind * 130, 570 + kind * 60
    phase = 2 * np.pi * (f0 * t + 0.5 * (f1 - f0) * t * t / max(seconds, 0.01))
    whine = (np.sin(phase) + 0.16 * np.sin(phase * 2)) * 0.008
    whine *= np.exp(-t / max(seconds * 0.85, 0.1))
    envelope = 0.76 + 0.24 * np.exp(-t / 0.3)
    electrical = (arc + mains + whine) * envelope * strength
    result = electrical + noise(n, rng, -48)
    # Keep static from being heard as an added /s/ or /st/ on a retained word.
    # The sustained centre stays prominent; the word boundaries get breathing room.
    guard = min(round(0.045 * SR), n // 6)
    if guard:
        protected = np.zeros_like(result)
        protected[guard:-guard] = fade(
            result[guard:-guard], min(0.035, seconds * 0.18), min(0.075, seconds * 0.25))
        return protected
    return fade(result, min(0.032, seconds * 0.22), min(0.085, seconds * 0.30))


def powercut_colour(audio):
    """Lower speech pitch without slowing it or changing the subtitle timeline."""
    factor = 2 ** (POWERCUT_PITCH_SEMITONES / 12)
    filters = (
        f"apad=pad_dur=0.25,rubberband=tempo=1:pitch={factor:.9f}:"
        "transients=smooth:detector=soft:window=long:formant=shifted:pitchq=quality,"
        "lowpass=f=3350,highshelf=f=1850:g=-1.5,lowshelf=f=280:g=1.2"
    )
    result = run_audio(audio, filters)
    if len(result) < len(audio):
        result = np.pad(result, (0, len(audio) - len(result)))
    return result[:len(audio)]


def equipment(audio, profile):
    filters = (
        f"highpass=f={profile['hp']},lowpass=f={profile['lp']},"
        f"equalizer=f=2100:t=q:w=0.85:g={profile['presence']},"
        "highshelf=f=2700:g=-1.8,"
        "acompressor=threshold=0.095:ratio=2.2:attack=9:release=130:makeup=1.1"
    )
    audio = run_audio(audio, filters)
    audio = 0.85 * audio + 0.15 * np.tanh(audio * 2.5) / 2.5
    dry = audio.copy()
    for delay, amount in [(0.0037, profile["cabinet"]), (0.0081, profile["cabinet"] * 0.45)]:
        n = int(delay * SR)
        audio[n:] += dry[:-n] * amount
    return audio


def full_vintage_colour(audio, actor):
    """An extra muffled broadcast layer applies only to full ads and news."""
    cutoff = {"Verity": 3050, "Sheila": 2850}.get(actor)
    if cutoff is None:
        return audio
    return run_audio(audio, f"lowpass=f={cutoff}:p=2,highshelf=f=1950:g=-3.0,lowshelf=f=420:g=0.6")


def factory_echo(audio):
    """Quiet, damped early reflections, without a synthetic noise floor."""
    wet = run_audio(audio, "highpass=f=280,lowpass=f=2700")
    result = audio.copy()
    for seconds, gain in [(0.043, 0.10), (0.079, 0.065), (0.123, 0.040),
                          (0.177, 0.025), (0.237, 0.015)]:
        delay = round(seconds * SR)
        result[delay:] += wet[:-delay] * gain
    return fade(result, 0.006, 0.025)


def ensure_meta(path, folder=False):
    target = Path(str(path) + ".meta")
    if target.exists():
        return
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "taptap-voice/" + path.relative_to(ROOT).as_posix()).hex
    if folder:
        text = f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    else:
        text = f"""fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 6
  defaultSettings:
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 48000
    compressionFormat: 0
    quality: 1
    conversionMode: 0
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 0
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
    target.write_text(text, encoding="utf-8")


def write_wav(path, audio, unity=True):
    path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [FFMPEG, "-v", "error", "-y", "-f", "f32le", "-ar", str(SR), "-ac", "1",
         "-i", "pipe:0", "-c:a", "pcm_s24le", "-map_metadata", "-1", str(path)],
        input=np.asarray(audio, dtype="<f4").tobytes(), capture_output=True, check=True,
    )
    if unity:
        ensure_meta(path)


def excerpt(audio, words, first, last, start_adjust=0, end_adjust=0):
    start = max(0, words[first]["start"] - 0.025 + start_adjust)
    end = min(len(audio) / SR, words[last]["end"] + 0.04 + end_adjust)
    # Never reach into a discarded neighboring word to add padding.
    if first > 0:
        start = max(start, words[first - 1]["end"] + start_adjust)
    if last + 1 < len(words):
        end = min(end, words[last + 1]["start"] + end_adjust)
    if end <= start:
        raise ValueError("Empty edit range")
    return fade(audio[round(start * SR):round(end * SR)], 0.003, 0.005), start, end


def render_cut(base, words, spec, seed):
    rng = np.random.default_rng(seed)
    lead = fault(spec.get("lead", 0.25), rng, 0, 0.8)
    speech_parts = [np.zeros_like(lead)]
    effect_parts = [lead]
    cues, original_spans = [], []
    cursor = len(lead) / SR
    for i, span in enumerate(spec["spans"]):
        first, last = span["words"]
        clip, start, end = excerpt(base, words, first, last,
                                   span.get("start_adjust", 0), span.get("end_adjust", 0))
        # Only tiny modulation: retained dialogue must stay intelligible.
        t = np.arange(len(clip)) / SR
        clip *= 0.988 + 0.012 * np.sin(2 * np.pi * 1.7 * t + i)
        speech_parts.append(clip)
        effect_parts.append(np.zeros_like(clip))
        text = span["text"]
        if i > 0 and spec["spans"][i - 1].get("gap", 0.45) == 0:
            cues[-1]["end"] = round(cursor + len(clip) / SR, 4)
            cues[-1]["text"] += " " + text
        else:
            cues.append(dict(start=round(cursor, 4), end=round(cursor + len(clip) / SR, 4), text=text))
        original_spans.append(dict(start=round(start, 4), end=round(end, 4), words=[first, last], text=text))
        cursor += len(clip) / SR
        if i < len(spec["spans"]) - 1:
            gap = fault(span.get("gap", 0.45), rng, i + 1, spec.get("fault_strength", 1.0))
            speech_parts.append(np.zeros_like(gap))
            effect_parts.append(gap)
            cursor += len(gap) / SR
    tail = fault(spec.get("tail", 0.32), rng, 2, 0.7)
    speech_parts.append(np.zeros_like(tail))
    effect_parts.append(tail)
    return powercut_colour(np.concatenate(speech_parts)) + np.concatenate(effect_parts), cues, original_spans


def srt_timestamp(t):
    ms = round(t * 1000)
    return f"{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}"


def write_srt(path, cues):
    path.parent.mkdir(parents=True, exist_ok=True)
    text = "\n\n".join(f"{i + 1}\n{srt_timestamp(cue['start'])} --> {srt_timestamp(cue['end'])}\n{cue['text']}" for i, cue in enumerate(cues))
    path.write_text(text + "\n", encoding="utf-8")


def write_audition(manifest):
    ending = ('<h2>最终独白</h2><p><a style="color:#b8d8c7" href="Ending/Audition.html">'
              '打开柔化成品与中文字幕时间轴试听</a></p>') if (REPORT / "Ending/Audition.html").exists() else ""
    rows = []
    for entry in manifest["files"]:
        version = entry.get("revision", 2)
        url = "../../" + entry["file"] + f"?revision={version}"
        rows.append(f'<article><h2>{html.escape(entry["id"])}</h2><p>{html.escape(entry["transcript"])}</p><audio controls preload="none" src="{url}"></audio><small>{entry["duration"]:.2f}s · {entry["integrated_lufs"]:.1f} LUFS</small></article>')
    comparison = '<h2>工厂播报 · 调整前后对比</h2><p>工厂完整版去掉添加的底噪，略增短促、柔和的空间回音。广告、新闻和全部故障版保持原样。</p>'
    for label, path in [
        ("工厂 · 调整前", "Revisions/v3/Full/M04_Sarah_TransferForm_Full.wav"),
        ("工厂 · 去底噪、略增回音", "../../Assets/Audio/Voice/Processed/Full/M04_Sarah_TransferForm_Full.wav?revision=4"),
    ]:
        comparison += f'<p>{label}</p><audio controls preload="none" src="{path}"></audio>'
    page = '<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>广播试听 · 第 4 版</title><style>body{font:16px/1.7 system-ui;background:#101a20;color:#e6e6dd;max-width:900px;margin:40px auto;padding:0 24px}h1{color:#b8d8c7}h2{font-size:17px}article{border-top:1px solid #37474b;padding:16px 0}audio{width:100%}small{color:#a3b2b5}</style><h1>广播试听 · 第 4 版 · 21 个独立文件</h1>' + ending + comparison + '<h2>M07 五遍连续预览 · 沿用第 2 版</h2><audio controls preload="none" src="M07_FivePass_Preview.wav?revision=2"></audio>' + ''.join(rows) + '</html>'
    (REPORT / "Audition.html").write_text(page, encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", help="Rebuild a source ID such as M07, preserving other masters")
    parser.add_argument("--full-only", action="store_true", help="Preserve every power-cut file and its previews")
    parser.add_argument("--actors", nargs="+", choices=list(PROFILES), help="Actor profiles to rebuild; requires --full-only")
    args = parser.parse_args()
    if args.actors and not args.full_only:
        parser.error("--actors requires --full-only")
    plan = json.loads((Path(__file__).with_name("voice_edits.json")).read_text(encoding="utf-8"))
    REPORT.mkdir(parents=True, exist_ok=True)
    for folder in [OUTPUT, OUTPUT / "Full", OUTPUT / "PowerCut"]:
        folder.mkdir(parents=True, exist_ok=True)
        ensure_meta(folder, folder=True)
    manifest = dict(revision=REVISION, powercut_pitch_semitones=POWERCUT_PITCH_SEMITONES,
                    fault_model="sustained irregular electrical arc, mains harmonics and power sag",
                    sample_rate=SR, channels=1, encoding="PCM 24-bit", target_lufs=TARGET_LUFS,
                    language="en", m07_playback=["M07_PowerCut_01", "M07_PowerCut_02", "M07_PowerCut_03", "M07_PowerCut_04", "M07_PowerCut_04"], files=[])
    sources = sorted(p for p in SOURCE.glob("*.mp3") if re.match(r"^[MH]\d{2}_", p.stem))
    if args.only:
        sources = [p for p in sources if p.stem.split("_")[0] == args.only]
    if args.actors:
        sources = [p for p in sources if p.stem.split("_")[1] in args.actors]
    if not sources:
        parser.error("No matching sources")
    if args.only or args.full_only:
        previous = json.loads((REPORT / "voice_manifest.json").read_text(encoding="utf-8"))
        selected_sources = {p.name for p in sources}
        manifest["files"] = [e for e in previous["files"] if not (
            Path(e["source"]).name in selected_sources and
            (not args.full_only or e["id"].endswith("_Full")))]
    rendered = {}
    for source in sources:
        stem = source.stem
        key, actor = stem.split("_")[:2]
        timing_file = REPORT / "Alignment" / (stem + ".json")
        if not timing_file.exists():
            timing_file = REPORT / "Analysis" / (stem + ".json")
        analysis = json.loads(timing_file.read_text(encoding="utf-8"))
        if analysis["language"] != "en":
            raise ValueError(f"Non-English recording: {source}")
        words = analysis["words"]
        raw = decode(source)
        profile = PROFILES[actor]
        base = equipment(raw, profile)
        base, _ = master(base)
        rng = np.random.default_rng(int(hashlib.sha256(stem.encode()).hexdigest()[:8], 16))
        start = max(0, words[0]["start"] - 0.14)
        end = min(len(base) / SR, words[-1]["end"] + 0.22)
        full = fade(base[round(start * SR):round(end * SR)], 0.012, 0.035)
        if actor == "Sarah":
            full = factory_echo(full)
        else:
            full += fade(noise(len(full), rng, profile["hiss"]), 0.05, 0.09)
        full = full_vintage_colour(full, actor)
        full, full_info = master(full)
        full_name = stem + "_Full"
        write_wav(OUTPUT / "Full" / (full_name + ".wav"), full)
        # Sentence-level subtitles preserve the actual recorded wording.
        full_cues, sentence_start, text = [], max(0, words[0]["start"] - start), []
        for i, word in enumerate(words):
            text.append(word["text"])
            if re.search(r"[.!?]$", word["text"]) or i == len(words) - 1:
                full_cues.append(dict(start=round(sentence_start, 4), end=round(word["end"] - start, 4), text=" ".join(text)))
                text = []
                if i + 1 < len(words):
                    sentence_start = words[i + 1]["start"] - start
        if key in {"H01", "M04", "M06"}:
            write_wav(REPORT / f"{key}_Full_Preview_v{REVISION}.wav", full, unity=False)
        entry = dict(id=full_name, revision=4 if actor == "Sarah" else 3,
                     source=source.relative_to(ROOT).as_posix(),
                     source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                     file=(OUTPUT / "Full" / (full_name + ".wav")).relative_to(ROOT).as_posix(),
                     duration=round(len(full) / SR, 4), integrated_lufs=full_info["input_i"],
                     true_peak_dbtp=full_info["input_tp"], transcript=analysis["text"],
                     cues=full_cues, profile=actor)
        manifest["files"].append(entry)
        write_srt(REPORT / "Subtitles" / (full_name + ".srt"), full_cues)
        print(full_name, entry["duration"], entry["integrated_lufs"], flush=True)
        for j, spec in enumerate([] if args.full_only else plan.get(key, [])):
            name = spec["id"]
            cut, cues, spans = render_cut(base, words, spec, seed=int(key[1:]) * 100 + j)
            cut, info = master(cut)
            write_wav(OUTPUT / "PowerCut" / (name + ".wav"), cut)
            original_speech_duration = sum(w["end"] - w["start"] for w in words)
            kept_duration = sum(sum(words[k]["end"] - words[k]["start"] for k in range(s["words"][0], s["words"][1] + 1)) for s in spec["spans"])
            entry = dict(id=name, revision=2, source=source.relative_to(ROOT).as_posix(),
                         source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                         file=(OUTPUT / "PowerCut" / (name + ".wav")).relative_to(ROOT).as_posix(),
                         duration=round(len(cut) / SR, 4), integrated_lufs=info["input_i"],
                         true_peak_dbtp=info["input_tp"], transcript=spec.get("transcript", " … ".join(s["text"] for s in spec["spans"])),
                         removed_speech_ratio=round(1 - kept_duration / original_speech_duration, 4),
                         source_spans=spans, cues=cues, profile=actor)
            manifest["files"].append(entry)
            rendered[name] = cut
            write_srt(REPORT / "Subtitles" / (name + ".srt"), cues)
            print(name, entry["duration"], entry["integrated_lufs"], entry["removed_speech_ratio"], flush=True)
    manifest["files"].sort(key=lambda e: (e["id"][:3], not e["id"].endswith("_Full"), e["id"]))
    (REPORT / "voice_manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.full_only:
        write_audition(manifest)
        return
    montage_parts, montage_cues, cursor = [], [], 0.0
    entries = {e["id"]: e for e in manifest["files"]}
    for i, name in enumerate(manifest["m07_playback"]):
        if name not in rendered:
            rendered[name] = decode(ROOT / entries[name]["file"])
        if i:
            montage_parts.append(np.zeros(SR, dtype=np.float32))
            cursor += 1
        for cue in entries[name]["cues"]:
            montage_cues.append(dict(start=cursor + cue["start"], end=cursor + cue["end"], text=f"[Pass {i + 1}] {cue['text']}"))
        montage_parts.append(rendered[name])
        cursor += len(rendered[name]) / SR
    montage = np.concatenate(montage_parts)
    write_wav(REPORT / "M07_FivePass_Preview.wav", montage, unity=False)
    write_wav(REPORT / f"M07_FivePass_Preview_v{REVISION}.wav", montage, unity=False)
    m04 = rendered.get("M04_Sarah_TransferForm_PowerCut")
    if m04 is None:
        m04 = decode(ROOT / entries["M04_Sarah_TransferForm_PowerCut"]["file"])
    write_wav(REPORT / f"M04_PowerCut_Preview_v{REVISION}.wav", m04, unity=False)
    write_srt(REPORT / "M07_FivePass_Preview.srt", montage_cues)
    write_audition(manifest)


if __name__ == "__main__":
    main()
