# 계정 경험치·레벨업 보상

## 지급 정책

- 미션 `accountExp`는 계정 경험치다. 기존 `passExp`(패스 경험치)와 독립적이다.
- 초기 값: 활성 일일 미션 50 XP, 주간 200 XP, 가이드 100 XP. 비활성 미션은 0 XP.
- 기존 `AccountLevel` 누적 경험치 곡선과 전투 승리 100 / 패배·무승부 50 XP를 사용한다. 만렙 이후 경험치는 쌓이지 않는다.
- `Reward`의 `ownerType=AccountLevel`, `ownerId=도달 레벨`로 자동 보상을 저작한다. 초기 값은 레벨 2~50마다 Gold 100.
- 여러 레벨을 건너뛰면 도달한 모든 레벨의 보상을 합산한다. 자동 지급할 수 없는 선택형 팩(`PackChoice`)은 허용하지 않는다.
- 기존 계정은 저장된 경험치를 유지하고 현재 레벨 이하의 보상을 소급하지 않는다. `profile.accountRewardLevel`은 서버만 갱신하는 지급 경계다.

## 구현 경로

- 미션: `claimMission`의 달성·수령 판정, XP, 레벨 보상, 세이브, 지갑을 한 트랜잭션에서 확정한다.
- 전투: `submitMatchResult`가 확정한 기록을 `claimBattleExperience`가 검증한다. 클라이언트가 보낸 승패·경험치 수치는 사용하지 않는다.
- 전투별 `accountBattleClaims/{matchId}` 낙인은 만료되는 영수증과 별개로 보존한다. 응답 유실·다른 영수증 번호·동시 요청에도 한 번만 지급한다.
- 클라이언트는 XP 회수가 끝날 때까지 전투 제출 큐를 보존한다. 서버 Profile 채택 후 레벨 HUD를 갱신하고, 전투 레벨업 보상은 로비의 기존 보상 팝업으로 표시한다.
- 미션 모두 받기는 각 서버 지급 결과를 합산한다. 팝업은 이미 지급된 결과를 보여주며 추가로 지급하지 않는다.
- 로컬 XP 디버그 변경은 중단했다. 경험치와 지급 경계의 직접 수정·삭제는 Firestore 규칙이 거부한다.

## 경험치 UI

- 미션·전투 보상 팝업에 경험치 획득량, 레벨, 누적 진행 게이지를 표시한다. 전투는 레벨업하지 않아도 획득량을 안내한다.
- 미션 모두 받기는 실제 지급량을 합산해 한 번 표시한다. 응답 채택 당시 누적치를 보관하므로 대기 중 다음 보상이 도착해도 앞선 보상의 표시가 변하지 않는다.
- 경험치만 받은 경우 패널이 중앙에, 아이템도 받은 경우 아이템 아래에 표시된다. 여러 보상 페이지에서는 첫 페이지에만 경험치 증가를 재생한다.
- 레벨 경계를 100% 채운 뒤 다음 레벨로 넘어가며 숫자와 게이지를 함께 갱신한다. 만렙은 `MAX`로 표시한다. 로비 설정의 프로필에도 같은 증가 규칙을 적용한다.
- `RewardClaimPopup.prefab`에 경험치 패널을 배선했다. 배포 앱에는 변경된 코드와 프리팹 리소스 반영이 필요하다. 기존 제목의 배치를 보존하기 위해 구 리소스의 제목에 경험치 문구를 덧붙이지 않는다.
- 검증: 런타임 전체 C# 컴파일, 실제 UI 소스를 사용하는 스텁 하네스 20항목, 프리팹 참조·배선·입력 통과·아이템 간격 검사 통과. Unity 연결 해제로 실제 Play 화면 및 모바일 시각 검수는 미실행이다.

## 데이터 발행과 출시

