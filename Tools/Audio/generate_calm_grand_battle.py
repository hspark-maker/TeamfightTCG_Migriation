#!/usr/bin/env python3
"""Generate CalmGrandBattle, an original orchestral card-battle loop.

Every instrument is synthesized from deterministic waveforms. The generator
uses no samples, soundfonts, presets, or existing musical material.

Requires: Python 3.10+ and NumPy.
"""

from __future__ import annotations

import argparse
import functools
import math
import wave
from pathlib import Path

import numpy as np


SAMPLE_RATE = 44_100
BPM = 75
BEATS_PER_BAR = 4
BARS = 32
SAMPLES_PER_BEAT = SAMPLE_RATE * 60.0 / BPM
TOTAL_SAMPLES = int(round(SAMPLES_PER_BEAT * BEATS_PER_BAR * BARS))
RNG = np.random.default_rng(20260815)


def configure_profile(profile: str) -> None:
    """Select tempo/seed before any cached instrument is rendered."""
    global BPM, SAMPLES_PER_BEAT, TOTAL_SAMPLES, RNG
    BPM = 96 if profile == "tcg" else 75
    SAMPLES_PER_BEAT = SAMPLE_RATE * 60.0 / BPM
    TOTAL_SAMPLES = int(round(SAMPLES_PER_BEAT * BEATS_PER_BAR * BARS))
    RNG = np.random.default_rng(20260816 if profile == "tcg" else 20260815)
    string_ensemble.cache_clear()
    horn.cache_clear()
    flute.cache_clear()
    harp.cache_clear()
    choir_vowel.cache_clear()


def midi_frequency(note: int) -> float:
    return 440.0 * (2.0 ** ((note - 69) / 12.0))


def attack_curve(t: np.ndarray, seconds: float) -> np.ndarray:
    return 1.0 - np.exp(-t / max(seconds, 1e-5))


def release_curve(t: np.ndarray, duration: float, seconds: float) -> np.ndarray:
    return np.minimum(1.0, np.maximum(0.0, (duration - t) / seconds))


@functools.lru_cache(maxsize=128)
def string_ensemble(note: int, length_millibeats: int, brightness: int = 5) -> np.ndarray:
    """Warm, slowly bowed ensemble made from detuned harmonic oscillators."""
    length_beats = length_millibeats / 1000.0
    duration = length_beats * 60.0 / BPM + 0.62
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    base = midi_frequency(note)
    tone = np.zeros_like(t)
    detunes = (-5.5, -1.7, 2.2, 6.1)
    harmonic_count = max(3, min(7, brightness))
    for voice, cents in enumerate(detunes):
        frequency = base * (2.0 ** (cents / 1200.0))
        vibrato = 0.0022 * np.sin(2.0 * np.pi * (4.65 + voice * 0.11) * t + voice * 1.31)
        phase = 2.0 * np.pi * frequency * t + vibrato
        for harmonic in range(1, harmonic_count + 1):
            amplitude = 1.0 / (harmonic ** 1.48)
            if harmonic % 2 == 0:
                amplitude *= 0.78
            tone += amplitude * np.sin(harmonic * phase + voice * 0.43 + harmonic * 0.09)
    bow_motion = 0.94 + 0.06 * np.sin(2.0 * np.pi * 0.37 * t + note * 0.13)
    envelope = attack_curve(t, 0.34) * release_curve(t, duration, 0.54)
    return (0.16 * tone * bow_motion * envelope).astype(np.float32)


