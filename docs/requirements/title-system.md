# 칭호 선택·장착 시스템

2026-09-22 갱신. 칭호를 꾸미기와 별도 도메인으로 분리하고 서버 지급·소유로 전환한다. 기존 테스트 칭호 8종은 서버 통계의 달성 조건에 따라 자동 지급한다.

## Overview

보유 칭호를 선택·장착하고 로비 설정 화면에 표시한다. 기존 프로필 저장·복원 흐름을 확장한다.

## Goals

- 서버가 소유 칭호 ID 목록을 관리하고 클라이언트는 소유한 칭호의 장착 ID만 저장한다.
- 보유 칭호 하나를 장착하거나 해제한다.
- LobbySetting.prefab의 Line 위를 왼쪽 프로필 이미지, 오른쪽 칭호·닉네임·랭크 순으로 구성한다.
- 기존 `ProfileEditPanel.prefab`에 `FrameTab`을 복제한 칭호 탭을 추가한다. 탭 안에 미리보기·설명·스크롤 목록과 장착·해제 조작을 제공한다. 별도 칭호 선택창·목록 칸 프리팹은 제거하고 목록 칸은 편집창 내부 템플릿으로 둔다.

## Out of Scope

- 진행도 수치, 전투 효과, 매칭 상대에게 칭호 전달.
- 신규 정식 칭호 콘텐츠, 판매, Addressables 리소스 배포.
- SpecData.bytes 생성과 자동 생성 C# 수정.

## Technical Requirements

- 칭호 책임은 OutGame/Title/TitleManager에 둔다. CosmeticType·CosmeticItem에는 포함하지 않는다.
- 소지·장착 정보는 기존 profile 슬롯에 저장한다. 서버 응답의 ownedTitleIds를 채택하며 편집 중인 장착 후보는 보존한다. 명시적 devResetSave는 소유·장착을 모두 초기화한다.
- 칭호의 안정적 ID는 Title_sheet.csv의 titleId다. 전용 표 열은 id(int), titleId(string), eventKey(string), synergyId(string), targetCount(int), description(string)이다. 기존 테스트 8종을 유지한다. TitleCatalog는 표시 이름·기본 설명·아이콘·색·표시 순서를 담당한다.
- 칭호는 Reward 표와 공통 아이템 지급 분기를 사용하지 않는다. 서버의 칭호 전용 모듈이 해금 조건을 판정하고 영구 소유를 지급한다. 공개 임의 지급 API는 없고 중복 지급은 소유 유지, 자동 장착은 없다. 결과 표시용 Title 종류와 titles 응답은 기존 공통 보상 UI에 전달할 수 있다.
- 소유·수령 기록은 기존 트랜잭션으로 확정한다. 응답 titles 배열(titleId, isNew)을 공통 보상 전달·표시까지 유지한다.
- 클라이언트 소유 목록 쓰기를 금지하고 장착은 서버 기존 소유로 검증한다. 빈 ID는 해제다. 기존 계정의 필드 부재는 빈 소유·미장착이며 기존 소유 기록은 보존한다.
- 서버 Title 표는 칭호 보상 처리 시에만 로드한다. 클라이언트 필수 초기화 표에 추가하지 않는다. 표시 카탈로그에 없는 소유 ID는 보존한다.
- 에디터의 직접 지급 메뉴는 제거한다. 테스트 지급은 격리된 테스트 데이터와 로컬 에뮬레이터에서만 수행한다.
- 보유 칭호 클릭은 장착 후보를 변경하고 저장 버튼을 활성화한다. 같은 후보를 다시 클릭하면 해제 후보가 된다. 저장·닫기 시 확정하며 별도 장착 버튼은 두지 않는다. 미보유 칭호 클릭은 미리보기만 변경한다.
- 기존 프로필 탭의 크림·갈색 배경과 글자색을 사용한다. 선택은 노란 강조, 장착은 체크, 미보유는 자물쇠·흑백으로 구분한다.
- 로비 설정의 칭호 변경 버튼은 프로필 편집창의 칭호 탭을 바로 연다. 편집창을 닫으면 로비 설정으로 복귀한다. 외부 풀 정리·씬 이탈로 숨기는 경우 로비 설정을 다시 열지 않는다.
- 칭호 탭과 기존 아바타·프레임·감정표현 탭은 상호 전환한다. 칭호도 기존 프로필처럼 드래프트를 유지하며 저장·닫기 시 확정한다. 기존 장착 상태로 되돌리면 다른 변경이 없는 한 저장 버튼이 비활성화된다.
- 기존 프로필 편집·닉네임 수정·랭크 표시와 사용자 프리팹 수정분을 보존한다.
- 테스트용 목록에 긴 이름과 미보유 항목을 포함한다. 달성 판정은 서버 통계를 재사용하며 칭호 전용 카운터를 만들지 않는다.

