"""Align the approved bilingual ending against local speech recognition and CTC."""

import difflib
import json
import re
from pathlib import Path

import numpy as np
import torch
import torchaudio
from scipy import signal

from build_voice import ROOT, decode

REPORT = ROOT / "Design/AudioVoice/Ending"


def tokens(text):
    return re.findall(r"[a-z]+(?:'[a-z]+)?", text.lower().replace("’", "'"))


def main():
    source = ROOT / "Assets/Audio/Voice/Ending.mp3"
    script = (ROOT / "Assets/Docs/GameDesign/最终独白.md").read_text(encoding="utf-8")
    parallel = script.split("## 逐句中英对照", 1)[1].split("## 英文完整稿", 1)[0]
    pairs = re.findall(r"\*\*(.*?)\*\*\s*\n([^\n]+)", parallel)
    original = json.loads((REPORT / "Analysis/Ending.json").read_text(encoding="utf-8"))
    # Whole-file Whisper missed a phrase and hallucinated two boundary words.
    # Independent short-excerpt recognition recovered the complete approved sentence.
    words = [w for w in original["words"] if not (
        (50 < w["start"] < 51 and tokens(w["text"]) == ["thoughts"])
        or (151 < w["start"] < 152 and tokens(w["text"]) == ["summer"]))]
    burial = json.loads((REPORT / "Review/Transcripts/Burial.json").read_text(encoding="utf-8"))
    replacement = [dict(w, start=w["start"] + 98, end=w["end"] + 98)
                   for w in burial["words"] if tokens(w["text"]) != ["closed"]]
    assert tokens(" ".join(w["text"] for w in replacement)) == tokens(
        "It was buried underground, along with the last batch of unfinished toys and the ones we'd marked as defective.")
    words = [w for w in words if w["end"] <= 99.3 or w["start"] >= 108]
    words = sorted(words + replacement, key=lambda w: w["start"])
    flattened = [(token, w) for w in words for token in tokens(w["text"])]
    expected = [token for english, _ in pairs for token in tokens(english) if token != "ha"]
    actual = [token for token, _ in flattened]
    if expected != actual:
        differences = [(op, expected[a:b], actual[c:d]) for op, a, b, c, d in
                       difflib.SequenceMatcher(None, expected, actual, autojunk=False).get_opcodes() if op != "equal"]
        raise ValueError(f"Unreviewed spoken-text difference: {differences}")
    samples = signal.resample_poly(decode(source), 1, 3).astype(np.float32)
    torch.set_num_threads(4)
    torch.hub.set_dir(str(Path.home() / ".codex/tmp/taptap-audio-models/torch"))
    bundle = torchaudio.pipelines.WAV2VEC2_ASR_BASE_960H
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print("Loading cached CTC model:", device, flush=True)
    model = bundle.get_model().to(device).eval()
    dictionary = {char: i for i, char in enumerate(bundle.get_labels())}
    cursor, cues = 0, []
    for english, chinese in pairs:
        target_words = [word for word in tokens(english) if word != "ha"]
        anchors = flattened[cursor:cursor + len(target_words)]
        cursor += len(target_words)
        begin = max(0, anchors[0][1]["start"] - (0.7 if english.startswith("Ha.") else 0.35))
        finish = min(len(samples) / 16000, anchors[-1][1]["end"] + 0.4)
        crop = samples[round(begin * 16000):round(finish * 16000)]
        letters = "|".join(word.upper() for word in target_words)
        target = [dictionary[c] for c in letters]
        with torch.inference_mode():
            emission, _ = model(torch.from_numpy(crop).unsqueeze(0).to(device))
            emission = emission.log_softmax(-1).cpu()
        alignment, scores = torchaudio.functional.forced_align(
            emission, torch.tensor([target], dtype=torch.int64), blank=0)
        spans = torchaudio.functional.merge_tokens(alignment[0], scores[0].exp())
        assert [s.token for s in spans] == target
        ratio = len(crop) / 16000 / emission.shape[1]
        start = max(0, begin + spans[0].start * ratio - 0.045)
        end = min(len(samples) / 16000 - 0.01, begin + spans[-1].end * ratio + 0.18)
        if english.startswith("Ha."):
            start = max(begin, start - 0.45)
        cues.append(dict(start=round(start, 4), end=round(end, 4), chinese=chinese.strip(),
                         english=english.lstrip("…"), ctc_score=round(float(np.mean([s.score for s in spans])), 5)))
        print(round(start, 2), round(end, 2), english, flush=True)
    for i, cue in enumerate(cues):
        if i + 1 < len(cues):
            cue["end"] = min(cue["end"], round(cues[i + 1]["start"] - 0.025, 4))
        assert 0 <= cue["start"] < cue["end"]
    (REPORT / "subtitle_alignment.json").write_text(json.dumps(dict(
        source_sha256=original["file_sha256"], transcript_verified=True,
        method="Local Whisper plus independent excerpt review and per-sentence Wav2Vec2 CTC",
        notes="Ha is a nonverbal chuckle; its caption uses the recorded laugh and following sentence.",
        cues=cues), ensure_ascii=False, indent=2), encoding="utf-8")
    print("Aligned", len(cues), "bilingual cues", flush=True)


if __name__ == "__main__":
    main()
