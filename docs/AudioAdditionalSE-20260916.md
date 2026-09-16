# 추가 SE 후보 — 2026-09-16

신규 상세 조사: Crash Smash 1,110개, Sci-Fi Sound Pack 790개, Fruit Falling SFX Pack 1 176개, Retro Sci-Fi Pack 72개, Free SFX Package 21개. 합계 2,169개 파일(포맷 변형 포함). 기존 상세 조사 22개 팩과 별도로 5개를 추가 조사했다.

기존 후보 37개와 GUID가 겹치지 않는 후보 24개를 추렸다. 음색을 직접 청취한 선정이 아니라 로컬 파일 분류·길이·PCM 데이터에 근거한 비교 후보다. 최초 탐색 단계에서는 임포트·설정 변경을 하지 않았으며, 후속 적용 결과는 아래 절에 기록했다.

[24개 미리듣기](../Build/AudioReview-20260916/ExtraSE/index.html) · [원본 경로 및 분석 JSON](../Build/AudioReview-20260916/ExtraSE/candidates.json)

## 먼저 비교할 후보

- 강타: SE01·02 (heavy/distorted), SE03 (kick heavy). 현재의 일반 Punch 계열과 비교.
- 카드 사망: SE06 (heavy slam), SE07 (stone crash). 현재 작은 자갈음의 대안.
- 짧은 충돌: SE04 (0.146초), SE05 (0.226초). 빠르게 끝나는 충격음 후보.
- UI: SE09 탭, SE10 확인, SE12·13 팝업 열기/닫기 한 쌍.
- 보상: SE15 동전, SE16 짧은 확인, SE18 완료 징글.
- 강화·진화: SE19 마나 생성, SE20 마나 폭발.

## 후보 목록

| 번호 | 용도 | 파일 | 패키지 | 길이 |
|---|---|---|---|---:|
| SE01 | 강타/처형 후보 | `punch_heavy_huge_distorted_01.wav` | Pro Sound Collection | 0.484초 |
| SE02 | 강타/처형 후보 | `punch_heavy_huge_distorted_02.wav` | Pro Sound Collection | 0.384초 |
| SE03 | 기본 타격 대안 | `kick_heavy_impact_02.wav` | Pro Sound Collection | 0.450초 |
| SE04 | 카드 착지/타격 레이어 후보 | `IMPACT_Snappy_01_mono.wav` | Universal Sound FX | 0.146초 |
| SE05 | 마법 타격 후보 | `IMPACT_Energy_Solid_01_mono.wav` | Universal Sound FX | 0.226초 |
| SE06 | deathClips 대안 | `rock_impact_heavy_slam_04.wav` | Pro Sound Collection | 1.046초 |
| SE07 | deathClips 대안 | `stone_hit_crash_164.wav` | Crash Smash | 0.679초 |
| SE08 | 파괴/팩 개봉 후보 | `wood_break_070.wav` | Crash Smash | 0.313초 |
| SE09 | TabTurn / 선택 이동 | `GlassHitScroll6.wav` | Free SFX Package | 0.163초 |
| SE10 | 확인 버튼/매칭 확정 | `HitsAccept2.wav` | Free SFX Package | 0.560초 |
| SE11 | 거절/잠금 버튼 | `DistClickBlocked1.wav` | Free SFX Package | 0.310초 |
| SE12 | PopupOpen | `UI_Animate_Bright_Whistle_Appear_stereo.wav` | Universal Sound FX | 0.236초 |
| SE13 | PopupClose | `UI_Animate_Bright_Whistle_Disappear_stereo.wav` | Universal Sound FX | 0.208초 |
| SE14 | RewardClaim / EnhanceSuccess | `UI_Notification_Bright_Keys_01_stereo.wav` | Universal Sound FX | 0.967초 |
| SE15 | CurrencyGain | `coin_bag_ring_gemstone_item_01.wav` | Pro Sound Collection | 0.328초 |
| SE16 | RewardClaim / PackSummary | `BellishAccept6.wav` | Free SFX Package | 0.459초 |
| SE17 | RankStarFill / EnhanceSuccess | `chime_bell_positive_ring_01.wav` | Pro Sound Collection | 1.000초 |
| SE18 | RankPromote / AdventureClear | `jingle_chime_11_positive.wav` | Pro Sound Collection | 1.772초 |
| SE19 | EnhanceCharge 후보 | `Arcane_Spell_Conjure_Mana_01_16_441.wav` | Magic Spell Sounds | 0.736초 |
| SE20 | EvolveBurst 후보 | `Arcane_Spell_Mana_Burst_01_16_441.wav` | Magic Spell Sounds | 0.863초 |
| SE21 | 마법 공격 후보 | `Arcane_Spell_Energy_Blast_01_16_441.wav` | Magic Spell Sounds | 0.822초 |
| SE22 | ButtonPress | `Clicks_13.wav` | Free SFX Package | 0.095초 |
| SE23 | AlbumCardSeat / 버튼 확정 | `ui_button_socket_movement_02.wav` | Pro Sound Collection | 0.269초 |
| SE24 | 턴 알림/단계 증가 후보 | `UI_Notification_Taps_01_x3_mono.wav` | Universal Sound FX | 0.386초 |

