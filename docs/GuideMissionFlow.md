# FTUE 이후 가이드 흐름

## 저작

`OutgameTutorial.asset`은 FTUE와 가이드 미션 두 축으로 저작한다.

- `ftueChapters`: 첫 시작에 순차 실행하는 FTUE. 현재 4챕터이며 저장 진행 좌표와 졸업 경계를 소유한다.
- `guide` (`GuideTutorialData`): 시너지 설명·미션 연결·`guideChapters`를 소유한다. 현재 안내 3챕터이며 미션 흐름 또는 기존 콘텐츠 진입으로 실행하고 안내별 완료 기록을 남긴다.

챕터 성격은 소속 목록에서 결정하며 별도의 `kind` 값으로 저작하지 않는다. `Chapters`는 기존 논리 좌표(FTUE 다음 가이드)를 보존하는 읽기 전용 뷰이며 별도 사본을 저장하지 않는다.

Inspector는 FTUE 목록과 가이드 설정을 별도로 보여준다. 전용 편집창은 두 탭에서 각각 편집한다. 전용 편집창의 챕터 순서 이동은 같은 성격 안에서만 가능하며, 스텝 추가·복제는 기존 ID 발급 연산을 사용한다. 가이드 탭에는 별도 해금 에셋을 여는 버튼이 있다.

`Assets/SO/ContentUnlockConfig.asset` (`ContentUnlockData`)이 콘텐츠별 해금 조건과 이름·설명·아이콘을 소유한다. `RuntimeContentCatalog.contentUnlockData`가 직접 참조하고 `OutgameConfigStep`이 `ContentUnlockConfig`에 주입한다. 튜토리얼 에셋에는 해금 설정이나 그 사본을 저장하지 않는다.

`OutgameTutorial.asset`의 `guide.guideFlows`에서 미션 ID, 해금 소개 목록, 목적지, 자율 챕터를 연결한다.

미션 연결과 안내 챕터는 비어 있어도 된다. 이 경우에도 독립 해금 에셋의 `contentUnlocks` 조건과 `contentIntros` 소개를 사용한다. 조건의 `guideMissionId`가 비어 있으면 미션 서버 데이터가 필요 없다. 명시적으로 미션 ID를 연결한 콘텐츠는 해당 미션 도달을 기다린다. 가이드 흐름의 `missionId`는 안내 시작 조건이며 콘텐츠 이용 자격을 바꾸지 않는다.

| 미션 | 해금 소개 | 목적지 | 기존 완료 키 |
|---|---|---|---|
| 빈 값: FTUE 졸업 | 미션 | 없음 | 없음 |
| guide.01 | 카드 강화 | 컬렉션 | CollectionTabFirstEnter |
| guide.03 | 모험 | 모험 맵 | AdventureUnlocked |
| guide.06 | 룰렛 | 없음 | 없음 |

`contentUnlocks`는 FTUE·랭크·계정 레벨·가이드 미션 조건을 AND로 평가한다. 현재 카드 강화는 `guide.01`, 모험은 `guide.03`, 룰렛은 `guide.06` 도달을 요구한다. 미션은 FTUE 완료만 요구한다. 룰렛은 첫 무료 시너지 성장 안내를 마치고 돌보미 카드 3장을 2성으로 키우는 구간에 소개한다. 잔액 부족을 실시간 판정하는 조건은 아니며, 이미 해금된 계정의 이용 자격은 유지한다. 안내 순서나 챕터의 첫 스텝으로 이용 자격을 추론하지 않는다.

`tutorial`은 저장 호환을 위한 안내 식별자다. 기존 enum 이름과 값은 유지하되 연결된 안내를 탭 진입 사건으로 시작하지 않는다. 신규 스텝 ID는 기존 에디터 발급 절차를 사용한다.

## 책임

