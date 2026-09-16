# 다운로드 사운드 후보 조사 — 2026-09-16

## 결론

우선 도입 후보는 **Playing Cards Sounds(카드) + Pro Sound Collection(UI 클릭) + Twilight Dream(보상·성장) + Punch and Fighting Sounds(타격)** 조합이다. 팩 찢기와 도감 넘김은 Universal Sound FX로 보완한다.

캐시의 80개 unitypackage 중 게임 이벤트와 관련된 22개 패키지를 상세 조사했다. 내부 오디오 파일은 23,265개이며, WAV/OGG 및 모노/스테레오 등 변형 중복을 포함한다. 고유 사운드 개수나 전체 캐시의 오디오 총수는 아니다.

**직접 청취한 최종 선정이 아니다.** 패키지 내부 실제 파일 경로·분류·WAV 길이·일부 PCM 피크와 게임의 호출 지점을 대조한 1차 후보다. 음색의 일관성, 피로도, 루프 경계, 로비 BGM과의 조화는 청취 및 인게임 확인이 필요하다.

## 미리듣기

- [34개 후보 미리듣기](../Build/AudioReview-20260916/index.html): 브라우저에서 열고 분류별 비교. 자동 재생 없음, 초기 음량 35%, 한 번에 한 클립만 재생.
- [상세 후보 JSON](../Build/AudioReview-20260916/shortlist.json): 원본 패키지·내부 경로·GUID·길이·샘플 형식·SHA-256.
- [22개 패키지 전체 조사 목록](../Build/AudioReview-20260916/inventory.json).
- 미리듣기 원본 34개(약 30.2 MiB)는 `Build/AudioReview-20260916/clips/`에만 추출했다. Build 폴더의 로컬 산출물이므로 다른 컴퓨터에는 자동 전달되지 않는다.

## 용도별 우선 후보

| 용도 | 패키지 / 파일 | 길이 | 연결 지점 / 판단 |
|---|---|---:|---|
| 버튼 클릭 | Pro Sound Collection / `ui_button_simple_click_01.wav`, `02.wav` | 0.097 / 0.087초 | `ButtonPress`, `uiClickClips`. 잦은 클릭에 짧은 파일 우선 |
| 탭 이동 | UI Item Sound Effect Jingles / `Menu MoveV2.wav` | 0.466초 | `TabTurn` |
| 카드 드로우 | Playing Cards Sounds / `pulling_card_from_top_of_deck_fast/001.wav`, `002.wav` | 0.436 / 0.314초 | `dealCardClips`. 덱에서 뽑는 동작과 분류가 일치 |
| 카드 배치·팩 카드 넘김 | Playing Cards Sounds / `card_throw_normal/001.wav`, `002.wav` | 0.239 / 0.214초 | `PackCardFlick`, `AlbumCardSeat` |
| 팩 개봉 시작 | Playing Cards Sounds / `deck_box_open/002.wav` | 0.496초 | `PackOpenBegin` |
| 팩 찢기 | Universal Sound FX / `PAPER_Tear_Fast_mono.wav`, `PAPER_Rip_01_mono.wav` | 0.590 / 0.327초 | `PackTear`. 포장 재질과 애니메이션 길이 확인 |
| 도감 넘김 | Universal Sound FX / `BOOK_Turn_Page_02_mono.wav` | 0.782초 | `AlbumPageTurn` |
| 보상 수령·강화 성공 | Twilight Dream / `TD_Positive_Notification_01_01.wav` | 0.865초 | `RewardClaim`, `EnhanceSuccess` |
| 랭크 별 채움 | Twilight Dream / `TD_Collect_Star_01.wav` | 0.640초 | `RankStarFill` |
| 강화 실패 | Twilight Dream / `TD_Negative_Sting_04_01.wav` | 0.669초 | `EnhanceFail` |
| 골드 획득 | Fantasy Loot Audio Pack / `DropCoin01 - Mono.wav` | 0.625초 | `CurrencyGain` |
| 작은 보상 대안 | UI Item Sound Effect Jingles / `Item4B.wav` | 0.351초 | `CurrencyGain` 또는 `RewardClaim` 비교 |
| 기본 타격 | Punch and Fighting Sounds / `Simple Punch 5.wav`, `7.wav` | 0.810 / 0.750초 | `hitClips` |
| 공격 진입 | Punch and Fighting Sounds / `Whoosh 14.wav` | 1.000초 | `cinemaEnterClips` |
| 카드 사망 | Organic Stone Sounds / `Small Pebbles Drop Short.wav` | 0.920초 | `deathClips`. 파편 표현과 맞는지 청취 후 결정 |
| 강화 차지 | Pro Sound Collection / `casting_charge_whoosh_buildup4.wav` | 0.640초 | `EnhanceCharge`. 전체 차지 시간과 별도 대조 |
| 승급·모험 클리어 | Twilight Dream / `TD_Win_Stinger_02.wav` | 3.588초 | `RankPromote`, `AdventureClear`. 큰 결과 연출 전용 후보 |
| 전투 BGM | Battle RPG Music Pack / `BRPG_Take_Courage_Lite_Loop.wav` | 64.043초 | 우선 청취 후보. 로비 BGM은 기존 유지 |
| 전투 BGM 대안 | Battle RPG Music Pack / `BRPG_Assault_noGT_Loop.wav` | 72.000초 | 기존 전투 BGM 슬롯에 배정 가능, 루프·효과음 간섭 검증 필요 |

