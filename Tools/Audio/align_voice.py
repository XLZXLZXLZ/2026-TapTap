"""Refine local Whisper timings with independent English CTC alignment."""

import json
import re
import subprocess
from pathlib import Path

import imageio_ffmpeg
import numpy as np
import torch
import torchaudio

ROOT = Path(__file__).resolve().parents[2]
REPORT = ROOT / "Design/AudioVoice"


def main():
    torch.set_num_threads(4)
    cache = Path.home() / ".codex/tmp/taptap-audio-models/torch"
    cache.mkdir(parents=True, exist_ok=True)
    torch.hub.set_dir(str(cache))
    bundle = torchaudio.pipelines.WAV2VEC2_ASR_BASE_960H
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print("Loading CTC alignment model", device, flush=True)
    model = bundle.get_model().to(device).eval()
    dictionary = {s: i for i, s in enumerate(bundle.get_labels())}
    out = REPORT / "Alignment"
    out.mkdir(parents=True, exist_ok=True)
    for path in sorted((REPORT / "Analysis").glob("*.json")):
        target = out / path.name
        if target.exists():
            print("Cached", path.stem, flush=True)
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        decoded = subprocess.run(
            [imageio_ffmpeg.get_ffmpeg_exe(), "-v", "error", "-i", data["file"],
             "-ar", "16000", "-ac", "1", "-f", "f32le", "pipe:1"],
            check=True, capture_output=True,
        )
        samples = np.frombuffer(decoded.stdout, dtype="<f4").copy()
        waveform = torch.from_numpy(samples).unsqueeze(0).to(device)
        normalized = [re.sub("[^A-Z']", "", w["text"].upper()) for w in data["words"]]
        transcript = "|".join(normalized)
        tokens = [dictionary[c] for c in transcript]
        with torch.inference_mode():
            emissions, _ = model(waveform)
            emissions = emissions.log_softmax(dim=-1).cpu()
        alignments, scores = torchaudio.functional.forced_align(
            emissions, torch.tensor([tokens], dtype=torch.int64), blank=0,
        )
        spans = torchaudio.functional.merge_tokens(alignments[0], scores[0].exp())
        assert [s.token for s in spans] == tokens
        ratio = len(samples) / 16000 / emissions.shape[1]
        cursor = 0
        for word, clean in zip(data["words"], normalized):
            first, last = spans[cursor], spans[cursor + len(clean) - 1]
            word["whisper_start"] = word["start"]
            word["whisper_end"] = word["end"]
            word["start"] = round(max(0, first.start * ratio - 0.015), 5)
            word["end"] = round(last.end * ratio + 0.025, 5)
            word["ctc_score"] = round(float(np.mean([s.score for s in spans[cursor:cursor + len(clean)]])), 5)
            cursor += len(clean) + 1
        data["timing_method"] = "Wav2Vec2 CTC forced alignment; original Whisper times retained"
        target.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        print(path.stem, "aligned", len(data["words"]), flush=True)


if __name__ == "__main__":
    main()
