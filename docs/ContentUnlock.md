# 아웃게임 콘텐츠 해금

## 설정

진실원은 `Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset`의 최상위 `contentUnlocks`다. Inspector 또는 튜토리얼 저작 창의 설정 영역에서 편집한다. 챕터와 스텝의 `unlocks`는 FTUE 입력 제어용이며 콘텐츠 이용 조건을 덮지 않는다.

| 필드 | 의미 |
| --- | --- |
| feature | Mission / Adventure / Roulette. 콘텐츠당 하나 |
| requireFtue | 강제 선형 FTUE 전체 완료 필요 여부 |
| requireRank | 최소 랭크 조건 사용 여부 |
| minRankGrade | 최소 랭크 등급. ERankGrade 드롭다운 |
| minRankDivision | 해당 등급 내 단계. 랭크 미사용이면 0 |
| minAccountLevel | 최소 계정 레벨. 0이면 미사용 |

조건은 모두 AND로 평가한다. 현재 Mission은 FTUE 완료, Adventure는 Bronze(0) 2단계, Roulette는 조건 없음이다. 랭크는 최고 도달 티어로 비교한다. 신규 콘텐츠 추가 시 enum·매핑·검증·대표 UI 연결도 필요하다.

## 런타임과 저장

- `TutorialDataStep`이 같은 SO의 챕터를 러너에, 해금 조건을 `ContentUnlockConfig`에 각각 주입한다. 설정은 읽기 전용 값으로 복사한다.
- `ContentUnlockConfig`가 중복·누락·조건 형식을 검증한다. 성장 표 로드 이후 `SaveDependentManagersStep`에서 실제 랭크·계정 레벨 범위도 확인한다. 잘못된 설정은 초기화 복구로 연결한다.
- 에디터 `TutorialSequenceState`는 편집 중인 SO를 직접 사용한다. 플레이 세션에 주입된 다른 SO에 영향을 받지 않는다.
- `ContentUnlockRules`가 조건을 평가하고, `ContentUnlockManager`가 FTUE·랭크·레벨 변경에 따라 영구 해금과 연출 대기를 관리한다.
- 기존 `OutgameFeatureLock.IsUnlocked`는 세 콘텐츠를 관리자에 위임한다. FTUE 입력 게이트와 `IsFtueFreeNavigation`은 별도 역할을 유지한다.
- `profile.contentUnlocks`에 `version`, `unlocked`, `pending`을 저장한다. 최초 도입 시 이미 충족한 조건과 기존 Mission·Adventure 해금은 연출 없이 이관한다. 이후 조건 변경이나 랭크 하락으로 재잠금하지 않는다.
- 서버 슬롯 채택 중에는 변경을 예약하고, 응답 채택과 업로드 기준선 확정 이후 저장한다. 계정 교체는 예약과 연출 세션을 초기화한다.
- `AdventureUnlock`은 과거 튜토리얼 진행도 이관만 담당한다.

## 연출

`ContentUnlockPresentation`은 매치 탭이 보이고 기존 로비 연출이 끝난 뒤 대표 버튼의 `FeatureLockView`를 순차 재생한다. 탭 이동·비활성화·계정 변경으로 취소되면 대기를 유지한다. 정상 완료한 경우에만 대기를 제거한다. 콘텐츠 화면을 자동으로 열거나 탭을 강제로 바꾸지 않는다.

## 검증과 반영

- `Tools/ContentUnlockTests/run.ps1`: 실제 관리자·조건·저장 DTO를 사용한 동작 회귀 테스트.
- `Tools/ContentUnlockSpecTests/run.ps1`: 실제 SO 설정 모델·주입·검증 코드 테스트.

해금 전용 원격 표와 생성 타입은 사용하지 않는다. 설정 변경은 SO를 포함하는 클라이언트/에셋 배포로 반영한다. 서버 보상·전투 검증 정책은 기존대로 유지한다. 이미 저장된 해금 이력은 SO로 전환해도 호환된다.

`Assets/Resources/SpecData.bytes`는 수정하지 않는다. 실제 Unity 플레이에서 FTUE 졸업, 잠금 표시, 로비 연출 순서와 취소·복귀는 별도 확인한다.

기존 OutGame 규약이 참조하는 `docs/OutGamePlan/STRUCTURE.md`는 현재 체크아웃에 없어 이 문서에 구조를 기록한다.