## 적용 범위와 추가 작업

- 최초 조사 시 `Assets/SO/OutgameSoundBank.asset`는 24개 이벤트의 `clips`가 모두 비어 있었다. 버튼·탭·팩·강화·도감·보상·랭크·모험·매칭은 기존 이벤트를 활용할 수 있다. 호출 지점은 `Assets/Scripts/Audio/EOutgameSound.cs` 및 각 UI에 존재한다. 후속 게임 적용 상태는 아래 절을 따른다.
- 전투 공용 슬롯은 `SoundConfig`의 `hitClips`, `deathClips`, `dealCardClips`, `turnChangeClips`, `cinemaEnterClips`다. 최종 후보 선택 후 배선과 볼륨 조절이 필요하다.
- `PopupClose`는 enum에 존재하지만 실제 호출 여부를 별도 확인해야 한다. 후보가 있다고 모든 이벤트가 자동 재생되는 것은 아니다.
- 힐러 전용 `Healing_Spell_Mend_02_16_441.wav`(1.207초), 전기·불·바람 충돌(약 0.86~0.96초)을 미리듣기에 포함했다. 카드·효과별 훅을 확인/추가한 뒤 연결할 후보이며, 공용 타격 랜덤 목록에 섞지 않는다.
- 승리·패배 징글도 포함했다. `EOutgameSound`에 별도 전투 승패 항목이 없어 결과 연출 배선을 확인해야 한다.
- 3~4초 징글은 반복되는 재화 획득보다 승급·완료·전투 결과에 우선 배정한다. 긴 잔향과 중복 재생은 인게임에서 확인한다.

## 보류 및 기술 확인

- 34개 추출 파일 모두 WAV 헤더와 전체 PCM 데이터 길이 검증을 통과했다. 원본 바이트를 그대로 추출했으며 SHA-256을 기록했다. 원본 파일의 리샘플링·볼륨 변경·자르기는 하지 않았다.
- 미리듣기 19번 `DropGold08 - Mono.wav`는 피크가 0 dBFS이고 최대치 근접 샘플이 약 0.1505%다. 이것만으로 왜곡을 확정하지 않지만, 청취 전 우선 후보에서 보류하고 18번 동전음을 먼저 비교한다.
- 피크 분석은 16-bit PCM 후보에 한정했다. 24-bit 마법 후보 3개는 구조·길이만 검증했으며 같은 피크 검사를 했다고 보지 않는다.
- Roguelike Sound Pack의 일부 메뉴 파일은 길이가 약 14.77초다. 현재 게임의 빈번한 버튼·탭에 쓰는 우선 후보에서는 제외했다. 무음/잔향 여부는 확인하지 않았다.
- 전체 80개 중 총기·호러·음성·자연 환경음 및 여러 음악 패키지는 이번 상세 조사 대상에서 제외했다. 필요하면 특정 연출에 맞춰 후속 검토할 수 있다.
- 로컬 캐시는 구매 계정이나 현재 사용 권한의 증거로 삼지 않았다. 이번 조사로 `mobile@cookapps.com`의 온라인 보유 목록을 확인한 것은 아니다.

## 조사 범위 보존

게임 코드·프리팹·사운드 설정·기존 로비 BGM을 변경하지 않았다. Unity 임포트, 빌드, 업로드도 실행하지 않았다. 이번 결과는 신규 사운드 후보와 로컬 비교 자료다.

## 참고한 제작사 설명