원본은 `docs/SpecData/Mission_sheet.csv`, `AccountLevel_sheet.csv`, `Reward_sheet.csv`다. 세 표의 Firestore 업로더는 CSV를 직접 읽는다. Mission은 원래부터 클라이언트 스펙 동기화 대상이 아닌 서버 전용 표이므로 새 열을 위해 생성 C#이나 `SpecData.bytes`를 변경할 필요가 없다.

업로더는 Mission의 기존 열 순서를 유지한 `accountExp` 열 추가만 같은 테이블 세대에서 허용한다. 기존 열 삭제·이름 변경·순서 변경 및 다른 표의 컬럼 변경은 기존 세대 검사를 유지한다. 이 추가 열을 발행하기 위해 테이블 세대 6을 올릴 필요는 없다.

초기 구현 검증은 로컬에서 수행했다. 2026-09-16 전투 경험치 호출의 `functions/NotFound` 확인 후 `bm-cardbattle` 프로젝트의 `asia-northeast3`에 `claimBattleExperience`를 배포했다. 함수 `ACTIVE` 상태와 호출 주소의 `UNAUTHENTICATED` 응답(미인증 검사)을 확인했다. 이 조치는 해당 함수만 배포했으며, 전체 기능 출시는 다음 묶음의 반영 여부를 별도로 확인해야 한다.

1. CSV의 Mission/AccountLevel/Reward 발행 및 테이블 인덱스 갱신. 보상표가 누락된 상태에서는 XP 지급을 거절한다.
2. 새 `claimBattleExperience`, 변경된 `claimMission`·`getMissions`·`ensureAccount` 등 Functions 코드와 Firestore 규칙 배포.
3. 새 클라이언트 빌드 배포. 구 클라이언트는 로컬에서 XP를 쓰므로 새 규칙과 호환되지 않는다. 앱 최소 버전 게이트를 포함해 구 클라이언트 접속을 차단한 상태로 전환해야 한다.

전투 복구는 기존 매치 기록 보존 기간(7일)의 제약을 따른다. 지급 완료 낙인은 그 기간과 무관하게 유지한다. 테스트 강제 승리·AI 대신 조작 등 서버 결과 제출을 건너뛰는 전투에서는 계정 XP를 지급하지 않는다.

## 검증

- `npm.cmd --prefix functions run build`, `npm.cmd --prefix functions run lint`
- `functions/scripts/test-account-experience.js`: 곡선·다중 레벨·만렙·소급 방지·카드 보상·미션 XP 파싱.
- `test-battle-experience.js`, `test-battle-experience-claim.js`: 확정 승패·참가자 검증, 영수증 및 영구 낙인, 동시 요청, 롤백, 카드 성장 미션 반영.
- `test-account-progress-rules.js`: 로컬 Firestore 에뮬레이터 전용. XP 위조·낙인 삭제 차단 및 신규/기존 계정 프로필 저장.
- `test-account-experience-emulator.js`: 실제 Firestore 트랜잭션에서 동일 영수증 경쟁·서로 다른 미션 경쟁·지급 실패 롤백·부분 스펙 발행 후 복구.
- Unity 런타임·에디터 전체 컴파일, `SpecSheetsUploadValidation.Run()` CSV 발행 오프라인 검증. 서버 업로드와 bytes 생성은 하지 않는다.

기존 `test-guide-mission-growth.js`는 변경 전 HEAD CSV부터 가이드 순서와 테스트 기대값이 어긋나 실패한다(`guide.07`은 7번인데 테스트는 4번째로 기대). 이번 작업에서는 사용자가 편집한 가이드 순서를 변경하지 않았다.

보안 검토 범위는 계정 경험치와 보상 낙인이다. 다른 세이브 슬롯에 대한 전체 보안 감사 결과를 뜻하지 않는다.

```json
{"score":5,"summary":"계정 경험치·지급 경계는 서버만 변경하며 기존 계정의 일반 프로필 저장은 허용한다. 계정 XP 변경 범위의 에뮬레이터 검증을 통과했다.","findings":[]}
```
