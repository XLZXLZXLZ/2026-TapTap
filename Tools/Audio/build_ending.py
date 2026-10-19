"""Gently soften Ending.mp3 and author its complete bilingual VoiceSequence."""

import hashlib
import html
import json
import wave
from pathlib import Path

import numpy as np

from build_voice import ROOT, SR, decode, ensure_meta, fade, master, run_audio, write_audition, write_srt, write_wav
from configure_radio_sequences import write_asset

REPORT = ROOT / "Design/AudioVoice/Ending"
SOURCE = ROOT / "Assets/Audio/Voice/Ending.mp3"
FOLDER = ROOT / "Assets/Audio/Voice/Processed/Ending"
OUTPUT = FOLDER / "Ending_Soft.wav"
FILTERS = (
    "highpass=f=75,highshelf=f=2800:g=-2.5,"
    "equalizer=f=5600:t=q:w=1.2:g=-2,lowpass=f=7200,"
    "acompressor=threshold=0.08:ratio=1.6:attack=15:release=160:makeup=1"
)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    alignment = json.loads((REPORT / "subtitle_alignment.json").read_text(encoding="utf-8"))
    source_hash = sha(SOURCE)
    assert source_hash == alignment["source_sha256"], "Source changed; realign before rendering"
    library = json.loads((ROOT / "Design/AudioVoice/radio_sequence_catalog.json").read_text(encoding="utf-8"))
    before = {path: sha(ROOT / path) for path in library["master_sha256"]}
    raw = decode(SOURCE)
    softened = run_audio(raw, FILTERS)
    softened = 0.95 * softened + 0.05 * np.tanh(softened * 1.4) / 1.4
    softened = fade(softened, 0.008, 0.025)
    softened, levels = master(softened)
    assert len(softened) == len(raw), "Processing must preserve every pause and subtitle timestamp"
    assert np.isfinite(softened).all() and np.max(np.abs(softened)) < 0.86
    assert abs(levels["input_i"] + 19) < 0.4 and levels["input_tp"] <= -1.5
    FOLDER.mkdir(parents=True, exist_ok=True)
    ensure_meta(FOLDER, folder=True)
    write_wav(OUTPUT, softened)
    duration = len(softened) / SR
    cues = alignment["cues"]
    for i, cue in enumerate(cues):
        assert 0 <= cue["start"] < cue["end"] <= duration
        if i:
            assert cues[i - 1]["end"] <= cue["start"]
    segment = dict(file=OUTPUT.relative_to(ROOT).as_posix(), duration=duration,
                   gap_after=0, subtitles=cues)
    asset = write_asset("Ending", [segment])
    for language, key in [("zh-CN", "chinese"), ("en", "english")]:
        write_srt(REPORT / ("Ending." + language + ".srt"), [dict(
            start=c["start"], end=c["end"], text=c[key]) for c in cues])
    # A short native audio preview includes the bitter turn and the closing farewell.
    preview_start = max(0, next(c["start"] for c in cues if c["english"].startswith("I fought")) - 0.6)
    write_wav(REPORT / "Ending_Soft_Preview.wav", softened[round(preview_start * SR):], unity=False)
    with wave.open(str(OUTPUT), "rb") as handle:
        assert handle.getframerate() == SR and handle.getnchannels() == 1 and handle.getsampwidth() == 3
    assert sha(SOURCE) == source_hash
    assert all(sha(ROOT / path) == value for path, value in before.items())
    report = dict(source=SOURCE.relative_to(ROOT).as_posix(), source_sha256=source_hash,
                  output=OUTPUT.relative_to(ROOT).as_posix(), output_sha256=sha(OUTPUT),
                  sequence=asset["asset"], duration=duration, sample_rate=SR, channels=1,
                  encoding="PCM 24-bit", filters=FILTERS, target_lufs=-19,
                  integrated_lufs=levels["input_i"], true_peak_dbtp=levels["input_tp"],
                  lra=levels["input_lra"], pitch_and_timing_preserved=True,
                  original_and_other_21_masters_unchanged=True,
                  subtitle_count=len(cues), subtitles=cues)
    (REPORT / "ending_manifest.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    cue_json = json.dumps(cues, ensure_ascii=False).replace("<", "\\u003c")
    rows = "".join(f'<tr><td>{c["start"]:.2f}–{c["end"]:.2f}</td><td>{html.escape(c["chinese"])}</td><td>{html.escape(c["english"])}</td></tr>' for c in cues)
    page = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>最终独白 · 柔化试听</title>
<style>body{font:16px/1.7 system-ui;background:#101a20;color:#e6e6dd;max-width:1080px;margin:32px auto;padding:0 24px}audio{width:100%}h1{color:#b8d8c7}#caption{min-height:4em;background:#253137;padding:20px;border-radius:8px;font-size:22px}td{border-bottom:1px solid #37474b;padding:12px}td:first-child{white-space:nowrap;color:#a3b2b5}table{width:100%;border-collapse:collapse}</style>
<h1>最终独白 · 柔化试听</h1><p>轻收高频与齿音，温和压缩；原音高、语速、停顿及非语言表演保留。语音 SO：Ending.asset。</p>
<p>原录音</p><audio controls preload="none" src="../../../Assets/Audio/Voice/Ending.mp3"></audio>
<p>柔化成品 · 中文字幕同步预览</p><audio id="soft" controls preload="metadata" src="../../../Assets/Audio/Voice/Processed/Ending/Ending_Soft.wav"></audio>
<div id="caption"></div><h2>字幕时间轴</h2><table>''' + rows + '''</table><script>
const cues=''' + cue_json + ''';const audio=document.getElementById('soft');const caption=document.getElementById('caption');
audio.addEventListener('timeupdate',()=>{const c=cues.find(c=>audio.currentTime>=c.start&&audio.currentTime<c.end);caption.textContent=c?c.chinese:'';});
audio.addEventListener('ended',()=>caption.textContent='');</script></html>'''
    (REPORT / "Audition.html").write_text(page, encoding="utf-8")
    write_audition(json.loads((ROOT / "Design/AudioVoice/voice_manifest.json").read_text(encoding="utf-8")))
    print("Ending:", round(duration, 3), "seconds,", len(cues), "bilingual timeline cues", flush=True)
    print("Levels:", levels["input_i"], "LUFS;", levels["input_tp"], "dBTP", flush=True)
    print("SO:", asset["asset"], flush=True)
    print("PASS: encoding, duration, cue bounds, peak/loudness, source and existing audio preservation", flush=True)


if __name__ == "__main__":
    main()
