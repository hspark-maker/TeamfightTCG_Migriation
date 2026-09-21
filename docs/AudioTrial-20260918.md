# 대체 사운드 시험 적용 — 2026-09-18

사용자 요청으로 캐시에서 고른 새 후보 20개 중 14개를 적용했다. 원본 음원과 GUID를 보존하고 `Assets/Assets/Audio/Candidates/ALTxx_...`에 추가했다. 짧은 효과음은 미리 읽기, BGM은 스트리밍으로 설정했다.

| 적용 위치 | 미리듣기 번호 / 파일 |
|---|---|
| 버튼·모험 정점 클릭 | 03 / DM-CGS-20 |
| 탭 이동·팝업 닫기 | 02 / DM-CGS-16 |
| 팝업 열기·도감 카드 배치 | 01 / DM-CGS-03 |
| 공용 타격 | CardImpact_PopPunch (0.180초) |
| 카드 사망 | 09 / BrokenShield |
| 턴 전환·매칭 확정 | 12 / Menu3B |
| 팩 결과·보상 수령·도감 완성·승급·모험 완료 | 10 / Item5B |
| 강화 성공·랭크 별 채움 | 11 / Item5D |
| 재화 획득 | 13 / DropGems06 - Mono |
| 강화 차지 | 15 / Bravery 1 (3.456초) |
| 진화 공개 | 16 / Shield 1 (1.656초) |
| 로비 BGM | 교체 전 LobbyBGM 복구 |
| 전투 BGM | 교체 전 33_BRPG_Take_Courage_Lite_Loop 복구 |

공용/BGM 슬롯 6곳과 이벤트 슬롯 17곳을 교체했다. 전체 SFX 0.85와 이벤트별 볼륨 배율은 유지했다. 기존 카드 드로우·팩 찢기·도감 넘김·공격 진입·강화 실패 등 나머지 배정은 유지했다. 기존 호출부를 사용하므로 PopupClose 등 호출되지 않던 이벤트는 새로 활성화되지 않는다.

사용하는 설정 2개와 클립 24개에 맞춰 RemoteAudio 그룹을 갱신했다. 이전 음원은 파일을 보존하고 미사용 원격 등록만 제거했다. Unity AudioClip 임포트, 14개 원본 SHA-256·GUID·배정, 볼륨 보존, RemoteAudioBuildValidation을 확인했다. 실제 인게임 청취와 음색·연출 타이밍 평가는 아직 하지 않았다. 특히 차지음은 3.456초이므로 다음 결과음과 겹치는 정도를 플레이에서 확인할 필요가 있다.

미리듣기와 적용 전 설정 백업은 `C:/Users/cookapps/Desktop/AudioReview-20260918/`에 있다. `before-apply/`는 SoundConfig·OutgameSoundBank·RemoteAudio·AddressableAssetSettings 원본, `applied-imports.json`과 `applied-validation.json`은 적용 파일과 검증 기록이다.

프로젝트 적용까지 완료했다. 리소스 빌드·배포와 APK 빌드는 수행하지 않았으며 기존 설치 앱의 원격 리소스는 바뀌지 않았다.

후속 요청으로 검격 04·05를 공용 타격에서 제외하고 적용 전 펀치 타격음 2개로 복구했다. 검격의 미사용 RemoteAudio 등록도 제거하고 기존 타격음을 다시 등록했다. 다른 시험 배정은 유지했으며 RemoteAudioBuildValidation을 다시 통과했다. 위의 14개·6곳은 최초 적용 기록이며 현재 새 후보 사용은 12개·공용/BGM 변경은 5곳이다.

추가 요청으로 공용 타격을 Universal Sound FX의 `SE04_IMPACT_Snappy_01_mono.wav` 한 개로 교체했다. 0.146초의 짧은 충격음이며 검격·기존 펀치 랜덤 배정은 제거했다. RemoteAudio 사용 목록을 동기화하고 검증을 통과했다. 위의 펀치 복구와 5곳 집계는 중간 기록이며 현재 공용/BGM 변경은 다시 6곳이다.