- `GuideMissionProgress`: 서버 미션 정의와 수령 상태에서 현재 미션·도달 여부를 판정한다. 조회 준비 전에는 판정하지 않는다.
- `GuideMissionFlows`: SO 연결을 조회하고 현재 미션의 안내 자격을 판단한다.
- `ContentUnlockManager`: 미션 변화에 해금을 재평가하고 기존 영구 해금·소개 대기 기록을 유지한다.
- `ContentUnlockData` / `ContentUnlockConfig`: 독립 해금 저작물과 초기화된 조회 창구. 소개 화면도 이 경로로 콘텐츠 표현을 조회한다.
- `GuidanceCoordinator.Missions`: 소개 → 이동 → 온보딩을 하나씩 실행한다. 미션 이동 버튼도 이 경로로 재개한다.
- `ContentUnlockPresentation`: 요청받은 소개와 버튼 연출을 실행한다.
- `OutgameTutorialRunner`: 기존 FTUE와 자율 챕터의 스텝 실행·완료 기록을 담당한다.

## 진행과 호환

### 강화 안내 전 첫 패배 (2026-09-18)

- FTUE 중 첫 패배(항복 포함)는 `tutorial.defeatEnhancePending`을 예약한다. 승리·무승부·무효 경기는 예약하지 않는다.
- 카드 지급과 덱 편집을 마친 다음 `BattleEntry` 직전에 기존 `CollectionTabFirstEnter` 무료 강화 안내를 먼저 실행한다. 선택한 덱의 강화 가능한 카드를 우선 안내한다.
- FTUE 좌표와 졸업 여부는 유지하며 강화 안내 동안 강제 커서와 전투 진입을 멈춘다. 카드 강화와 컬렉션만 필요한 범위에서 열고 다른 미션의 안내는 앞당기지 않는다.
- 기존 `GuideResume`로 중단 지점을 저장한다. 강화 안내 완료 저장과 매치 탭 복귀까지 복구 정보를 유지한 뒤 원래 FTUE를 재개한다. 이후 `guide.01`에서는 같은 강화 안내를 반복하지 않는다.
- 튜토리얼 전투도 플레이어 실제 레벨이 시나리오 레벨 이상이면 샤드 진행도를 포함한 실제 성장값을 쓴다. 적은 시나리오 성장값을 유지한다.
- 기존 무료 강화 권한을 사용하므로 서버 함수·스펙 데이터 변경은 없다. `Validate Onboarding Execution`에서 패배 예약, 지급 전 보류, 전투 체크포인트, 저장 왕복, 완료 후 복귀, 중복 방지를 검사한다.

현재 가이드는 서버 정의 순서상 가장 앞선 미수령 미션이다. 달성만으로 다음 미션이 활성화되지 않는다. 해금은 해당 미션 도달 후 유지하고, 자동 안내는 현재 미션에 대해서만 실행한다. 지난 미션의 미완료 안내는 소급하지 않는다.

해금 여부, 소개 확인, 온보딩 완료는 별도 의미다. 소개는 항목마다 `MarkPresented`로 소비하며, 안내 중단 시 이미 확인한 소개를 다시 재생하지 않는다. 진행 중인 자율 안내가 중단되면 이번 세션 자동 재시작을 미루고, 미션 이동 버튼으로 재개한다. 기존 `CompletedTriggers`와 모험 레거시 완료 호환은 유지한다.

미션 정의·서버 저장 스키마·재화 권한은 변경하지 않는다. guide.05 시너지 상세 안내와 무료 성장 지급은 후속 작업이다. 키워드 강화·덱 시너지 조합 소개는 기존 조건을 유지한다.

## 돌보미 성장·두 시너지 진행 수정 (2026-09-17)

