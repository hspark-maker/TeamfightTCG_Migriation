# Original Battle BGM Generators

`BrightCasualBattle`은 이 프로젝트를 위해 파형부터 직접 합성한 오리지널 64초 루프다.
외부 샘플, 프리셋, 기존 곡을 사용하지 않았다.

## 재생성

NumPy가 설치된 Python으로 WAV 마스터를 만든 뒤 FFmpeg로 OGG를 인코딩한다.

```powershell
python Tools/Audio/generate_bright_casual_battle.py Tools/Audio/Generated/BrightCasualBattle_Master.wav
ffmpeg -i Tools/Audio/Generated/BrightCasualBattle_Master.wav -af "volume=-3.0dB" -ar 44100 -c:a libvorbis -q:a 7 Assets/Assets/Audio/BGM/BrightCasualBattle.ogg
```

곡 사양: 120 BPM, G Major, 4/4, 32마디, 44.1 kHz stereo, 정확히 2,822,400 samples/channel.

### Calm Grand Battle

잔잔한 현악·하프 위에 플루트, 프렌치호른, 합창 패드와 팀파니가 점진적으로 쌓이는 오케스트라 버전이다.

```powershell
python Tools/Audio/generate_calm_grand_battle.py Tools/Audio/Generated/CalmGrandBattle_Master.wav
ffmpeg -i Tools/Audio/Generated/CalmGrandBattle_Master.wav -af "volume=-4.45dB" -ar 44100 -c:a libvorbis -q:a 7 Assets/Assets/Audio/BGM/CalmGrandBattle.ogg
```

곡 사양: 75 BPM, D Major/B Minor, 4/4, 32마디, 44.1 kHz stereo, 정확히 4,515,840 samples/channel(102.4초).

### Tactical Grand Battle

Calm Grand의 오케스트라 화성과 음색을 유지하면서 현악 오스티나토, 첼로 펄스와 절제된 프레임드럼을 더한 TCG 전투 버전이다.

```powershell
python Tools/Audio/generate_calm_grand_battle.py Tools/Audio/Generated/TacticalGrandBattle_Master.wav --profile tcg
ffmpeg -i Tools/Audio/Generated/TacticalGrandBattle_Master.wav -af "volume=-3.25dB" -ar 44100 -c:a libvorbis -q:a 7 Assets/Assets/Audio/BGM/TacticalGrandBattle.ogg
```

곡 사양: 96 BPM, D Major/B Minor, 4/4, 32마디, 44.1 kHz stereo, 정확히 3,528,000 samples/channel(80초).

## Paulyudin Battle 50초 루프 편집

사용자가 제공한 `paulyudin-battle-battle-music-491417.mp3`의 약 85.3 BPM 박자 그리드를 분석해
18마디 구간을 선택하고, 마지막 1박과 첫 1박을 등전력 크로스페이드했다.
최종 길이는 49.929705초이며 프로젝트 BGM 기준에 맞춰 약 -18 LUFS로 낮췄다.

- 원곡: `Battle - Battle Music`
- 저작자: PaulYudin
- 출처: https://pixabay.com/music/adventure-battle-battle-music-491417/
- 라이선스: Pixabay Content License — https://pixabay.com/service/license-summary/
- 다운로드·확인일: 2026-08-14 (Asia/Seoul)
- 원본 SHA-256: `EA30DCA82467B68593DE480F541006EBD85D7453F1F3E771F0CED006A0CAD1F1`
- 원본 크기: 5,191,053 bytes
- 주의: 원곡 페이지에 `Content ID Registered`가 표시되어 있다.

상세 증빙과 사용 주의사항은 `Licenses/PaulyudinBattle_Loop50.md`에 기록한다.

원본 자르기 범위는 0.943235~51.576180초다. 첫 1박 뒤부터 본문을 재생하고 마지막에
`tail → head`를 0.703235초 크로스페이드해 다음 반복의 같은 박으로 연결한다.

동일 편집본 재생성 명령:

```powershell
$sourceAudio = "C:\path\to\paulyudin-battle-battle-music-491417.mp3"
ffmpeg -y -i $sourceAudio -filter_complex "[0:a]atrim=start=1.646470:end=50.872945,asetpts=PTS-STARTPTS[main];[0:a]atrim=start=50.872945:end=51.576180,asetpts=PTS-STARTPTS[tail];[0:a]atrim=start=0.943235:end=1.646470,asetpts=PTS-STARTPTS[head];[tail][head]acrossfade=d=0.703235:c1=qsin:c2=qsin[xf];[main][xf]concat=n=2:v=0:a=1[out]" -map "[out]" -ar 44100 -c:a pcm_s24le Tools/Audio/Generated/PaulyudinBattle_Loop50_Master.wav
ffmpeg -y -i Tools/Audio/Generated/PaulyudinBattle_Loop50_Master.wav -af "volume=-5.6dB" -ar 44100 -c:a libvorbis -q:a 7 Assets/Assets/Audio/BGM/PaulyudinBattle_Loop50.ogg
```

이 파일은 편집본이며 원곡의 저작권·라이선스 조건은 바뀌지 않는다.

## 자체 합성곡 권리

이 절은 `BrightCasualBattle`, `CalmGrandBattle`, `TacticalGrandBattle` 세 자체 합성곡에만 적용된다.
작곡·합성 소스는 이 저장소에서 생성되었으며 TeamfightTCG 프로젝트에서 자유롭게 수정하고 사용할 수 있다.
각 곡의 생성 시드와 전체 악보 데이터는 `generate_bright_casual_battle.py`와
`generate_calm_grand_battle.py`에 각각 포함되어 있다.