@functools.lru_cache(maxsize=96)
def horn(note: int, length_millibeats: int) -> np.ndarray:
    length_beats = length_millibeats / 1000.0
    duration = length_beats * 60.0 / BPM + 0.42
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    frequency = midi_frequency(note)
    vibrato = 0.0015 * np.sin(2.0 * np.pi * 4.35 * t + 0.8)
    phase = 2.0 * np.pi * frequency * t + vibrato
    tone = (
        np.sin(phase)
        + 0.52 * np.sin(2.0 * phase + 0.18)
        + 0.39 * np.sin(3.0 * phase + 0.47)
        + 0.16 * np.sin(4.0 * phase + 0.91)
        + 0.07 * np.sin(5.0 * phase + 1.12)
    )
    breath = RNG.normal(0.0, 1.0, len(t)).astype(np.float32)
    breath = np.convolve(breath, np.ones(9, dtype=np.float32) / 9.0, mode="same")
    swell = 0.90 + 0.10 * np.sin(np.pi * np.minimum(1.0, t / max(0.2, duration - 0.4)))
    envelope = attack_curve(t, 0.16) * release_curve(t, duration, 0.34)
    return ((0.49 * tone + 0.008 * breath) * swell * envelope).astype(np.float32)


@functools.lru_cache(maxsize=96)
def flute(note: int, length_millibeats: int) -> np.ndarray:
    length_beats = length_millibeats / 1000.0
    duration = length_beats * 60.0 / BPM + 0.30
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    frequency = midi_frequency(note)
    vibrato = 0.0028 * np.sin(2.0 * np.pi * 5.05 * t + 0.3)
    phase = 2.0 * np.pi * frequency * t + vibrato
    tone = np.sin(phase) + 0.16 * np.sin(2.0 * phase + 0.4) + 0.055 * np.sin(3.0 * phase + 0.9)
    air = RNG.normal(0.0, 1.0, len(t)).astype(np.float32)
    air = np.concatenate(([0.0], np.diff(air))).astype(np.float32)
    envelope = attack_curve(t, 0.075) * release_curve(t, duration, 0.24)
    return ((0.78 * tone + 0.0045 * air) * envelope).astype(np.float32)


@functools.lru_cache(maxsize=128)
def harp(note: int) -> np.ndarray:
    duration = 2.25
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    frequency = midi_frequency(note)
    tone = np.zeros_like(t)
    for harmonic in range(1, 10):
        amplitude = (1.0 / harmonic**1.22) * np.exp(-t * (1.65 + harmonic * 0.45))
        tone += amplitude * np.sin(2.0 * np.pi * frequency * harmonic * t + harmonic * 0.17)
    finger = RNG.normal(0.0, 1.0, len(t)).astype(np.float32) * np.exp(-t * 72.0)
    return ((0.30 * tone + 0.008 * finger) * attack_curve(t, 0.002)).astype(np.float32)


@functools.lru_cache(maxsize=64)
def choir_vowel(note: int, length_millibeats: int) -> np.ndarray:
    length_beats = length_millibeats / 1000.0
    duration = length_beats * 60.0 / BPM + 0.75
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    base = midi_frequency(note)
    tone = np.zeros_like(t)
    for cents, phase_offset in ((-7.0, 0.2), (-2.0, 1.7), (3.0, 3.1), (8.0, 4.4)):
        frequency = base * (2.0 ** (cents / 1200.0))
        phase = 2.0 * np.pi * frequency * t + phase_offset
        tone += (
            np.sin(phase)
            + 0.31 * np.sin(2.0 * phase + 0.5)
            + 0.12 * np.sin(3.0 * phase + 1.0)
        )
    envelope = attack_curve(t, 0.72) * release_curve(t, duration, 0.68)
    motion = 0.92 + 0.08 * np.sin(2.0 * np.pi * 0.21 * t + note)
    return (0.115 * tone * envelope * motion).astype(np.float32)


def timpani(note: int, strength: float = 1.0) -> np.ndarray:
    duration = 1.65
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    base = midi_frequency(note)
    falling = base * (1.0 + 0.085 * np.exp(-t * 15.0))
    phase = 2.0 * np.pi * np.cumsum(falling, dtype=np.float64) / SAMPLE_RATE
    body = np.sin(phase) + 0.27 * np.sin(1.52 * phase + 0.4)
    mallet = RNG.normal(0.0, 1.0, len(t)).astype(np.float32) * np.exp(-t * 50.0)
    return (strength * (0.72 * body * np.exp(-t * 2.9) + 0.025 * mallet)).astype(np.float32)


