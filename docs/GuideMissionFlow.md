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
| 빈 값: FTUE 졸업 | 미션, 룰렛 | 없음 | 없음 |
| guide.01 | 카드 강화 | 컬렉션 | CollectionTabFirstEnter |
| guide.03 | 모험 | 모험 맵 | AdventureUnlocked |

`contentUnlocks`는 FTUE·랭크·계정 레벨·가이드 미션 조건을 AND로 평가한다. 현재 카드 강화는 `guide.01`, 모험은 `guide.03` 도달을 요구한다. 미션·룰렛은 FTUE 완료만 요구한다. 기존 저작의 해금 시점은 유지했다. 안내 순서나 챕터의 첫 스텝으로 이용 자격을 추론하지 않는다.

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

현재 가이드는 서버 정의 순서상 가장 앞선 미수령 미션이다. 달성만으로 다음 미션이 활성화되지 않는다. 해금은 해당 미션 도달 후 유지하고, 자동 안내는 현재 미션에 대해서만 실행한다. 지난 미션의 미완료 안내는 소급하지 않는다.

해금 여부, 소개 확인, 온보딩 완료는 별도 의미다. 소개는 항목마다 `MarkPresented`로 소비하며, 안내 중단 시 이미 확인한 소개를 다시 재생하지 않는다. 진행 중인 자율 안내가 중단되면 이번 세션 자동 재시작을 미루고, 미션 이동 버튼으로 재개한다. 기존 `CompletedTriggers`와 모험 레거시 완료 호환은 유지한다.

미션 정의·서버 저장 스키마·재화 권한은 변경하지 않는다. guide.05 시너지 상세 안내와 무료 성장 지급은 후속 작업이다. 키워드 강화·덱 시너지 조합 소개는 기존 조건을 유지한다.

## 검증

- `Tools/Tutorial/Validate FTUE Guide Separation`: 분리 저장, 챕터 성격, 편집 좌표, 스텝 ID·직렬화 왕복과 목록 경계 검증.
- `Tools/Tutorial/Validate Content Unlock Separation`: 별도 SO·카탈로그 참조, AND 조건, 미션 조건 없는 콘텐츠의 독립성 검증.
- `Tools/Tutorial/Validate Guide Mission Flows`: 연결 중복, 미션 CSV 참조, 챕터·소개 존재, 미션 접근 순환 및 진행 규칙 검증.
- `Tools/Tutorial/Validate Content Unlock Intro`: 해금 소개 저작과 기존 연출 계약 검증.
- `Tools/Tutorial/Validate Guidance Integration`: 기존 저장 기록 호환 검증.
- 플레이 회귀: FTUE 졸업, guide.01·guide.03 활성화, 소개 도중 종료, 안내 중단 후 이동 버튼, 재접속, 지난 미션 계정, 팝업·탭 전환과의 순서.