## 적용 시 구분

- 보상·매칭·도감·강화 등 기존 SoundBank 이벤트는 선택한 클립으로 교체할 수 있다.
- 강타/처형 전용 구분, 모든 팝업의 열기/닫기, 잠금 거절 전용음은 별도 호출부 확인 또는 추가가 필요하다. 후보 이름만으로 이미 구현됐다고 보지 않는다.
- heavy/distorted는 제작사가 붙인 파일명이다. 왜곡된 질감이 기본 공격에 적합한지는 청취 후 판단한다.
- Sci-Fi·Retro·과일 낙하 팩은 실제 파일 목록을 조사했지만 이번 24개 우선 후보에는 포함하지 않았다. 현재 목적에 직접 대응하는 충돌·파괴·확인·알림 후보를 우선했다.
- 기존 게임의 SFX 85%, Punch 31·32, Whoosh 3 배정은 유지했다.

## 후속 적용 — “추가해서 한번 보자”

24개 전부 `Assets/Assets/Audio/Candidates/SE01_...wav` ~ `SE24_...wav`로 추가했다. 기존 후보와 합쳐 총 61개다. 이 중 새 음원 19개를 공용 슬롯 4곳과 이벤트 슬롯 18곳에 배정했다. 별도 코드는 추가하지 않았다.

| 적용 위치 | 새 후보 |
|---|---|
| 공용 클릭 / ButtonPress / AdventureNodeTap | SE22 Clicks_13 |
| 공용 타격 | SE01·SE02 heavy huge distorted 랜덤 |
| 카드 사망 | SE06 heavy rock slam·SE07 stone crash 랜덤 |
| 턴 변경 | SE24 세 번 탭 알림 |
| TabTurn | SE09 GlassHitScroll6 |
| PopupOpen / PopupClose | SE12 / SE13 열기·닫기 쌍 |
| PackSummary / RewardClaim | SE16 BellishAccept6 |
| EnhanceCharge | SE19 Conjure Mana |
| EnhanceSuccess | SE17 positive bell |
| EnhanceFail | SE11 blocked click |
| EvolveBurst | SE20 Mana Burst |
| AlbumCardSeat | SE23 socket movement |
| AlbumFanfare / RankPromote / AdventureClear | SE18 positive jingle |
| CurrencyGain | SE15 coin bag / gemstone |
| RankStarFill | SE14 bright keys |
| MatchFound | SE10 HitsAccept2 |

전체 SFX 볼륨은 0.85, 이벤트별 배율은 이전 값을 유지했다. 로비·전투 BGM, 실제 카드 뽑기/팩 찢기/도감 넘김, 공격 진입 Whoosh 3은 그대로다. SE03·04·05·08·21은 프로젝트에서 비교할 수 있는 예비 후보로 두었다.

`PopupClose`는 기존 호출부가 없어 배정만 됐으며 실제 재생은 되지 않는다. `PopupOpen`도 콘텐츠 해금 연출의 기존 호출 범위에만 적용된다. 강타용 별도 판정은 추가하지 않았으므로 SE01·02는 공용 타격 전체에 적용되는 시험 배정이다.

### 가져오기와 검증

- 24개 음원 모두 원본 WAV 바이트와 GUID를 보존했다.
- 19개는 원본 `AudioImporter` 메타데이터를 그대로 사용했다.
- Free SFX Package의 SE09·10·11·16·22는 Unity 3.5.6의 바이너리 `metaData`만 있었다. 원본 GUID와 음원은 유지하고, 프로젝트에서 확인한 Unity 6 `AudioImporter` 형식으로 메타데이터를 새로 저작했다. 짧은 효과음용 데이터 미리 읽기를 켰으며 옛 바이너리는 로컬 조사 폴더에 보관했다. 이 5개는 과거 임포트 설정의 완전한 보존으로 보지 않는다.
- 검증: 61개 음원 SHA-256·GUID·단일 폴더 위치, 추가 24개 WAV의 전체 PCM 길이, 메타데이터, 공용 6개/이벤트 24개 슬롯의 배정과 배율, 두 씬의 해시 보존, `git diff --check` 통과.
- 적용 전 설정은 로컬 `Build/AudioReview-20260916/ExtraSE/before-apply.json`, 현재 배정은 `applied-wiring.json`, 검증 결과는 `applied-validation.json`에 저장했다.
- Unity MCP 권한 해제로 에디터 내부 재생·기기 청취 검증은 하지 못했다. 빌드·업로드도 하지 않았다. 플레이를 다시 시작해 새 배정을 비교할 단계다.

## 제작사 자료

- [Gamemaster Audio — Pro Sound Collection](https://www.gamemasteraudio.com/product/pro-sound-collection/): 전투·UI·수집·마법·후시 계열을 포함하는 종합 라이브러리 설명을 참고했다. 집계는 웹의 최신 수치가 아니라 로컬 패키지 기준이다.
- [SoundBits — Crash & Smash](https://soundbits.de/product/crash-smash/): 충돌뿐 아니라 파편·붕괴를 포함하는 파괴음 라이브러리라는 제작사 설명을 참고했다.
