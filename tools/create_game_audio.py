#!/usr/bin/env python3
"""Reproducible original synthesis; no downloaded recordings or music samples."""
from pathlib import Path
import hashlib
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'UnityProject/Assets/Soccer/Resources/SoccerAudio'
SR = 44100
rng = np.random.default_rng(20261009)
entries = []


def clock(seconds):
    return np.arange(round(seconds * SR)) / SR


def tone(freq, seconds, decay=8, bright=0.15):
    t = clock(seconds)
    env = (1 - np.exp(-t * 450)) * np.exp(-t * decay)
    return env * (np.sin(2*np.pi*freq*t) + bright*np.sin(4*np.pi*freq*t))


def sweep(start, end, seconds, decay=10, noise=0):
    t = clock(seconds)
    phase = 2*np.pi*(start*t + (end-start)*t*t / (2*seconds))
    return (np.sin(phase) + noise*rng.normal(0, 1, len(t))) * np.exp(-t*decay) * np.minimum(t*500, 1)


def hiss(seconds, decay=12):
    t = clock(seconds)
    raw = rng.normal(0, 1, len(t))
    smooth = np.convolve(raw, np.ones(8)/8, mode='same')
    return smooth*np.exp(-t*decay)*np.minimum(t*300, 1)


def chord(notes, seconds, decay=5):
    return sum(tone(440*2**((n-69)/12), seconds, decay) for n in notes)/len(notes)