## Acceptance Criteria

- [x] 로비 설정 상단이 요청한 배치로 표시된다.
- [x] 프로필 편집창의 칭호 탭 버튼 배선·생성·닫기 복귀가 정상이다.
- [ ] 칭호 탭 왕복 후 기존 아바타·프레임·감정표현 드래프트가 유지된다.
- [x] 선택·장착·미보유 상태가 구분된다.
- [x] 보유 칭호만 장착되며 해제가 가능하다.
- [ ] 재접속 후 소지·장착 상태가 복원된다.
- [x] 기존 프로필 편집이나 서버 응답 채택으로 칭호 정보가 사라지지 않는다.
- [x] 긴 칭호와 스크롤 목록이 잘리거나 겹치지 않는다.
- [x] 기존 계정도 접속 시 달성한 칭호를 지급받으며, 미달성 칭호는 잠금 상태를 유지한다(로컬 서버·클라이언트 검증).

2026-09-21 검증: 전체 런타임·에디터 컴파일, 도메인 9항목, 전체/슬롯 스냅샷과 JSON 복원, 서버 응답 채택 검증 통과. 프로필 프리팹의 목록·잠금·장착·스크롤·닫기 복귀와 렌더 확인 완료.

2026-09-22 서버 소유 전환 검증: 칭호 유닛 7개·로컬 Firestore 트랜잭션/규칙 4개·기존 꾸미기/계정경험치/규칙 회귀 36개 통과. C# 칭호 도메인 및 실제 저장 메서드 기반 프로필 하네스 통과. Unity 런타임·에디터 컴파일과 실제 프리팹 검증(잠금 8종 노출, 미소유 미리보기, 서버 소유 통지 후 즉시 해금, 후보·스크롤 유지, 탭 재개방, JSON 복원·보상 DTO) 통과. 실제 운영 서버 업로드 후 앱 재접속·플레이 연출은 미검증이며 원격 배포는 수행하지 않았다.

기존 직접 지급 메뉴는 서버 소유 전환으로 제거했다. 화면은 로비 설정 또는 `Tools > 칭호 > 칭호 선택창 열기`로 확인한다. 독립 칭호 테스트는 `scripts/test-title-ownership.ps1`, 서버 단위 테스트는 `functions/scripts/test-title-ownership.js`, 로컬 Firestore 테스트는 `functions/scripts/test-title-ownership-emulator.js`다. 에디터 `Tools > 검증 > 칭호 서버 소유 회귀`는 저장·통신 없이 임시 프리팹과 CSV를 검사한다.

배포 시 Title 전용 조건 표를 불변 스펙 릴리스로 발행한다. 기존 6.61의 Reward 칭호 행은 제거한다. 칭호 응답을 이해하는 클라이언트와 서버·규칙을 함께 검증한다. 원격 배포 대상은 test이며 아래 구현 기록과 원격 반영 결과를 구분한다.

## 자동 지급 조건 초안

`Title` 표가 해금 이벤트·시너지 ID·목표값·조건 문구의 진실원이다. Reward·Achievement 표를 읽지 않는다. 누적 집계는 기존 서버 통계를 재사용하며 업적 재화 보상 수령 여부와 이전 단계 수령 여부는 칭호 지급에 영향을 주지 않는다. 빈 eventKey·빈 synergyId·targetCount=0은 자동 해금이 없는 수동 지급용 항목이며 현재 8종은 모두 자동 해금 조건을 갖는다.

| 칭호 ID | 서버 통계 이벤트 | 조건 |
|---|---|---|
| test_first_step | WinBattle | 누적 1승 |
| test_blue_traveler | WinBattle | 누적 10승 |
| test_golden_collector | OpenPack | 카드 팩 누적 10개 개봉 |
| test_dawn_guardian | WinStreak | 최고 2연승 |
| test_long_journey | WinBattle | 누적 100승 |
| test_silent_sword | DestroyCards | 상대 카드 누적 50장 파괴 |
| test_small_universe | CompleteAlbum | 앨범 테마 1개 완성 |
| test_endless_adventure | WinBattle | 누적 300승 |

전투는 검증된 결과 정산 후 본인의 자동 `claimBattleExperience` 처리에서 지급한다. 상대 요청으로 양쪽 통계를 정산하는 `submitMatchResult`는 세이브 revision을 변경하지 않는다. 팩·카드 지급은 해당 서버 명령의 최종 소유·통계로 판정하며 기존 지급과 동일 트랜잭션에 합친다. `ensureTitles`는 초기 세이브 채택 전에 기존 달성 기록을 보완한다. 중복 지급·자동 장착은 하지 않는다.