- `guide.06`은 특정 카드 3종 대신 보유한 돌보미 중 서로 다른 3종의 2성 이상 성장을 인정한다. 기존 `Guide.StarterCardsAtStar2` 이벤트 키·최고 진행도·수령 기록은 유지한다. 안내 후보도 카드 표의 돌보미 전체에서 보유·성장 상태가 좋은 3종을 고른다.
- `guide.07` 보상에 깜밤이(8) 1장을 추가한다. 기존 버섯냥(9)과 함께 다음 돌보미·추적 조합을 준비할 수 있다. `guide.08`의 2성 이상 돌보미 3종 + 깜밤이·버섯냥 편성 조건은 유지한다.
- `guide.07`을 이미 수령한 계정에는 보상을 재지급하지 않는다. 이 계정도 실버 전에 깜밤이를 얻을 수 있도록 브론즈 일반 팩 풀에 가중치 400으로 추가했다. 브론즈 풀의 상대 확률은 다시 정규화되며 실버 이상 풀은 그대로다.
- 후속 사용자 지시로 서버 배포와 CSV 발행을 완료했다. `bm-cardbattle` / `asia-northeast3`의 `getMissions`, `claimMission`, `syncGuideProgress`, `enhanceCard`, `enhanceSynergyIntroduction`, `limitBreakCard`, `openPack`, `claimReward`, `claimAttendance`, `claimBattleExperience`, `claimPassReward`, `spinRoulette` 12개를 선별 배포하고 모두 `ACTIVE`임을 확인했다.
- test **6.57→6.58**, live **6.9→6.10**을 발행했다. `publish-guide-synergy-spec.js`가 CSV를 직접 읽어 Mission 21·22행 수정, Reward 268행·CardPackDrop 1409행 추가만 허용한다. 환경별 15개 문서를 CAS 조건의 단일 commit으로 반영하고 재조회했다. 나머지 21개 표 pin은 보존했다. 발행 계획·이전 문서·배포 로그는 `.codex_tmp/guide-synergy-deploy/`에 보관했다.
- 새 test 계정에서 실제 `getMissions`·`claimMission`을 호출해 포슬램·파도리·솜구름몽만으로 2→3 진행도 및 guide.06 수령, guide.07의 깜밤이·버섯냥 지급, 중복 수령 거절, guide.08 두 시너지 달성을 확인했다. 기존 사용자 데이터는 변경하지 않았다. Unity에서 `GuidanceIntegrationValidation`도 통과했다.
- 런타임 표는 서버 스냅샷으로 동기화되므로 `SpecData.bytes`·자동 생성 C#은 갱신하지 않았다. 외부 스프레드시트는 이번 발행 범위 밖이다.
- 클라이언트 릴리즈 APK 빌드 완료: `Build/GuideSynergy-20260917/CardBattle-guide-synergy-20260917.apk` (57,135,781바이트). Android ARM64 / IL2CPP / development=false, 빌드 오류 0·경고 47. ZIP CRC와 새 `IsCaretaker` 메서드의 IL2CPP 메타데이터 포함을 확인했다. SHA-256: `831a5fd784152593797e7f8781fcc2add443eedda0ea945646bdf974eaf14f45`. Addressables 재빌드·Hosting 변경 없이 기존 리소스를 사용했다. 설치·기기 실플레이는 수행하지 않았다.
- 검증: 서버 가이드 회귀 12개, 실제 `GuideMissionPreparation` 카드 선택 검사 7개, 런타임·에디터 C# 컴파일, CSV 열 수·행 ID 중복 검사를 통과했다. 기기 실플레이는 별도 확인이 필요하다.

## 검증

- `Tools/Tutorial/Validate FTUE Guide Separation`: 분리 저장, 챕터 성격, 편집 좌표, 스텝 ID·직렬화 왕복과 목록 경계 검증.
- `Tools/Tutorial/Validate Content Unlock Separation`: 별도 SO·카탈로그 참조, AND 조건, 미션 조건 없는 콘텐츠의 독립성 검증.
- `Tools/Tutorial/Validate Guide Mission Flows`: 연결 중복, 미션 CSV 참조, 챕터·소개 존재, 미션 접근 순환 및 진행 규칙 검증.
- `Tools/Tutorial/Validate Content Unlock Intro`: 해금 소개 저작과 기존 연출 계약 검증.
- `Tools/Tutorial/Validate Guidance Integration`: 기존 저장 기록 호환 검증.
- 플레이 회귀: FTUE 졸업, guide.01·guide.03 활성화, 소개 도중 종료, 안내 중단 후 이동 버튼, 재접속, 지난 미션 계정, 팝업·탭 전환과의 순서.
