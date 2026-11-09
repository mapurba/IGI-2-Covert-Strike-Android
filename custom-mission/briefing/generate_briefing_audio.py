"""Generates the Operation Redemption briefing voice-over and its caption timings.

Every line in transcript.json is spoken by an installed Windows voice through
synthesize_line.ps1 (offline; nothing is sent to an online service). The lines are
cleaned, given a light radio treatment where the speaker is on comms, joined with
squelch clicks and short gaps, and written as the mono 16-bit PCM WAV the game's
WavPcm reader accepts. A caption file with millisecond timings is written beside it;
the game shows those captions while the briefing plays.

Usage:
    python generate_briefing_audio.py [--out DIR]

To use a different (for example studio-recorded) voice-over, replace briefing.wav with
a mono 16-bit PCM WAV and adjust briefing_captions.txt to match its timings.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
LEAD_IN_SECONDS = 0.6
GAP_SECONDS = 0.45
TAIL_SECONDS = 0.8
PEAK = 10 ** (-3 / 20)


def read_wav(path: Path) -> tuple[np.ndarray, int]:
    with wave.open(str(path), "rb") as source:
        channels = source.getnchannels()
        width = source.getsampwidth()
        rate = source.getframerate()
        frames = source.readframes(source.getnframes())

    if width == 2:
        samples = np.frombuffer(frames, dtype="<i2").astype(np.float64) / 32768.0
    elif width == 1:
        samples = (np.frombuffer(frames, dtype=np.uint8).astype(np.float64) - 128.0) / 128.0
    elif width == 4:
        samples = np.frombuffer(frames, dtype="<i4").astype(np.float64) / 2147483648.0
    else:
        raise ValueError(f"{path}: unsupported sample width {width}")

    if channels > 1:
        samples = samples.reshape(-1, channels).mean(axis=1)

    return samples, rate


def resample(samples: np.ndarray, source_rate: int, target_rate: int) -> np.ndarray:
    if source_rate == target_rate:
        return samples

    if target_rate < source_rate:
        samples = band_limit(samples, source_rate, 0.0, target_rate * 0.45)

    duration = len(samples) / source_rate
    count = int(round(duration * target_rate))
    source_times = np.arange(len(samples)) / source_rate
    target_times = np.arange(count) / target_rate
    return np.interp(target_times, source_times, samples)


def band_limit(samples: np.ndarray, rate: int, low: float, high: float) -> np.ndarray:
    """Zero-phase band-pass through the spectrum, with soft edges so it does not ring."""
    spectrum = np.fft.rfft(samples)
    freqs = np.fft.rfftfreq(len(samples), 1.0 / rate)
    gain = np.ones_like(freqs)
    if low > 0:
        gain *= 1.0 / (1.0 + (low / np.maximum(freqs, 1e-6)) ** 4)
    if high > 0:
        gain *= 1.0 / (1.0 + (freqs / high) ** 4)
    return np.fft.irfft(spectrum * gain, n=len(samples))


def trim_silence(samples: np.ndarray, rate: int, threshold: float = 0.004) -> np.ndarray:
    loud = np.flatnonzero(np.abs(samples) > threshold)
    if loud.size == 0:
        return samples

    pad = int(0.04 * rate)
    start = max(int(loud[0]) - pad, 0)
    end = min(int(loud[-1]) + pad, len(samples))
    return samples[start:end]


def radio(samples: np.ndarray, rate: int, rng: np.random.Generator) -> np.ndarray:
    """Comms-headset colouring: a telephone band, a little drive, and a faint carrier hiss."""
    voiced = band_limit(samples, rate, 320.0, 3300.0)
    voiced /= max(np.max(np.abs(voiced)), 1e-6)
    driven = np.tanh(voiced * 1.8) / np.tanh(1.8)
    hiss = band_limit(rng.normal(0.0, 1.0, len(samples)), rate, 1500.0, 5000.0)
    hiss *= 0.012 / max(np.std(hiss), 1e-6)
    return driven * 0.9 + hiss


def field_mic(samples: np.ndarray, rate: int) -> np.ndarray:
    return band_limit(samples, rate, 110.0, 7500.0)


def squelch(rate: int, rng: np.random.Generator, opening: bool) -> np.ndarray:
    """The short click-and-burst a radio makes as the key goes down or comes up."""
    length = int(0.07 * rate)
    noise = band_limit(rng.normal(0.0, 1.0, length), rate, 900.0, 4500.0)
    noise /= max(np.max(np.abs(noise)), 1e-6)
    envelope = np.exp(-np.linspace(0.0, 7.0, length))
    if not opening:
        envelope = envelope[::-1]
    burst = noise * envelope * 0.28
    click = np.zeros(length)
    click[0 if opening else -1] = 0.5
    return burst + click


def split_captions(text: str) -> list[str]:
    """Breaks a line into caption-sized pieces on sentence boundaries."""
    sentences = [s.strip() for s in re.split(r"(?<=[.?!])\s+", text) if s.strip()]
    captions: list[str] = []
    current = ""
    for sentence in sentences:
        candidate = f"{current} {sentence}".strip()
        if current and len(candidate) > 110:
            captions.append(current)
            current = sentence
        else:
            current = candidate
    if current:
        captions.append(current)

    # A caption shorter than this flashes past before it can be read; fold it into a neighbour.
    merged: list[str] = []
    pending = ""
    for caption in captions:
        if pending:
            caption = f"{pending} {caption}"
            pending = ""
        if len(caption) < 24:
            if merged:
                merged[-1] = f"{merged[-1]} {caption}"
            else:
                pending = caption
            continue
        merged.append(caption)
    if pending:
        merged.append(pending)
    return merged


def synthesize(line_text: str, voice: dict, work: Path, index: int) -> tuple[np.ndarray, int]:
    text_file = work / f"line{index:02d}.txt"
    wav_file = work / f"line{index:02d}.wav"
    text_file.write_text(line_text, encoding="utf-8")

    errors: list[str] = []
    for name in (voice["voice"], voice.get("fallback")):
        if not name:
            continue
        result = subprocess.run(
            [
                "powershell",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(HERE / "synthesize_line.ps1"),
                "-Voice",
                name,
                "-TextFile",
                str(text_file),
                "-OutFile",
                str(wav_file),
                "-Rate",
                str(voice.get("rate", 1.0)),
                "-Pitch",
                str(voice.get("pitch", 1.0)),
            ],
            capture_output=True,
            text=True,
        )
        if result.returncode == 0 and wav_file.exists():
            print(f"  line {index + 1}: {name}")
            return read_wav(wav_file)
        errors.append(f"{name}: {result.stderr.strip() or result.stdout.strip()}")

    raise RuntimeError("Could not synthesize line {}:\n{}".format(index + 1, "\n".join(errors)))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=HERE, help="directory for briefing.wav and briefing_captions.txt")
    args = parser.parse_args()

    script = json.loads((HERE / "transcript.json").read_text(encoding="utf-8"))
    rate = int(script.get("sampleRate", 22050))
    voices = script["voices"]
    rng = np.random.default_rng(1729)

    pieces: list[np.ndarray] = [np.zeros(int(LEAD_IN_SECONDS * rate))]
    cursor = len(pieces[0])
    captions: list[tuple[int, int, str, str]] = []

    with tempfile.TemporaryDirectory(prefix="igi-briefing-") as temp:
        work = Path(temp)
        for index, line in enumerate(script["lines"]):
            voice = voices[line["speaker"]]
            spoken, source_rate = synthesize(line["text"], voice, work, index)
            spoken = trim_silence(resample(spoken, source_rate, rate), rate)
            spoken /= max(np.max(np.abs(spoken)), 1e-6)

            on_radio = bool(voice.get("radio"))
            spoken = radio(spoken, rate, rng) if on_radio else field_mic(spoken, rate)

            if on_radio:
                opening = squelch(rate, rng, opening=True)
                pieces.append(opening)
                cursor += len(opening)

            start = cursor
            pieces.append(spoken)
            cursor += len(spoken)
            end = cursor

            if on_radio:
                closing = squelch(rate, rng, opening=False)
                pieces.append(closing)
                cursor += len(closing)

            gap = np.zeros(int(GAP_SECONDS * rate))
            pieces.append(gap)
            cursor += len(gap)

            parts = split_captions(line["text"])
            weights = np.array([len(p) for p in parts], dtype=np.float64)
            bounds = np.concatenate([[0.0], np.cumsum(weights) / weights.sum()])
            for part, lo, hi in zip(parts, bounds[:-1], bounds[1:]):
                part_start = start + (end - start) * lo
                part_end = start + (end - start) * hi
                captions.append(
                    (
                        int(round(part_start * 1000 / rate)),
                        int(round(part_end * 1000 / rate)),
                        line["speaker"],
                        part,
                    )
                )

    pieces.append(np.zeros(int(TAIL_SECONDS * rate)))
    mix = np.concatenate(pieces)
    mix *= PEAK / max(np.max(np.abs(mix)), 1e-6)
    pcm = np.clip(np.round(mix * 32767.0), -32768, 32767).astype("<i2")

    args.out.mkdir(parents=True, exist_ok=True)
    wav_path = args.out / "briefing.wav"
    with wave.open(str(wav_path), "wb") as target:
        target.setnchannels(1)
        target.setsampwidth(2)
        target.setframerate(rate)
        target.writeframes(pcm.tobytes())

    caption_path = args.out / "briefing_captions.txt"
    with caption_path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("# start_ms|end_ms|speaker|text\n")
        for start_ms, end_ms, speaker, text in captions:
            handle.write(f"{start_ms}|{end_ms}|{speaker}|{text}\n")

    print(f"Wrote {wav_path} ({len(pcm) / rate:.1f} s, {rate} Hz mono 16-bit)")
    print(f"Wrote {caption_path} ({len(captions)} captions)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
