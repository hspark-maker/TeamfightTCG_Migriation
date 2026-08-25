#!/usr/bin/env python3
"""Generate the original 64-second loop used by BrightCasualBattle.

The composition and every sound are synthesized locally from deterministic
waveforms. No samples, presets, or third-party musical material are used.

Requires: Python 3.10+ and NumPy.
"""

from __future__ import annotations

import argparse
import math
import wave
from pathlib import Path

import numpy as np


SAMPLE_RATE = 44_100
BPM = 120
BEATS_PER_BAR = 4
BARS = 32
SAMPLES_PER_BEAT = SAMPLE_RATE * 60 // BPM
TOTAL_SAMPLES = SAMPLES_PER_BEAT * BEATS_PER_BAR * BARS
RNG = np.random.default_rng(20260814)


def midi_frequency(note: int) -> float:
    return 440.0 * (2.0 ** ((note - 69) / 12.0))


def soft_attack(t: np.ndarray, seconds: float) -> np.ndarray:
    return 1.0 - np.exp(-t / max(seconds, 1e-5))


def pluck(note: int, length_beats: float = 0.46) -> np.ndarray:
    duration = length_beats * 60.0 / BPM + 0.42
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    f = midi_frequency(note)
    # Rounded kalimba/marimba hybrid: a soft fundamental with quickly dying
    # wooden upper partials. The short envelope keeps card SFX intelligible.
    tone = (
        np.sin(2.0 * np.pi * f * t) * np.exp(-t * 4.0)
        + 0.34 * np.sin(2.0 * np.pi * f * 2.01 * t + 0.18) * np.exp(-t * 7.5)
        + 0.15 * np.sin(2.0 * np.pi * f * 3.96 * t + 0.6) * np.exp(-t * 10.0)
    )
    knock = RNG.normal(0.0, 1.0, len(t)).astype(np.float32)
    knock = np.concatenate(([0.0], np.diff(knock))).astype(np.float32)
    tone += 0.018 * knock * np.exp(-t * 65.0)
    return (tone * soft_attack(t, 0.0035)).astype(np.float32)


def pizzicato(note: int, length_beats: float = 0.34) -> np.ndarray:
    duration = length_beats * 60.0 / BPM + 0.26
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    f = midi_frequency(note)
    tone = np.zeros_like(t)
    for harmonic in range(1, 7):
        amplitude = (1.0 / harmonic**1.7) * np.exp(-t * (5.0 + harmonic * 0.8))
        tone += amplitude * np.sin(2.0 * np.pi * f * harmonic * t + harmonic * 0.11)
    return (0.75 * tone * soft_attack(t, 0.0025)).astype(np.float32)


def warm_bass(note: int, length_beats: float) -> np.ndarray:
    duration = length_beats * 60.0 / BPM + 0.12
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    f = midi_frequency(note)
    tone = (
        np.sin(2.0 * np.pi * f * t)
        + 0.24 * np.sin(2.0 * np.pi * f * 2.0 * t + 0.2)
        + 0.08 * np.sin(2.0 * np.pi * f * 3.0 * t + 0.4)
    )
    gate = np.minimum(1.0, np.maximum(0.0, (duration - t) / 0.085))
    return (tone * soft_attack(t, 0.012) * gate).astype(np.float32)


def airy_pad(note: int, length_beats: float = 4.0) -> np.ndarray:
    duration = length_beats * 60.0 / BPM + 0.28
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    f = midi_frequency(note)
    vibrato = 0.0022 * np.sin(2.0 * np.pi * 0.31 * t)
    phase = 2.0 * np.pi * f * t + vibrato
    tone = (
        np.sin(phase)
        + 0.28 * np.sin(2.0 * phase + 0.4)
        + 0.10 * np.sin(3.0 * phase + 0.9)
    )
    attack = soft_attack(t, 0.16)
    release = np.minimum(1.0, np.maximum(0.0, (duration - t) / 0.24))
    return (0.48 * tone * attack * release).astype(np.float32)