- [Fantasy Loot Audio Pack 제작사 소개](https://discussions.unity.com/t/fantasy-loot-audio-pack/615907): 아이템 획득·UI·개폐·마법 등 용도 설명 참고. 이 게시글의 과거 파일 수 대신 로컬 패키지의 실제 목록으로 집계했다.
- [UI & Item Sound Effect Jingles 제작사 소개](https://discussions.unity.com/t/update-ui-item-sound-effect-jingles-11-new-sounds/563058): 메뉴와 아이템 수집용 징글 의도 참고. 실제 후보 길이는 로컬 WAV에서 확인했다.

## 후속 요청: Unity 프로젝트에 후보 추가

사용자의 임포트 요청으로 위 후보 34개(약 30.2 MiB)를 현재 프로젝트에 추가한 뒤, 후속 정리 요청으로 **`Assets/Assets/Audio/Candidates/` 한 폴더에 모았다.** 파일명 앞에 미리듣기 번호(01~34)를 붙이고 숫자로만 된 카드 파일에는 동작 이름도 붙여 구분했다. 원본 패키지 경로는 목록의 `original_path`에 기록했다. 각 파일의 원본 `AudioImporter` 메타데이터와 GUID를 보존했으며, 이동 전후 오디오·메타데이터 SHA-256이 모두 같음을 검증했다.

- [추가 파일 목록](../Build/AudioReview-20260916/import-manifest.json)
- 통합 폴더: [Assets/Assets/Audio/Candidates](../Assets/Assets/Audio/Candidates/)
- 통합 후 사용자 요청으로 원래 위치의 빈 폴더 35개와 해당 `.meta`를 정리했다. 폴더가 비어 있는지 각각 확인하고 비재귀 방식으로 삭제했으며, 후보 음원 34개는 보존했다.

전체 패키지의 스크립트나 데모를 임포트하지 않고 선택한 오디오와 메타데이터만 추가했다. `SoundConfig.asset`, `OutgameSoundBank.asset`, 기존 `LobbyBGM.mp3`는 추가 전후 해시가 동일하다. 후보 19번의 보류 판단도 유지한다.

현재 Unity MCP는 `Connection revoked`를 반환하므로 에디터 내부 `AudioClip` 로드 확인은 수행하지 못했다. 파일 배치는 완료했으며 Unity의 자동 새로고침(필요 시 Assets > Refresh) 후 확인할 수 있다. 게임 재생 배선·빌드·배포는 실행하지 않았다.

## 후속 요청: 게임에 1차 적용

사용자의 “일단 게임에 넣어봐” 요청으로 기존 사운드 시스템에 연결했다. 위 조사·임포트 단계의 미배선 상태를 이 절의 적용 상태로 갱신한다. 코드 추가 없이 `SoundConfig.asset`, `OutgameSoundBank.asset`, `BattleScene.unity` 세 파일의 저작값만 변경했다.

| 연결 지점 | 미리듣기 번호 | 공통 SFX 대비 배율 |
|---|---|---:|
| 공용 UI 클릭 / ButtonPress | 01 | 1 |
| 타격 | 21, 22 랜덤 | 1 |
| 카드 사망 | 24 | 1 |
| 카드 드로우 | 06, 07 랜덤 | 1 |
| 턴 변경 | 05 | 1 |
| 공격 진입 | 23 | 1 |
| TabTurn | 04 | 0.65 |
| PopupOpen / PopupClose | 05 / 04 | 0.65 / 0.5 |
| PackOpenBegin / PackTear | 10 / 11 | 0.85 / 0.85 |
| PackCardFlick / PackSummary | 08·09 랜덤 / 14 | 0.65 / 0.85 |
| EnhanceCharge / EnhanceSuccess / EnhanceFail | 29 / 14 / 16 | 0.55 / 0.9 / 0.75 |
| EvolveBurst | 17 | 0.85 |
| AlbumPageTurn / AlbumCardSeat / AlbumFanfare | 13 / 08·09 랜덤 / 17 | 0.55 / 0.55 / 0.8 |
| RewardClaim / CurrencyGain | 14 / 18 | 0.8 / 0.45 |
| RankStarFill / RankPromote | 15 / 17 | 0.9 / 0.9 |
| AdventureNodeTap / AdventureClear | 01 / 17 | 0.8 / 0.8 |
| MatchSearch / MatchFound / MatchVersus | 29 / 14 / 23 | 0.4 / 0.8 / 0.75 |
| 전투 BGM | 33 Take Courage Lite Loop | 기존 BGM 설정 사용 |

- 공통 `sfxVolume`은 0.45. 실제 효과음 크기는 사용자 SFX 설정 × 0.45 × 이벤트별 배율이다. 로비 씬과 기존 로비 BGM은 변경하지 않았다.
- 고유 음원 21개를 연결했다. 보류한 19번은 사용하지 않았다. 회복·속성별 전용 효과음과 승패 징글 후보는 이번에 별도 훅을 추가하지 않았다.
- `PopupClose`는 사운드 표에만 배정됐으며 호출부가 없어 아직 재생되지 않는다. `PopupOpen`은 현재 콘텐츠 해금 연출의 기존 호출에 적용되며 모든 팝업에 자동 적용되는 것은 아니다.
- 검증: 공용 6개 슬롯·이벤트 24칸의 음원 순서와 배율, 전투 BGM 참조, 음원 SHA-256·GUID·AudioImporter 존재, Initialize 프리팹의 설정 참조, 로비 씬 해시 보존, `git diff --check`를 확인했다.
- Unity MCP 권한이 해제된 상태여서 에디터 재생 및 기기 청취 검증은 하지 못했다. 빌드·업로드도 실행하지 않았다. 실제 볼륨·잔향·반복 피로도는 플레이하면서 조정할 1차 배정이다.

## 2차 조정: 효과음 음량·타격 강화

사용자 피드백: “효과음 더 커야하고, 지금 효과음은 너무 약함. 좀 더 타격감 있는 사운드 필요.”

- `SoundConfig.sfxVolume`: **0.45 → 0.85**. 동일 클립·사용자 설정 기준 출력 게인은 약 +5.52 dB 증가한다. 체감 음량이 두 배라는 뜻은 아니다. 사용자 SFX 설정은 유지한다.
- `hitClips`: 기존 `Simple Punch 5·7`을 **`35_Punch 31.wav`, `36_Punch 32.wav`**로 교체했다.
- `cinemaEnterClips` 및 `MatchVersus`: 기존 `Whoosh 14`를 **`37_Whoosh 3.wav`**로 교체했다. 공격 진입과 대치 진입에 동일한 새 후보를 쓴다.
- 새 원본 3개는 모두 `Assets/Assets/Audio/Candidates/`에 추가했다. 폴더 안 음원은 총 37개이며, 기존 후보를 덮어쓰거나 원본 파형을 가공하지 않았다. 패키지의 GUID·임포트 설정을 보존했다.
- 이벤트별 배율, 로비 BGM 및 전투 BGM 배정은 그대로다. 전체 효과음은 높아지지만 재화·도감 등 이벤트별 상대 음량 구분은 유지된다.

선택 근거는 로컬 WAV 샘플 분석이다. 아래 초반 RMS는 피크의 2% 또는 PCM 100 중 큰 임계값을 처음 넘은 지점부터 150ms 동안 측정했다. 이 측정은 음색 청취나 체감 타격감 검증을 대체하지 않는다.

| 타격음 | 길이 | 신호 시작 | 초반 150ms RMS |
|---|---:|---:|---:|
| 이전 Simple Punch 5 | 0.810초 | 약 41.75ms | -16.30 dBFS |
| 이전 Simple Punch 7 | 0.750초 | 약 24.74ms | -11.13 dBFS |
| 새 Punch 31 | 1.375초 | 약 1.50ms | -12.08 dBFS |
| 새 Punch 32 | 1.500초 | 약 15.17ms | -11.52 dBFS |

두 타격 변주의 초반 RMS 차이는 약 5.17 dB에서 0.56 dB로 줄었다. 기존에 특히 작았던 변주를 제외하고, 빠르게 시작하며 초반 음량이 일정한 후보로 맞췄다. 새 Whoosh 3은 기존 Whoosh 14보다 원본 피크가 약 3.11 dB 높고 신호 시작도 약 59.77ms에서 13.74ms로 빨라졌다.

검증: 음원 37개의 원본 SHA-256·GUID, 공용 6개 슬롯·이벤트 24개 배정, 옛 타격/진입음과 보류한 골드음의 미사용, 로비 씬 및 전투 BGM 보존, `git diff --check` 확인. 실제 플레이 청취·빌드·업로드는 수행하지 않았다.

## 3차 적용: 추가 SE 후보 연결

후속 요청으로 SE 후보 24개를 더 추가해 `Candidates` 폴더는 총 61개가 됐다. 새 후보 19개를 타격·사망·UI·턴 알림·보상·강화 등에 배정했다. 전체 SFX 0.85와 BGM은 유지했다. **현재 배정과 Unity 3.5 메타데이터 5개의 변환 내용은 [추가 SE 적용 기록](AudioAdditionalSE-20260916.md#후속-적용--추가해서-한번-보자)을 따른다.** 위 1·2차 배정은 변경 이력이다.
