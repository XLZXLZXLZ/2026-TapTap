"""Build Unity VoiceSequence assets against existing masters, without editing audio."""

import hashlib
import json
import re
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REPORT = ROOT / "Design/AudioVoice"
FOLDER = ROOT / "Assets/Audio/Voice/Sequences"
SCRIPT = ROOT / "Assets/C# Scripts/Audio/VoiceSequence.cs.meta"


def guid(path):
    return re.search(r"^guid: (\w+)", path.read_text(encoding="utf-8"), re.M)[1]


def quote(text):
    return json.dumps(text, ensure_ascii=False)


def clean_english(text):
    return re.sub(r"\s+-", "-", text)


def subtitle_cues(entry, rules, fragmented=False):
    result = []
    for rule in rules:
        source = [entry["cues"][index] for index in rule["cues"]]
        result.append(dict(
            start=source[0]["start"],
            end=min(entry["duration"] - 0.01, source[-1]["end"] + rule.get("hold_after", 0.18)),
            chinese=rule["chinese"],
            english=clean_english((" … " if fragmented else " ").join(c["text"] for c in source)),
        ))
    for i, cue in enumerate(result):
        if i + 1 < len(result):
            cue["end"] = min(cue["end"], result[i + 1]["start"] - 0.02)
        cue["start"] = round(cue["start"], 4)
        cue["end"] = round(cue["end"], 4)
    return result


def write_asset(name, segments):
    path = FOLDER / (name + ".asset")
    lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:", "--- !u!114 &11400000",
             "MonoBehaviour:", "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}",
             "  m_PrefabInstance: {fileID: 0}", "  m_PrefabAsset: {fileID: 0}",
             "  m_GameObject: {fileID: 0}", "  m_Enabled: 1", "  m_EditorHideFlags: 0",
             f"  m_Script: {{fileID: 11500000, guid: {guid(SCRIPT)}, type: 3}}",
             f"  m_Name: {name}", "  m_EditorClassIdentifier: ", "  Volume: 1", "  Segments:"]
    for segment in segments:
        clip = ROOT / segment["file"]
        lines += [f"  - Clip: {{fileID: 8300000, guid: {guid(Path(str(clip) + '.meta'))}, type: 3}}",
                  "    Subtitles:"]
        for cue in segment["subtitles"]:
            assert 0 <= cue["start"] < cue["end"] <= segment["duration"]
            lines += [f"    - Start: {cue['start']}", f"      End: {cue['end']}",
                      f"      Chinese: {quote(cue['chinese'])}", f"      English: {quote(cue['english'])}"]
        lines += ['    Chinese: ""', '    English: ""', f"    GapAfter: {segment['gap_after']}"]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        value = uuid.uuid5(uuid.NAMESPACE_URL, "taptap-radio/" + path.relative_to(ROOT).as_posix()).hex
        meta.write_text(f"fileFormatVersion: 2\nguid: {value}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")
    return dict(name=name, asset=path.relative_to(ROOT).as_posix(), segments=segments)


def main():
    manifest = json.loads((REPORT / "voice_manifest.json").read_text(encoding="utf-8"))
    content = json.loads(Path(__file__).with_name("radio_subtitles_zh.json").read_text(encoding="utf-8"))
    entries = {entry["id"]: entry for entry in manifest["files"]}
    audio_hashes = {entry["file"]: hashlib.sha256((ROOT / entry["file"]).read_bytes()).hexdigest() for entry in entries.values()}
    catalog = []
    for key, rules in content["full"].items():
        entry = next(e for e in entries.values() if e["id"].startswith(key + "_") and e["id"].endswith("_Full"))
        segment = dict(file=entry["file"], duration=entry["duration"], gap_after=0,
                       subtitles=subtitle_cues(entry, rules))
        catalog.append(write_asset(key + "_FirstEncounter", [segment]))
    for key, rules in content["return"].items():
        entry = next(e for e in entries.values() if e["id"].startswith(key + "_") and e["id"].endswith("_PowerCut"))
        segment = dict(file=entry["file"], duration=entry["duration"], gap_after=0,
                       subtitles=subtitle_cues(entry, rules, fragmented=True))
        catalog.append(write_asset(key + "_ReturnJourney", [segment]))
    segments = []
    for i, rule in enumerate(content["m07_return"]):
        entry = entries[rule["clip"]]
        segments.append(dict(file=entry["file"], duration=entry["duration"], gap_after=1 if i < 4 else 0,
                             subtitles=[dict(start=entry["cues"][0]["start"], end=round(entry["duration"] - 0.01, 4),
                                             chinese=rule["chinese"], english=entry["transcript"])]))
    assert segments[3]["file"] == segments[4]["file"]
    catalog.append(write_asset("M07_ReturnJourney", segments))
    assert len(catalog) == 18
    for path, value in audio_hashes.items():
        assert hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == value
    (REPORT / "radio_sequence_catalog.json").write_text(json.dumps(dict(
        sequence_count=len(catalog), audio_unchanged=True, master_sha256=audio_hashes,
        sequences=catalog), ensure_ascii=False, indent=2), encoding="utf-8")
    print("Configured 18 VoiceSequence assets with Chinese timelines; M07 has 5 segments, sharing the last clip; 21 masters unchanged.")


if __name__ == "__main__":
    main()