## 하스스톤풍 타격 요청 후 현재 적용

공용 타격을 `CardImpact_Weighted_01.wav`·`02.wav` 두 가지로 교체했다. 하스스톤의 원본 음원은 사용하지 않았다. Universal Sound FX 캐시의 `THUD_Dark_03_Short`·`THUD_Medium_01`을 저음 바탕으로, `IMPACT_Stone_Deep`과 기존 `IMPACT_Snappy_01`을 겹쳐 제작했다. 저음 바탕의 피치를 0.92/0.96으로 낮추고 고음을 줄였으며, 시작 무음을 정리하고 65ms 끝 페이드를 넣었다. 길이 0.38/0.36초, 피크 -2.85dBFS, RMS 약 -20.1dBFS로 파일 자체의 클리핑이 없음을 확인했다.

[제작진 인터뷰](https://hearthstone.blizzard.com/en-us/news/23964694)의 카드 동작 타이밍과 충돌 순간 강조를 참고한 시험 디자인이다. 하스스톤과 음색이 같다는 청취 검증은 하지 않았다. Unity 임포트·공용 타격 배정·RemoteAudio 검증은 통과했다. 나머지 사운드 배정은 유지했다.

재현 스크립트·캐시 원본 경로·해시·합성 설정은 `Build/CardImpactTrial-20260918/`에 보관했다. 신규 합성 파일은 새 GUID를 사용한다. 이 변경도 리소스 배포 전 프로젝트 적용 상태다.

## BGM 복구·타격음 가청성 보정

사용자 요청으로 BGM 두 슬롯을 최초 시험 적용 전 백업의 GUID로 복구했다. 로비는 `LobbyBGM`, 전투는 `33_BRPG_Take_Courage_Lite_Loop`이다.

타격음이 안 들린다는 보고를 조사했다. `CardView.PlayHitAnim → CardAnimator.PlayHitAnim → SoundManager.PlayHit` 호출과 설정 배정은 존재했고, Unity가 읽은 두 클립은 Loaded 상태이며 실제 PCM 신호도 있었다. 확인 당시 에디터는 Play 중이 아니어서 사용자가 들은 상황의 재생·설정·원격 버전을 직접 재현하지는 못했다.

첫 합성본은 평균 -20.2/-20.1dBFS로 이전 펀치(-10.1dBFS)보다 작았고, 600Hz 위 성분도 약 -30.8dBFS로 약했다. BGM에 묻힐 수 있는 요인으로 보고 저음 비중을 줄이고 돌 충돌·타격 시작음의 중고음을 복원했다. 피크 압축 후 음량을 보정해 평균 -15.6/-16.8dBFS, 600Hz 위 성분 -25.0/-26.0dBFS가 됐다. 기존 GUID와 길이는 유지했다.

Unity 재임포트 후 실제 디코딩 PCM은 평균 -15.53/-16.79dBFS, 피크 0.896/0.881로 클리핑 없이 확인했다. BGM 참조 및 RemoteAudio 검증도 통과했다. 청취 환경에서 무음 원인이 해소됐다는 확인까지 완료한 것은 아니다. 리소스 빌드·배포는 하지 않았다.

## 타격음 타이밍 보정

사용자가 소리가 약간 늦다고 보고했다. 피격 재생 호출 전에 비동기 대기는 없었으나, 합성 음원의 가장 강한 5ms 구간이 시작 90~95ms 뒤에 있었다. 에디터 출력 버퍼는 48kHz에서 1024×4 샘플(버퍼 총량 약 85ms)이었다. 이 수치는 기기 전체 출력 지연을 측정한 값은 아니다.

강한 타격 직전까지 약 85~90ms를 잘라내고 시작 페이드와 피크 압축을 보정했다. Unity 디코딩 후 두 클립의 가장 강한 구간은 약 5ms, 평균 -16.16/-16.61dBFS, 피크 0.900/0.895로 확인했다. GUID·기존 타격 호출 시점·공격 규칙은 유지했다.

ProjectSettings/AudioManager.asset의 DSP Buffer Size를 Best latency(256)로 저장했다. 실제 에디터 설정도 256×4(버퍼 총량 약 21ms)로 확인했다. [Unity 출력 버퍼 설명](https://docs.unity3d.com/ScriptReference/AudioSettings.GetDSPBufferSize.html)을 참고했다. RemoteAudio 검증 통과. 실기기 동기화·오디오 끊김 여부는 미검증이며, 이 프로젝트 설정은 앱 빌드에 반영해야 기기에 적용된다.

## 타격음 크기·충격 강화

후속 요청으로 타격음 첫 80ms를 최대 1.4배 강조하고, 피크 압축과 출력 레벨을 조정했다. 파일 평균은 -13.80/-14.98dBFS로 이전보다 약 2.4/1.7dB 증가했고 피크는 -0.72dBFS다. 길이·GUID·타격 시작 위치와 BGM·다른 효과음 설정은 유지했다. Unity에서 디코딩 후 클리핑 없음, 강한 타격 구간 20ms 이내, RemoteAudio 참조 검증을 확인했다. 이전 음원은 `Build/CardImpactTrial-20260918/before-impact-boost/`에 보관했다. 실제 플레이 청취 및 원격 배포는 아직 수행하지 않았다.

## 둔탁한 합성음 제외 후 현재 배정

사용자가 둔탁한 음색을 지적해 저음 합성 2개를 공용 타격에서 제외하고, 캐시의 `Universal Sound FX/IMPACTS/Snappy/IMPACT_Snappy_02_mono.wav`로 교체했다. 이전에 사용했던 Snappy_01과는 다른 파일이다. 원본 바이트와 GUID를 보존했다. 길이 약 0.187초, 원본 평균 -15.88dBFS, 가장 강한 5ms 구간은 시작 약 5ms다. 추가 저음 레이어나 피치 변경은 적용하지 않았다.

Unity 임포트·디코딩 신호·클리핑 없음·RemoteAudio 검증을 확인했다. 기존 합성 파일은 보관하고 미사용 원격 등록만 제거했다. BGM과 다른 효과음, 저지연 버퍼 설정은 유지했다. 실제 인게임 청취는 미검증이며 원격 배포는 하지 않았다. 원본 경로·해시는 `Build/CardImpactTrial-20260918/snappy-replacement.json`에 있다.

## “팡” 터지는 음색 요청 후 현재 배정

공용 타격을 `Universal Sound FX/FOLEY/BALLOONS/BALLOON_Pop_Real_02_mono.wav`로 교체했다. 짧은 풍선 파열 원본이며 길이 약 0.150초, 강한 타격 구간 약 5ms다. 원본 바이트·GUID를 보존했고 추가 저음 합성은 하지 않았다. Unity 디코딩 클리핑 없음·초기 타격 구간·RemoteAudio 검증을 통과했다. BGM·기타 효과음·저지연 출력 설정은 유지했다. 원본 경로와 SHA-256은 `Build/CardImpactTrial-20260918/pop-replacement.json`에 기록했다. 인게임 청취와 배포는 아직 수행하지 않았다.

## 팝 타격 충격 강화 후 현재 배정

팝 단독의 충격이 약하다는 피드백으로 `CardImpact_PopPunch.wav`를 제작해 연결했다. 풍선 팝(1.0)·Snappy_02(0.6)·THUD_Bright_02(0.25)의 시작점을 맞춰 겹치고 140Hz 아래를 줄였다. 0.18초 안에 끝내며 피크 압축 후 평균 음량은 -12.5dBFS로 팝 단독(-19.06dBFS)보다 약 6.6dB 높였다.

Unity 디코딩 평균 -12.51dBFS·피크 0.819·강한 구간 약 5ms·RemoteAudio 등록 검증 통과. 기존 BGM·나머지 효과음·저지연 설정은 유지했다. 제작 스크립트와 출처·설정·해시는 `Build/CardImpactTrial-20260918/make_pop_impact.py`, `pop-impact-manifest.json`에 보관했다. 실기기 청취와 리소스 배포는 수행하지 않았다.