def suspended_cymbal() -> np.ndarray:
    duration = 2.4
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    noise = RNG.normal(0.0, 1.0, len(t)).astype(np.float32)
    high = np.concatenate(([0.0], np.diff(noise))).astype(np.float32)
    shimmer = 0.75 + 0.25 * np.sin(2.0 * np.pi * 7.3 * t) ** 2
    envelope = attack_curve(t, 0.42) * np.exp(-t * 0.68) * release_curve(t, duration, 0.32)
    return (high * shimmer * envelope).astype(np.float32)


@functools.lru_cache(maxsize=96)
def staccato_strings(note: int) -> np.ndarray:
    """Short bow stroke for the measured, thinking-game TCG pulse."""
    duration = 0.52 * 60.0 / BPM + 0.16
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    base = midi_frequency(note)
    tone = np.zeros_like(t)
    for voice, cents in enumerate((-4.0, 0.0, 4.5)):
        frequency = base * (2.0 ** (cents / 1200.0))
        phase = 2.0 * np.pi * frequency * t + voice * 0.61
        tone += (
            np.sin(phase)
            + 0.42 * np.sin(2.0 * phase + 0.2)
            + 0.21 * np.sin(3.0 * phase + 0.55)
            + 0.08 * np.sin(4.0 * phase + 0.9)
        )
    envelope = attack_curve(t, 0.028) * np.exp(-t * 5.2) * release_curve(t, duration, 0.13)
    return (0.25 * tone * envelope).astype(np.float32)


def frame_drum(low: bool = True) -> np.ndarray:
    duration = 0.34 if low else 0.20
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    frequency = 92.0 if low else 168.0
    body = np.sin(2.0 * np.pi * frequency * t) * np.exp(-t * (10.0 if low else 17.0))
    skin = RNG.normal(0.0, 1.0, len(t)).astype(np.float32)
    skin = np.convolve(skin, np.ones(5, dtype=np.float32) / 5.0, mode="same")
    return (0.62 * body + 0.035 * skin * np.exp(-t * 38.0)).astype(np.float32)


def wood_tick() -> np.ndarray:
    duration = 0.085
    t = np.arange(int(duration * SAMPLE_RATE), dtype=np.float32) / SAMPLE_RATE
    tone = np.sin(2.0 * np.pi * 880.0 * t) + 0.38 * np.sin(2.0 * np.pi * 1_340.0 * t + 0.4)
    return (tone * np.exp(-t * 49.0)).astype(np.float32)


def add_mono(mix: np.ndarray, sound: np.ndarray, start_sample: int, gain: float, pan: float = 0.0) -> None:
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


# bass, sustained string voicing, and harp chord tones.
CHORDS = {
    "D": (38, (50, 57, 62, 66, 69)),
    "A/C#": (37, (52, 57, 61, 64, 69)),
    "Bm7": (35, (47, 54, 57, 59, 62)),
    "Gmaj7": (43, (50, 54, 55, 59, 62)),
    "D/F#": (42, (50, 57, 62, 66, 69)),
    "Em7": (40, (52, 55, 59, 62, 64)),
    "Asus4": (45, (52, 57, 62, 64, 69)),
    "A": (45, (52, 57, 61, 64, 69)),
    "F#m": (42, (49, 54, 57, 61, 66)),
    "G": (43, (50, 55, 59, 62, 67)),
    "Bm/D": (38, (47, 54, 59, 62, 66)),
    "G/A": (45, (50, 55, 59, 62, 67)),
}

A_PROGRESSION = ["D", "A/C#", "Bm7", "Gmaj7", "D/F#", "Em7", "Asus4", "A"]
B_PROGRESSION = ["Bm7", "F#m", "Gmaj7", "D/F#", "Em7", "Bm/D", "G", "A"]
FINAL_PROGRESSION = ["D", "A/C#", "Bm7", "Gmaj7", "D/F#", "Em7", "G/A", "A"]
PROGRESSION = A_PROGRESSION + A_PROGRESSION + B_PROGRESSION + FINAL_PROGRESSION