def glock(note: int) -> np.ndarray:
    duration = 1.35
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    f = midi_frequency(note)
    tone = (
        np.sin(2.0 * np.pi * f * t) * np.exp(-t * 2.9)
        + 0.42 * np.sin(2.0 * np.pi * f * 2.76 * t + 0.2) * np.exp(-t * 4.8)
        + 0.18 * np.sin(2.0 * np.pi * f * 5.41 * t + 0.5) * np.exp(-t * 7.0)
    )
    return (tone * soft_attack(t, 0.0018)).astype(np.float32)


def kick() -> np.ndarray:
    duration = 0.34
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    phase = 2.0 * np.pi * (46.0 * t + (92.0 - 46.0) * (1.0 - np.exp(-t * 25.0)) / 25.0)
    body = np.sin(phase) * np.exp(-t * 11.0)
    click = RNG.normal(0.0, 1.0, len(t)).astype(np.float32) * np.exp(-t * 85.0)
    return (0.88 * body + 0.025 * click).astype(np.float32)


def rim() -> np.ndarray:
    duration = 0.13
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    noise = RNG.normal(0.0, 1.0, len(t)).astype(np.float32)
    high = np.concatenate(([0.0], np.diff(noise))).astype(np.float32)
    wood = np.sin(2.0 * np.pi * 1_160.0 * t) + 0.45 * np.sin(2.0 * np.pi * 1_790.0 * t)
    return ((0.20 * high + 0.48 * wood) * np.exp(-t * 42.0)).astype(np.float32)


def shaker(accent: float = 1.0) -> np.ndarray:
    duration = 0.075
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    noise = RNG.normal(0.0, 1.0, len(t)).astype(np.float32)
    high = np.concatenate(([0.0], np.diff(noise))).astype(np.float32)
    return (accent * high * soft_attack(t, 0.0015) * np.exp(-t * 58.0)).astype(np.float32)


def add_mono(mix: np.ndarray, sound: np.ndarray, start_sample: int, gain: float, pan: float = 0.0) -> None:
    """Add a mono sound to a stereo circular buffer with equal-power panning."""
    start = start_sample % TOTAL_SAMPLES
    left_gain = math.cos((pan + 1.0) * math.pi / 4.0) * gain
    right_gain = math.sin((pan + 1.0) * math.pi / 4.0) * gain
    remaining = len(sound)
    offset = 0
    cursor = start
    while remaining > 0:
        count = min(remaining, TOTAL_SAMPLES - cursor)
        segment = sound[offset : offset + count]
        mix[cursor : cursor + count, 0] += segment * left_gain
        mix[cursor : cursor + count, 1] += segment * right_gain
        remaining -= count
        offset += count
        cursor = 0


def beat_sample(bar: int, beat: float) -> int:
    return int(round((bar * BEATS_PER_BAR + beat) * SAMPLES_PER_BEAT))


# bass, pad voicing, and melodic chord tones. The progression ends on D so
# the loop boundary resolves into the opening G without a fake fade-out.
CHORDS = {
    "G": (43, (55, 59, 62, 69)),
    "D/F#": (42, (54, 57, 62, 66)),
    "Em7": (40, (52, 55, 59, 62)),
    "Cadd9": (36, (52, 55, 60, 62)),
    "G/B": (47, (55, 59, 62, 67)),
    "Am7": (45, (57, 60, 64, 67)),
    "Dsus4": (38, (55, 57, 62, 67)),
    "D": (38, (54, 57, 62, 66)),
    "Bm7": (47, (54, 59, 62, 66)),
    "G/D": (38, (55, 59, 62, 67)),
}

A_PROGRESSION = ["G", "D/F#", "Em7", "Cadd9", "G/B", "Am7", "Dsus4", "D"]
B_PROGRESSION = ["Em7", "Bm7", "Cadd9", "G/D", "Am7", "Em7", "Cadd9", "D"]
PROGRESSION = A_PROGRESSION + A_PROGRESSION + B_PROGRESSION + A_PROGRESSION