def write(name, samples, category, loop=False):
    samples = np.asarray(samples, dtype=np.float64)
    if not loop:
        edge = min(256, len(samples)//4)
        ramp = np.linspace(0, 1, edge)
        samples[:edge] *= ramp[:, None] if samples.ndim == 2 else ramp
        samples[-edge:] *= ramp[::-1, None] if samples.ndim == 2 else ramp[::-1]
    # Fixed headroom and deterministic PCM; preserve the music's internal dynamics.
    samples *= 0.72 / max(0.01, np.max(np.abs(samples)))
    data = (np.clip(samples, -1, 1)*32767).astype('<i2').tobytes()
    path = OUTPUT / (name+'.wav')
    with wave.open(str(path), 'wb') as wav:
        wav.setnchannels(2 if samples.ndim == 2 else 1)
        wav.setsampwidth(2)
        wav.setframerate(SR)
        wav.writeframes(data)
    entries.append(dict(name=name, category=category, seconds=round(len(samples)/SR, 3),
                        loop=loop, peak=round(float(np.max(np.abs(samples))), 4),
                        rms=round(float(np.sqrt(np.mean(samples**2))), 4),
                        sha256=hashlib.sha256(path.read_bytes()).hexdigest()))


def phrase(notes, interval, length):
    result = np.zeros(round(length*SR))
    for i, note in enumerate(notes):
        start = round(i*interval*SR)
        sound = tone(440*2**((note-69)/12), min(0.45, length-i*interval), 9)
        count = min(len(sound), len(result)-start)
        result[start:start+count] += sound[:count]*0.55
    return result


def music(name, bpm, energetic):
    beat = 60/bpm
    duration = 64*beat
    result = np.zeros((round(duration*SR), 2))

    def add(sound, when, gain=1, pan=0):
        # Fold tails across the loop boundary so ambience never drops at the seam.
        sound = sound*gain
        indices = (np.arange(len(sound)) + round(when*SR)) % len(result)
        result[indices, 0] += sound*np.sqrt((1-pan)/2)
        result[indices, 1] += sound*np.sqrt((1+pan)/2)

    progression = [(57, 60, 64), (53, 57, 60), (60, 64, 67), (55, 59, 62)]
    melody = [76, 72, 69, 72, 77, 76, 72, 69, 79, 76, 72, 76, 74, 71, 67, 71]
    for bar in range(16):
        notes = progression[bar % 4]
        add(chord(notes, beat*4.5, 0.8), bar*4*beat, 0.28, -0.18)
        for step in range(8):
            moment = (bar*4 + step/2)*beat
            add(tone(440*2**((notes[step % 3]+12-69)/12), beat*0.8, 9), moment,
                0.09 if energetic else 0.07, 0.32 if step % 2 else -0.32)
            if energetic or step % 2 == 0:
                add(hiss(0.085, 65), moment, 0.06 if step % 2 else 0.035, 0.2)
        for b in range(4):
            moment = (bar*4 + b)*beat
            add(sweep(145, 42, 0.27, 18), moment, 0.72 if energetic else 0.4)
            add(tone(440*2**((notes[0]-24-69)/12), beat*0.9, 5, 0.4), moment, 0.4)
            if b % 2:
                add(hiss(0.16, 25)+tone(185, 0.16, 25)*0.15, moment, 0.4 if energetic else 0.17)
        if bar >= 4:
            for b in range(2):
                note = melody[(bar % 4)*4 + b*2 + (bar//4 % 2)]
                add(tone(440*2**((note-69)/12), beat*1.6, 3, 0.07), (bar*4+b*2+0.5)*beat,
                    0.10 if energetic else 0.08, 0.1)
    # Soft compression keeps drums readable without hard clipping.
    write(name, np.tanh(result*1.2), 'music', loop=True)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write('ui-click', tone(680, .09, 35)+tone(1020, .09, 45)*.2, 'ui')
    write('ui-hover', tone(880, .065, 50), 'ui')
    write('ui-reject', phrase([55, 52], .08, .25), 'ui')
    write('ui-cancel', sweep(540, 240, .14, 18), 'ui')
    write('kick', sweep(180, 45, .22, 20, .13)+hiss(.22, 30)*.7, 'match')
    write('kick-power', sweep(260, 32, .5, 10, .13)+hiss(.5, 12)*.5, 'match')
    write('catch', tone(330, .14, 26)+tone(660, .14, 35)*.18, 'match')
    write('tackle', hiss(.22, 10)+sweep(250, 80, .22, 14)*.35, 'match')
    write('impact', sweep(130, 40, .23, 20, .2), 'match')
    write('steal', phrase([72, 79], .08, .3), 'match')
    write('bounce', tone(240, .13, 35, .35), 'match')
    write('countdown', tone(720, .12, 25), 'match')
    write('whistle', phrase([90, 90, 94], .065, .48), 'match')
    write('goal', phrase([72, 76, 79, 84], .12, 1.0)+chord([48, 60, 64, 67], 1, 4)*.45, 'match')
    write('finish', phrase([79, 76, 72, 84], .17, 1.3), 'match')
    write('ready', phrase([84, 88], .09, .32), 'match')
    write('combat-duang', sweep(260, 65, .24, 12)+tone(160, .24, 16)*.4, 'combat')
    write('combat-pan', tone(510, .18, 24, .5)+tone(833, .18, 27)*.4+hiss(.18, 35)*.12, 'combat')
    write('combat-rice', sweep(620, 180, .11, 30, .08), 'combat')
    write('combat-hit', hiss(.1, 45)*.8+sweep(170, 45, .1, 32), 'combat')
    write('combat-shield', tone(340, .15, 28, .3)+tone(680, .15, 38)*.2, 'combat')
    write('combat-ko', sweep(360, 70, .4, 6)+tone(105, .4, 12)*.25, 'combat')
    write('wind-q', hiss(.32, 6)+sweep(480, 1400, .32, 7)*.22, 'skill')
    write('wind-e', phrase([69, 76, 81], .09, .65)+hiss(.65, 8)*.2, 'skill')
    write('iron-q', tone(146, .55, 9, .7)+tone(437, .55, 14)*.35+hiss(.55, 25)*.2, 'skill')
    write('iron-e', chord([38, 45, 50], .65, 5)+sweep(85, 42, .65, 8)*.8, 'skill')
    write('arc-q', phrase([81, 85, 88], .085, .65), 'skill')
    write('arc-e', sweep(110, 440, .7, 4)+chord([45, 52, 57], .7, 5)*.45, 'skill')
    t = clock(.5)
    # Integer cycle counts keep this quiet charge loop phase continuous.
    write('charge-loop', .65*np.sin(2*np.pi*220*t)+.16*np.sin(2*np.pi*440*t), 'charge', loop=True)
    music('menu-loop', 100, False)
    music('match-loop', 116, True)
    (OUTPUT/'manifest.json').write_text(json.dumps(dict(sampleRate=SR, seed=20261009,
        source='Original procedural synthesis in tools/create_game_audio.py; no third-party samples.',
        assets=entries), ensure_ascii=False, indent=2)+'\n')
    (OUTPUT/'LICENSE.txt').write_text('Original procedural audio created for Skill Soccer.\n'
        'Generated by tools/create_game_audio.py without third-party recordings or samples.\n'
        'These generated audio assets are dedicated to the public domain under CC0 1.0.\n'
        'https://creativecommons.org/publicdomain/zero/1.0/\n')
    print(f'Created {len(entries)} original clips in {OUTPUT}')


if __name__ == '__main__':
    main()