# (beat, duration in beats, MIDI note). Long breaths are intentional: the
# orchestra carries scale while the melody leaves room for card impacts.
A_MELODY = [
    [(0.0, 2.0, 66), (2.0, 1.0, 69), (3.0, 1.0, 71)],
    [(0.0, 2.0, 69), (2.0, 1.0, 64), (3.0, 1.0, 66)],
    [(0.0, 1.0, 62), (1.0, 1.0, 66), (2.0, 2.0, 69)],
    [(0.0, 2.0, 67), (2.0, 1.0, 66), (3.0, 1.0, 64)],
    [(0.0, 2.0, 69), (2.0, 1.0, 74), (3.0, 1.0, 73)],
    [(0.0, 2.0, 71), (2.0, 1.0, 69), (3.0, 1.0, 67)],
    [(0.0, 1.0, 64), (1.0, 1.0, 66), (2.0, 2.0, 69)],
    [(0.0, 1.0, 64), (1.0, 1.0, 66), (2.0, 1.0, 69), (3.0, 1.0, 73)],
]

B_MELODY = [
    [(0.0, 2.0, 71), (2.0, 1.0, 69), (3.0, 1.0, 66)],
    [(0.0, 2.0, 66), (2.0, 1.0, 64), (3.0, 1.0, 61)],
    [(0.0, 1.0, 67), (1.0, 1.0, 69), (2.0, 2.0, 71)],
    [(0.0, 2.0, 69), (2.0, 2.0, 66)],
    [(0.0, 1.0, 64), (1.0, 1.0, 67), (2.0, 2.0, 71)],
    [(0.0, 2.0, 66), (2.0, 1.0, 62), (3.0, 1.0, 64)],
    [(0.0, 2.0, 67), (2.0, 1.0, 71), (3.0, 1.0, 69)],
    [(0.0, 1.0, 64), (1.0, 1.0, 66), (2.0, 2.0, 69)],
]