칭호 미리보기의 조건 문구는 `ensureTitles.definitions`에서 받는다. 표시 SO에 조건을 중복 저작하지 않는다. 공개 스펙 인덱스에 Title 표가 없는 환경은 자동 칭호 지급을 수행하지 않는다. 인덱스·표·해시가 손상된 경우는 미발행으로 취급하지 않고 실패를 알린다.

2026-09-22 자동 지급 검증: 서버 고유 89개 테스트 통과(자동 칭호·기존 칭호·꾸미기·계정 경험치·규칙·카드 제작·팩 통계·플레이어 통계). 전체 Functions lint, TypeScript 컴파일 통과. C# 소유/장착·서버 조건 캐시·만렙 칭호 보상 하네스, 기존 프로필 저장 하네스, Unity 컴파일·실제 프로필 프리팹 회귀 통과. 동시 접속 중 다른 호출이 먼저 지급했으면 `Changed=false`여도 반환 revision 차이를 감지해 초기 세이브를 다시 읽는다.

2026-09-22 원격 배포 완료: 프로젝트 `bm-cardbattle`, Firestore DB `cardbattle`, 리전 `asia-northeast3`. test `6.60 → 6.61` 발행(Title 8행, 기존 원격 Reward 213행 보존 + 8행 추가, 다른 25표 payload 보존). 불변 스냅샷과 인덱스를 CAS 단일 커밋으로 반영하고 27표 전체 payload hash를 검증했다. live 스펙은 `6.11`, Title 미발행 상태를 유지한다.

Functions 14개 배포 후 ACTIVE 확인: ensureTitles, openPack, claimAttendance, claimMission, claimReward, claimPassReward, claimBattleExperience, spinRoulette, craftCard, grantTutorialCards, claimAchievement, getMissions, getAttendance, getPass. 서버 소스는 test/live 공용이며 Title 보상 행이 없는 live에서 추가 표 의존성을 만들지 않는다. 이번 배포용 Firestore 규칙은 기존 원격 규칙에 `(envId != 'test' || preservesTitleOwnership())`만 추가 적용했다. 원격 규칙과 배포 후보 일치 및 ensureTitles 미인증 요청 HTTP 401을 확인했다. 저장소의 전체 환경용 규칙과 원격의 test 전용 제한 차이를 다음 배포 때 유지·검토해야 한다.

실제 플레이어 문서를 테스트 목적으로 변경하지 않았다. 원격 배포 후 앱 재접속·실제 달성 플레이는 미검증이다. SpecData.bytes와 자동 생성 C#은 변경하지 않았다.

## 칭호 전용 표 분리 반영

2026-09-22 후속 수정으로 위 6.61의 Reward 연결 방식을 폐기했다. 현재 test는 `6.62`다. Title 8행을 6열로 확장하고 Reward의 칭호 8행(268~275)과 행 미러를 제거했다. 기존 Reward 213행(카드·팩 보상 포함)의 내용·순서와 다른 25표 payload를 보존했다. Title 조건은 기존 8종과 동일하며 이미 획득한 칭호 소유도 유지한다.

서버 `RewardItem`과 공통 `itemGrant`에서 Title 종류·지급 분기를 제거했다. 자동 해금은 Title 전용 표·도메인과 기존 서버 통계만 사용한다. `ensureTitles`와 관련 함수 14개 재배포 후 ACTIVE, test 6.62의 27표 hash, Reward 행 미러 삭제, 기존 test 전용 칭호 규칙 유지, 미인증 HTTP 401을 확인했다. live 스펙은 6.11과 Title 미발행 상태 그대로다.

분리 후 통합 서버 회귀 94개, 최종 정리 후 관련 15개 재검증, 전체 ESLint·TypeScript, C# 실제 Title CSV 검증·소유/조건/보상 하네스, Unity 컴파일·실제 칭호/프로필 UI 회귀 통과. 실제 계정 접속·달성 플레이는 수행하지 않았다. SpecData.bytes와 자동 생성 C#은 변경하지 않았다.

## Open Questions

없음. 테스트 조건 초안·달성 즉시 자동 지급·test만 배포 승인. 이후 수정 지시로 칭호만 Reward에서 분리한다. 기존 카드·팩 보상은 유지한다. live 스펙은 변경하지 않는다. 프로젝트 공용 Functions는 Title 표가 미발행인 환경에서 칭호 지급을 생략하며, 원격 규칙의 칭호 소유 검증은 test에만 적용한다.
