"""Inspect rendered WAVs, immutable sources and the progressive M07 edit plan."""

import hashlib
import json
import re
import wave

import numpy as np

from build_voice import REPORT, ROOT, decode


def main():
    manifest = json.loads((REPORT / "voice_manifest.json").read_text(encoding="utf-8"))
    checks = []
    for entry in manifest["files"]:
        path = ROOT / entry["file"]
        with wave.open(str(path), "rb") as handle:
            params = handle.getparams()
        audio = decode(path)
        check = dict(
            id=entry["id"], frames=len(audio), sample_rate=params.framerate,
            channels=params.nchannels, bits=params.sampwidth * 8,
            finite=bool(np.isfinite(audio).all()),
            sample_peak_dbfs=round(float(20 * np.log10(max(np.max(np.abs(audio)), 1e-12))), 3),
            dc_mean=round(float(np.mean(audio)), 7),
            source_unchanged=hashlib.sha256((ROOT / entry["source"]).read_bytes()).hexdigest() == entry["source_sha256"],
        )
        assert params.framerate == 48000 and params.nchannels == 1 and params.sampwidth == 3
        assert np.isfinite(audio).all() and np.max(np.abs(audio)) < 0.86
        assert abs(entry["integrated_lufs"] + 19) < 0.4
        assert entry["true_peak_dbtp"] <= -1.5
        assert check["source_unchanged"]
        assert abs(len(audio) / 48000 - entry["duration"]) < 0.0002
        if not entry["id"].endswith("_Full"):
            verification = REPORT / "Verification" / (entry["id"] + ".json")
            transcript = json.loads(verification.read_text(encoding="utf-8"))
            assert transcript.get("file_sha256") == hashlib.sha256(path.read_bytes()).hexdigest(), f"Stale transcription: {entry['id']}"
            normalize = lambda text: re.findall(r"[a-z]+", text.lower())
            assert normalize(transcript["text"]) == normalize(entry["transcript"]), entry["id"]
            check["transcript_matches_target"] = True
            check["verified_transcript"] = transcript["text"]
            check["output_sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        checks.append(check)
    m07 = [e for e in manifest["files"] if e["id"].startswith("M07_PowerCut")]
    sets = [{i for s in e["source_spans"] for i in range(s["words"][0], s["words"][1] + 1)} for e in m07]
    assert all(b < a for a, b in zip(sets, sets[1:]))
    assert manifest["m07_playback"][-1] == manifest["m07_playback"][-2]
    assert len(checks) == 21
    assert sum(len(list((ROOT / "Assets/Audio/Voice/Processed" / folder).glob("*.wav")))
               for folder in ["Full", "PowerCut"]) == 21
    report = dict(passed=True, unique_files=len(checks),
                  full_files=sum(e["id"].endswith("_Full") for e in manifest["files"]),
                  powercut_files=10, m07_nested_deletions=True,
                  m07_last_two_use_same_file=True, files=checks)
    (REPORT / "quality_checks.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PASS: 21 files; 10 cut transcripts match; 48 kHz mono PCM24; loudness/true peak; unchanged sources; nested M07 cuts; final file reused.")


if __name__ == "__main__":
    main()