# Eight eighth-note cells per bar. None is an intentional breath, not silence
# at the loop boundary. A and A' are memorable; B moves lower and gets sparser.
A_MELODY = [
    [71, 74, 76, 74, 71, None, 67, 69],
    [69, None, 66, 69, 74, 72, 69, None],
    [71, 76, 74, 71, 67, None, 69, 71],
    [76, 74, 72, None, 67, 69, 67, 64],
    [74, 71, 67, 69, 71, 74, 76, 74],
    [72, 76, 74, 72, 69, None, 71, 72],
    [67, 69, 74, 72, 69, 67, 66, 69],
    [66, 69, 72, 69, 74, None, 66, 69],
]

B_MELODY = [
    [71, None, 67, 64, None, 67, 69, 71],
    [66, None, 71, 69, 66, None, 62, 66],
    [64, 67, 72, None, 71, 67, 64, None],
    [62, 67, 71, 69, 67, None, 62, 64],
    [69, 72, 76, None, 72, 71, 69, 67],
    [67, 71, 76, 74, 71, None, 67, 69],
    [64, 67, 72, 74, 72, 71, 67, None],
    [69, 67, 66, 69, 74, None, 66, 69],
]


def compose() -> np.ndarray:
    melodic = np.zeros((TOTAL_SAMPLES, 2), dtype=np.float32)
    rhythm = np.zeros_like(melodic)
    pads = np.zeros_like(melodic)

    for bar, chord_name in enumerate(PROGRESSION):
        bass_note, voicing = CHORDS[chord_name]
        section = bar // 8

        # Wide, restrained pad. Alternating pans keep the center clear for
        # voices and impact sounds while bass and drums remain mono.
        for index, note in enumerate(voicing):
            pan = -0.42 + index * 0.28
            add_mono(pads, airy_pad(note), beat_sample(bar, 0.0), 0.050, pan)

        fifth = voicing[1] - 12
        add_mono(melodic, warm_bass(bass_note, 1.45), beat_sample(bar, 0.0), 0.18, 0.0)
        add_mono(melodic, warm_bass(fifth, 0.52), beat_sample(bar, 2.0), 0.13, 0.0)
        add_mono(melodic, warm_bass(bass_note + 12, 0.40), beat_sample(bar, 3.48), 0.10, 0.0)

        # Light pizzicato answer on offbeats.
        arp_order = (0, 2, 1, 3)
        arp_gain = 0.070 if section != 2 else 0.058
        for index, beat in enumerate((0.5, 1.5, 2.5, 3.5)):
            note = voicing[arp_order[index]]
            add_mono(melodic, pizzicato(note), beat_sample(bar, beat), arp_gain, -0.28)

        cells = B_MELODY[bar - 16] if section == 2 else A_MELODY[bar % 8]
        melody_gain = (0.125, 0.132, 0.112, 0.140)[section]
        for cell, note in enumerate(cells):
            if note is None:
                continue
            # Small deterministic pan motion feels playful without pulling the
            # hook away from the board center.
            pan = 0.10 + 0.08 * math.sin((bar * 8 + cell) * 0.71)
            add_mono(melodic, pluck(note), beat_sample(bar, cell * 0.5), melody_gain, pan)

        # A' and A'' receive a sparse upper response instead of a louder lead.
        if section in (1, 3) and bar % 2 == 1:
            response_note = voicing[2] + 12
            add_mono(melodic, pizzicato(response_note, 0.48), beat_sample(bar, 2.75), 0.047, -0.12)

        # Glockenspiel is deliberately rare: one signpost per phrase.
        if bar in (7, 15, 23, 29):
            bell_note = (74, 79, 76, 79)[(7, 15, 23, 29).index(bar)]
            add_mono(melodic, glock(bell_note), beat_sample(bar, 3.0), 0.050, 0.38)

        # Soft board-game groove. Section B opens up for four bars, then rebuilds.
        kick_gain = 0.18 if not (16 <= bar < 20) else 0.145
        add_mono(rhythm, kick(), beat_sample(bar, 0.0), kick_gain, 0.0)
        add_mono(rhythm, kick(), beat_sample(bar, 2.5), kick_gain * 0.72, 0.0)
        if bar % 4 == 3 and bar >= 8:
            add_mono(rhythm, kick(), beat_sample(bar, 3.5), kick_gain * 0.42, 0.0)

        for beat in (1.0, 3.0):
            add_mono(rhythm, rim(), beat_sample(bar, beat), 0.075, -0.10)

        for step in range(8):
            if 16 <= bar < 20 and step % 2 == 0:
                continue
            accent = 0.75 if step % 2 == 0 else 1.0
            pan = 0.26 if step % 2 else 0.18
            add_mono(rhythm, shaker(accent), beat_sample(bar, step * 0.5), 0.025, pan)

    # Circular delays model a loop already in steady state; the effect tail at
    # the end continues at sample zero instead of being cut or faded.
    delay_1 = int(SAMPLES_PER_BEAT * 0.50)
    delay_2 = int(SAMPLES_PER_BEAT * 0.75)
    room_1 = int(SAMPLE_RATE * 0.037)
    room_2 = int(SAMPLE_RATE * 0.061)
    melodic_fx = (
        melodic
        + 0.075 * np.roll(melodic, delay_1, axis=0)
        + 0.035 * np.roll(melodic, delay_2, axis=0)
        + 0.026 * np.roll(melodic[:, ::-1], room_1, axis=0)
        + 0.018 * np.roll(melodic[:, ::-1], room_2, axis=0)
    )

    mix = melodic_fx + pads + rhythm

    # Gentle circular spectral shaping: remove rumble and leave a broad pocket
    # in the voice/card-impact band without introducing a boundary transient.
    frequencies = np.fft.rfftfreq(TOTAL_SAMPLES, 1.0 / SAMPLE_RATE)
    high_pass = 1.0 / (1.0 + np.exp(-(frequencies - 31.0) / 7.0))
    low_pass = 1.0 / (1.0 + np.exp((frequencies - 17_500.0) / 900.0))
    mid_dip = 1.0 - 0.15 * (
        1.0 / (1.0 + np.exp(-(frequencies - 1_450.0) / 260.0))
        - 1.0 / (1.0 + np.exp(-(frequencies - 4_100.0) / 520.0))
    )
    master_curve = high_pass * low_pass * mid_dip
    for channel in range(2):
        spectrum = np.fft.rfft(mix[:, channel])
        mix[:, channel] = np.fft.irfft(spectrum * master_curve, n=TOTAL_SAMPLES).astype(np.float32)

    mix = np.tanh(mix * 1.18) / np.tanh(1.18)
    peak = float(np.max(np.abs(mix)))
    if peak > 0.0:
        mix *= 0.72 / peak
    return mix.astype(np.float32)


def write_wav_24(path: Path, audio: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = np.clip(np.round(audio * 8_388_607.0), -8_388_608, 8_388_607).astype(np.int32)
    interleaved = pcm.reshape(-1)
    unsigned = interleaved & 0xFFFFFF
    packed = np.empty((len(unsigned), 3), dtype=np.uint8)
    packed[:, 0] = unsigned & 0xFF
    packed[:, 1] = (unsigned >> 8) & 0xFF
    packed[:, 2] = (unsigned >> 16) & 0xFF
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(2)
        wav.setsampwidth(3)
        wav.setframerate(SAMPLE_RATE)
        wav.writeframes(packed.tobytes())


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="Destination 24-bit WAV path")
    args = parser.parse_args()
    audio = compose()
    assert audio.shape == (TOTAL_SAMPLES, 2)
    write_wav_24(args.output, audio)
    print(f"Wrote {args.output} ({TOTAL_SAMPLES} samples/channel, {TOTAL_SAMPLES / SAMPLE_RATE:.3f}s)")


if __name__ == "__main__":
    main()