def compose(profile: str = "calm") -> np.ndarray:
    tcg_pulse = profile == "tcg"
    strings = np.zeros((TOTAL_SAMPLES, 2), dtype=np.float32)
    orchestra = np.zeros_like(strings)
    percussion = np.zeros_like(strings)

    for bar, chord_name in enumerate(PROGRESSION):
        bass_note, voicing = CHORDS[chord_name]
        section = bar // 8
        string_gain = (0.048, 0.058, 0.054, 0.078)[section]

        # Sustained strings: cellos/basses in the center, violas and violins
        # progressively wider. Overlap and circular writes preserve bow tails.
        add_mono(strings, string_ensemble(bass_note, 4250, 4), beat_sample(bar, 0.0), string_gain * 0.88, 0.0)
        for index, note in enumerate(voicing[1:]):
            pan = (-0.46, -0.18, 0.20, 0.48)[index]
            brightness = 4 + (1 if index >= 2 else 0)
            add_mono(strings, string_ensemble(note, 4250, brightness), beat_sample(bar, 0.0), string_gain, pan)

        # Gentle cello breath on beat three adds movement without an ostinato.
        add_mono(
            strings,
            string_ensemble(voicing[1] - 12, 1750, 4),
            beat_sample(bar, 2.0),
            string_gain * 0.44,
            -0.08,
        )

        # Harp arpeggio density grows across the form. In the TCG profile it
        # avoids the upper-string grid and marks choices on 1, 2&, 3, 4&.
        if tcg_pulse and section == 2 and bar < 20:
            harp_beats = (0.0, 2.0)
            harp_order = (0, 4)
            harp_gain = 0.041
        elif tcg_pulse:
            harp_beats = (0.0, 1.5, 2.0, 3.5)
            harp_order = (0, 2, 4, 3)
            harp_gain = 0.046 if section == 3 else 0.043
        elif section == 0:
            harp_beats = (0.0, 1.0, 2.0, 3.0)
            harp_order = (0, 2, 4, 2)
            harp_gain = 0.052
        elif section == 2 and bar < 20:
            harp_beats = (0.0, 1.5, 3.0)
            harp_order = (0, 3, 1)
            harp_gain = 0.045
        else:
            harp_beats = tuple(step * 0.5 for step in range(8))
            harp_order = (0, 2, 4, 3, 1, 3, 4, 2)
            harp_gain = 0.041 if section == 1 else (0.050 if section == 3 else 0.046)
        for beat, order in zip(harp_beats, harp_order):
            note = voicing[order] + (12 if order >= 3 else 0)
            add_mono(orchestra, harp(note), beat_sample(bar, beat), harp_gain, -0.24 + 0.06 * order)

        if tcg_pulse:
            # A card battle needs a readable clock, not action-game drumming.
            # Offbeat upper strings suggest planning; the cello marks turns.
            if bar < 4:
                pulse_steps = ()
            elif section == 0:
                pulse_steps = (1, 3, 5, 7)
            elif section == 2 and bar < 20:
                pulse_steps = (0, 3, 4, 7)
            elif section == 3 and bar >= 28:
                pulse_steps = (0, 3, 4, 7)
            else:
                pulse_steps = (0, 1, 3, 4, 5, 7)
            pulse_order = (0, 2, 1, 3, 0, 3, 1, 2)
            pulse_gain = (0.030, 0.032, 0.028, 0.036)[section]
            for step in pulse_steps:
                note = voicing[pulse_order[step]] + (12 if step % 2 else 0)
                pan = -0.20 if step % 2 == 0 else 0.24
                add_mono(orchestra, staccato_strings(note), beat_sample(bar, step * 0.5), pulse_gain, pan)
            for beat in (0.0, 2.0):
                add_mono(
                    orchestra,
                    staccato_strings(bass_note + 12),
                    beat_sample(bar, beat),
                    0.020 if section < 3 else 0.024,
                    -0.08,
                )

        melody = B_MELODY[bar - 16] if section == 2 else A_MELODY[bar % 8]
        if tcg_pulse and bar in (28, 29, 30):
            melody = []
        elif tcg_pulse and bar == 31:
            # The final bar is harmony plus a tiny A-C# pickup, not a fresh
            # statement of the theme. It resolves into the opening D on loop.
            melody = [(3.0, 0.5, 69), (3.5, 0.5, 73)]
        for beat, duration, note in melody:
            if section == 0:
                add_mono(orchestra, flute(note, int(duration * 900)), beat_sample(bar, beat), 0.067, 0.17)
            elif section == 1:
                add_mono(orchestra, flute(note, int(duration * 900)), beat_sample(bar, beat), 0.060, 0.20)
                if duration >= 2.0:
                    add_mono(orchestra, horn(note - 12, int(duration * 920)), beat_sample(bar, beat), 0.030, -0.14)
            elif section == 2:
                lead = horn(note - 12, int(duration * 920)) if bar >= 20 else flute(note, int(duration * 900))
                gain = 0.055 if bar >= 20 else 0.058
                add_mono(orchestra, lead, beat_sample(bar, beat), gain, -0.08 if bar >= 20 else 0.16)
            else:
                add_mono(orchestra, horn(note - 12, int(duration * 940)), beat_sample(bar, beat), 0.062, -0.12)
                add_mono(orchestra, flute(note + 12, int(duration * 880)), beat_sample(bar, beat), 0.029, 0.23)

        # Broad horn countermelody, limited to one swell every two bars.
        if section >= 1 and bar % 2 == 1 and not (tcg_pulse and bar >= 28):
            counter_note = voicing[2]
            add_mono(orchestra, horn(counter_note, 3400), beat_sample(bar, 0.25), 0.034, -0.20)

        # Choir is a color, not a lead. It appears only in the late build and
        # stays below the dialogue/card-effect band.
        if bar >= 20:
            choir_gain = 0.018 if bar < 24 else 0.032
            for index, note in enumerate((voicing[1], voicing[3])):
                add_mono(
                    orchestra,
                    choir_vowel(note, 4250),
                    beat_sample(bar, 0.0),
                    choir_gain,
                    -0.36 if index == 0 else 0.36,
                )

        # Sparse timpani and cymbal swells provide scale without turning the
        # background track into a trailer cue.
        if bar in (8, 16, 20, 24, 28):
            strength = 0.75 if bar < 24 else 0.95
            add_mono(percussion, timpani(bass_note, strength), beat_sample(bar, 0.0), 0.080, 0.0)
        if bar in (15, 23, 31):
            add_mono(percussion, timpani(45, 0.72), beat_sample(bar, 3.0), 0.060, 0.0)
        if bar in (15, 23, 31):
            add_mono(percussion, suspended_cymbal(), beat_sample(bar, 1.0), 0.016, 0.18)
        if tcg_pulse:
            drum_gain = (0.026, 0.030, 0.024, 0.034)[section]
            add_mono(percussion, frame_drum(True), beat_sample(bar, 0.0), drum_gain, 0.0)
            add_mono(percussion, frame_drum(False), beat_sample(bar, 2.0), drum_gain * 0.64, 0.0)
            for beat in (1.0, 3.0):
                add_mono(percussion, wood_tick(), beat_sample(bar, beat), 0.013, 0.12)

    # Circular concert-hall reflections: the reverb is already in steady state
    # at sample zero, so the loop boundary does not chop a hall tail.
    hall = strings + orchestra
    reflections = (
        hall
        + 0.070 * np.roll(hall[:, ::-1], int(SAMPLE_RATE * 0.043), axis=0)
        + 0.052 * np.roll(hall, int(SAMPLE_RATE * 0.079), axis=0)
        + 0.041 * np.roll(hall[:, ::-1], int(SAMPLE_RATE * 0.137), axis=0)
        + 0.028 * np.roll(hall, int(SAMPLE_RATE * 0.263), axis=0)
        + 0.018 * np.roll(hall[:, ::-1], int(SAMPLE_RATE * 0.421), axis=0)
    )
    mix = reflections + percussion

    # Circular mastering EQ: remove sub-rumble, soften the voice/SFX presence
    # range, and retain enough air for an orchestral rather than synth-like top.
    frequencies = np.fft.rfftfreq(TOTAL_SAMPLES, 1.0 / SAMPLE_RATE)
    high_pass = 1.0 / (1.0 + np.exp(-(frequencies - 27.0) / 6.0))
    low_pass = 1.0 / (1.0 + np.exp((frequencies - 17_000.0) / 950.0))
    presence_dip = 1.0 - 0.13 * (
        1.0 / (1.0 + np.exp(-(frequencies - 1_600.0) / 280.0))
        - 1.0 / (1.0 + np.exp(-(frequencies - 4_300.0) / 620.0))
    )
    master_curve = high_pass * low_pass * presence_dip
    for channel in range(2):
        spectrum = np.fft.rfft(mix[:, channel])
        mix[:, channel] = np.fft.irfft(spectrum * master_curve, n=TOTAL_SAMPLES).astype(np.float32)

    mix = np.tanh(mix * 0.92) / np.tanh(0.92)
    peak = float(np.max(np.abs(mix)))
    if peak > 0.0:
        mix *= 0.66 / peak
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
    parser.add_argument("--profile", choices=("calm", "tcg"), default="calm")
    args = parser.parse_args()
    configure_profile(args.profile)
    audio = compose(args.profile)
    assert audio.shape == (TOTAL_SAMPLES, 2)
    write_wav_24(args.output, audio)
    print(f"Wrote {args.output} ({TOTAL_SAMPLES} samples/channel, {TOTAL_SAMPLES / SAMPLE_RATE:.3f}s)")


if __name__ == "__main__":
    main()
