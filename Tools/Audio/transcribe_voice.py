"""Local-only word timing and content checks for the broadcast recordings."""

import argparse
import hashlib
import json
import os
import subprocess
from pathlib import Path

import imageio_ffmpeg
import numpy as np

os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("inputs", nargs="+")
    parser.add_argument("--output", required=True)
    parser.add_argument("--model", default="small")
    parser.add_argument("--cache", required=True)
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    from faster_whisper import WhisperModel

    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    print("Loading local speech model", args.model, flush=True)
    model = WhisperModel(args.model, device="cpu", compute_type="int8",
                         cpu_threads=6, download_root=args.cache)
    paths = []
    for item in args.inputs:
        path = Path(item)
        paths.extend(sorted(p for p in path.iterdir() if p.suffix.lower() in {".mp3", ".wav", ".flac"}) if path.is_dir() else [path])
    for path in paths:
        target = output / (path.stem + ".json")
        if target.exists() and not args.force:
            print("Cached", path.name, flush=True)
            continue
        decoded = subprocess.run(
            [imageio_ffmpeg.get_ffmpeg_exe(), "-v", "error", "-i", str(path),
             "-f", "f32le", "-ac", "1", "-ar", "16000", "pipe:1"],
            check=True, capture_output=True,
        )
        audio = np.frombuffer(decoded.stdout, dtype="<f4").copy()
        segments, info = model.transcribe(
            audio, word_timestamps=True, beam_size=5, temperature=0,
            vad_filter=False, condition_on_previous_text=False,
        )
        words, lines = [], []
        for segment in segments:
            lines.append(segment.text.strip())
            for word in segment.words or []:
                words.append(dict(text=word.word.strip(), start=word.start,
                                  end=word.end, probability=word.probability))
        result = dict(file=str(path.resolve()), file_sha256=hashlib.sha256(path.read_bytes()).hexdigest(), language=info.language,
                      language_probability=info.language_probability,
                      text=" ".join(lines), words=words)
        target.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        print(path.name, info.language, round(info.language_probability, 3), result["text"], flush=True)


if __name__ == "__main__":
    main()
