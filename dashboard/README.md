# Card Battle 운영 대시보드

관리자 전용 조회·CSV 관리 화면. Firebase Authentication과 서울 리전의 조회 callable 3개를 사용한다.
게임 데이터는 명명 데이터베이스 `cardbattle`의 `envs/test`, `envs/live`에서 읽는다.

## 실행

```powershell
cd C:\Users\cookapps\TeamfightTCG_Migriation\dashboard
npm.cmd ci
npm.cmd --prefix ../functions ci
npm.cmd run dev
```

브라우저에서 <http://localhost:5173>을 연다. 저장소의 `dashboard/start.cmd`로도 실행할 수 있다.
기존 Firebase 관리자 계정으로 로그인한다. `admin: true` 커스텀 클레임이 필요하다.
Firebase CLI의 로그인과 대시보드의 관리자 로그인은 별개다. 이 앱에는 회원가입·권한 부여 기능이 없다.
로그인 상태는 브라우저 탭 세션에만 보관하며, 조회한 유저 데이터를 로컬 저장소에 저장하지 않는다.

새 PC에서는 `.env.example`을 `.env.local`로 복사하고 Firebase 웹 앱 설정을 넣는다.
파일은 UTF-8(BOM 없음)으로 저장한다. 설정 누락 또는 demo 프로젝트 설정이면 운영 빌드를 차단한다.
값은 Firebase Console → 프로젝트 설정 → 내 앱 → 웹 앱에서 확인한다.
브라우저용 Firebase 설정만 넣는다. 서비스 계정 키, Admin SDK 키, 개인 토큰을 넣으면 안 된다.

Google 로그인은 해당 도메인이 Authentication 허용 도메인에 있어야 한다. 로컬에서는
`localhost` 또는 `127.0.0.1` 중 실제 사용하는 주소를 등록한다. 이메일/비밀번호 로그인도 지원한다.
현재 프로젝트에는 `localhost`가 이미 등록되어 있으므로 위 실행 주소를 사용한다.
기존 관리자 권한 관리 도구는 `functions/scripts/grant-admin.js`다. 권한 부여는 자동 수행하지 않는다.

## 포함 범위

- 종합 현황: 콘텐츠 버전, 앱 버전 정책, 전투 검증 설정, 최근 7일 정산 수, 콘솔 링크.
- 유저 조회: UID 정확 검색, 인증 계정, 지갑, 세이브, 카드 소유/성장, 덱, 랭크, 모험, 도감, 튜토리얼.
- 콘텐츠·버전: 공개 인덱스의 테이블 리비전/해시, 앱 정책과 공지 상세.
- 전투 검증: 오늘을 포함한 UTC 7일 카운터. 집계 문서가 없는 날은 `—`로 표시.
- 매치 통계: 최대 90일 기간 선택, 일·주·월별 정산 추이, 최근 상세 기록의 AI전 승률(모험 포함), 평균 턴 수, 모드별 결과, 최근 50개 매치.
- CSV · SpecData: 테이블/행 검색, 기존 값 편집, 변경 검토 후 로컬 저장, test/live 서버 비교, test 발행.
- 수동 새로고침. 환경 변경·로그아웃·재조회 시 기존 데이터는 지운다.

Authentication 계정은 test/live가 공유한다. 세이브와 지갑만 선택 환경으로 분리된다.
미션·패스·출석 등 별도 문서 조회, 닉네임 검색, 재화 영수증, 유저 데이터 지급·회수·수정은 포함하지 않는다.
Cloud Run의 실제 인스턴스 상태·이미지와 Functions 로그는 연결된 콘솔에서 확인한다.
표시된 전투 검증 활성 여부는 환경 설정이지 Cloud Run의 건강 상태를 뜻하지 않는다.

유저 조회는 Firestore 문서 2개와 Auth 사용자 1건, 현황 조회는 문서 10개를 읽는다.
전체 유저를 훑거나 상시 리스너를 사용하지 않는다. 읽기 실패는 오류로 표시하며, 없는 문서를 생성하지 않는다.

## API

| Callable                 | 입력                                               | 응답                                                                  |
| ------------------------ | -------------------------------------------------- | --------------------------------------------------------------------- |
| `adminDashboardOverview` | `{ env: "test" \| "live" }`                        | `{ env, fetchedAtMs, content, appPolicy, replay: { enabled, days } }` |
| `adminDashboardPlayer`   | `{ env, uid }`                                     | `{ env, uid, fetchedAtMs, account, save, wallet }`                    |
| `adminDashboardMatches`  | `{ env, days }` 또는 `{ env, startDate, endDate }` | 최신 매치 표본·일별 집계·모드별 통계·최근 매치                        |

인증되지 않은 요청, 관리자 클레임이 boolean `true`가 아닌 요청, 잘못된 환경/UID는 읽기 전에 거절한다.
게임용 getter는 초기화·보정 쓰기를 수행할 수 있으므로 호출하지 않는다.
기존 Firestore Rules를 완화하지 않고 서버의 명시적 필드 목록만 반환한다.
Auth 비밀번호 해시·토큰·customClaims와 지갑 paidBalances는 응답에 포함하지 않는다.

소스: `functions/src/commands/adminDashboard.ts`.
새 API만 배포할 때 저장소 루트에서 다음처럼 범위를 한정한다.

```powershell
firebase.cmd deploy --only functions:adminDashboardOverview,functions:adminDashboardPlayer --project bm-cardbattle
```

이는 다른 게임 함수, Firestore 규칙, 리소스 Hosting을 배포하는 명령이 아니다.
조회 API가 미배포되었거나 접근할 수 없으면 화면에 연결 오류가 나타난다.

2026-09-22: `Card Battle Dashboard` 웹 앱 등록, 로컬 `.env.local` 연결,
`adminDashboardOverview`·`adminDashboardPlayer` 서울 리전 신규 배포 완료.
기존 게임 함수·Firestore 규칙·데이터·리소스 Hosting은 배포 대상으로 지정하지 않았다.

## 빌드

```powershell
npm.cmd run build
npm.cmd run preview
```

산출물: 현재 Windows 사용자의 `Desktop/build/CardBattleDashboard/`.
`index.html`을 직접 더블클릭하지 않고 이 저장소의 preview 서버로 연다. 서비스 계정 키는 필요 없다.
CSV 관리는 로컬 Node 서버가 필요하다. 정적 파일만 Hosting에 올리면 로컬 저장소의 CSV를 편집할 수 없다.
웹 Hosting 공개 배포는 별도이며, 게임 리소스 사이트 `bm-cardbattle-assets`에 이 산출물을 덮어쓰지 않는다.

## CSV · SpecData 작업 흐름

1. 관리자 로그인 후 **CSV · SpecData → CSV 편집**에서 테이블을 선택한다.
2. 기존 행의 값을 수정하고 **변경 검토**로 전후 값을 확인한 뒤 로컬 CSV에 저장한다.
3. **서버 비교 · 발행**에서 저장한 전체 스펙을 선택 환경과 비교한다.
4. TEST에서 발행 계획을 만들고 전체 변경 테이블·셀 차이·버전을 검토한다.
5. 표시된 버전을 입력하고 최종 확인하면 TEST에 한 번 발행하고 서버 결과를 검증한다.

CSV 원본은 `docs/SpecData/*_sheet.csv`다. test/live 전환은 서버 비교 대상을 바꾸며 로컬 CSV는 공유한다.
현재 30개 파일 중 일반 스키마 29개를 편집한다. `enum_sheet.csv`는 읽기 전용으로 표시한다.
`AIDeckCard`, `AdventureReward`, `CardGrade`는 로컬 CSV 편집만 지원하며 현재 서버 발행 대상이 아니다.
화면에서 해당 테이블을 **로컬 전용 · 서버 발행 제외**로 구분한다. 발행 목록 자체는 변경하지 않는다.
이번 단계는 기존 셀 값만 수정한다. ID·열 타입·스키마 변경과 행 추가/삭제는 지원하지 않는다.
정수 타입을 검증하며 CSV의 BOM, 줄바꿈, 따옴표와 변경하지 않은 셀의 바이트를 보존한다.
저장 직전 파일 해시를 다시 확인하므로 외부 편집과 충돌하면 덮어쓰지 않는다.

서버 비교는 발행 대상 26개 테이블의 payload 해시를 비교한다. 로컬 저장과 서버 발행은 별개다.
발행은 선택한 한 테이블이 아니라 **현재 CSV의 발행 대상 전체 변경**을 포함한다.
기존 `functions/scripts/publish-all-csv-spec.js`를 재사용하며 TEST의 같은 콘텐츠 세대 내 갱신만 지원한다.
LIVE 발행·정식 스키마 변경은 기존 릴리즈 절차에서 수행한다.
발행 계획에는 런타임 blob의 변경과 콘솔용 rows 문서의 실제 변경·복구 내역을 함께 표시한다.
발행 계획은 관리자 UID에 연결되고 10분 뒤 만료된다. 로컬 원본 해시와 서버 문서 버전을 다시 검사하며
한 번의 원자적 commit을 사용한다. 변경이 2,000셀을 초과하면 화면에서 발행하지 않는다.
발행 결과가 불명확하면 자동 재시도하지 않는다. 서버 비교에서 현재 버전을 확인한 뒤 계획을 새로 만든다.

`SpecData.bytes`는 존재·해시 확인을 위해 읽기만 한다. 재생성·임포터 실행·Google 시트 동기화를 하지 않는다.
bytes가 있다는 표시는 CSV와 일치한다는 의미가 아니다. 자동 생성 C# 파일도 수정하지 않는다.

로컬 API(`/__spec/*`)는 Vite dev/preview에 연결되며 loopback·동일 출처 JSON POST만 받는다.
매 요청마다 Firebase ID 토큰과 boolean `admin: true`를 확인한다. 서버 비교·발행은 로그인한 관리자의
ID 토큰으로 Firestore REST를 호출하므로 기존 Firestore Rules가 적용된다. CLI 로그인·서비스 계정 키는 쓰지 않는다.
`functions`의 기존 Admin SDK는 ID 토큰 검증에만 사용한다. 발행 계획 원본은 서버 메모리에 보관하며 브라우저에서 쓰기 내용을 주입할 수 없다.
서버 재시작 후에는 발행 계획을 새로 만들어야 한다.

## 매치 통계

`adminDashboardMatches`는 관리자 전용 읽기 API다. `{ env, days }`의 days는 1~90 정수이며 기본은 7일이다.
직접 지정은 `{ env, startDate: "YYYY-MM-DD", endDate: "YYYY-MM-DD" }`로 보내며 양 끝 날짜를 포함한다.
days와 직접 지정 날짜를 혼합하지 않는다. 실제 달력 날짜·시작/끝 순서·최대 90일·미래 날짜를 서버에서 검사한다.
기준 시간대는 UTC이며, 오늘의 끝은 현재 조회 시각까지만 포함한다.

기간 선택: 오늘, 어제, 3일, 7일, 14일, 30일, 90일, 이번 주(월요일 시작), 이번 달, 직접 지정.
최근 N일은 오늘을 포함하며 이번 주/이번 달도 오늘까지다. 직접 지정은 날짜 입력 후 **기간 적용**으로 조회한다.
추이의 **일별 / 주별 / 월별**은 받아온 일별 카운터를 화면에서 묶으므로 추가 서버 조회가 없다.
주별은 UTC 월요일, 월별은 UTC 달력 월을 기준으로 묶되 선택 범위를 벗어난 날짜는 합산하지 않는다.
집계가 일부 누락된 그룹은 확인한 날짜의 소계를 **부분 집계**로 표시하고, 확인한 날짜 수/대상 날짜 수를 함께 보여준다.
모든 날짜가 누락이면 `—`, 실제 확인된 0은 `0`으로 표시한다.

`matches`의 `settledAt` 범위로 최신 501개 문서만 조회하고, 500건을 분석한다. 나머지 1건으로
한도 초과를 표시한다. 조회 전용 필드 선택으로 제출 로그·시드·계정 UID·덱 원문은 반환하지 않는다.
정산 시각이 없는 진행 중 매치는 분석 대상이 아니다. 최근 목록은 분석 대상 중 최신 50건이다.
상세 조회 범위는 선택 기간과 현재 시각 기준 최근 7일의 교집합으로 제한한다. 이전 기간만 선택하면
매치 문서를 조회하지 않고 일별 집계만 보여준다. 이때 승률·평균 턴을 0으로 표시하지 않는다.
7일보다 긴 기간에서는 전체 선택 기간의 추이와 상세 지표의 실제 조회 범위를 화면에 나눠 표시한다.

승률은 서버 재생 판정이 있는 확정 AI/모험전의 승리 ÷ (승리 + 패배)다. 무승부·미확인은 분모에서 제외한다.
평균 턴 수는 서버 재생에 성공한 확정 매치 중 정상적인 정수 턴 수가 있는 기록만 사용한다.
PvP는 두 참가자의 승패를 합한 전체 승률을 만들지 않고 모드별 건수와 판정 여부를 보여준다.
승률·평균 턴·모드별 수치는 최신 500건 표본 기준이며 전체 이용자의 장기 통계가 아니다.

일별 추이는 기존 `telemetry/replayDaily/days`의 정산 카운터를 최대 90개 읽는다. 이 값은 표본 제한이 없는
집계이므로 상세 매치 수와 다를 수 있다. 집계 문서 누락·잘못된 값은 `—`로 표시하고 0과 구분한다.
집계는 비동기 처리돼 잠시 지연될 수 있다. 매치 문서는 정산 코드에서 7일 만료 시간을 지정하므로,
이미 삭제된 기록을 복구하거나 장기 이력을 생성하지 않는다. 신규 게임 이벤트·스키마·색인은 추가하지 않는다.

새 API만 배포할 때는 `firebase.cmd deploy --only functions:adminDashboardMatches --project bm-cardbattle`를 사용한다.
2026-09-22: 서울 리전에 `adminDashboardMatches` 배포 완료. TEST/LIVE의 실제 매치 기록 조회와 배포 API의 미인증 요청 차단을 확인했다.
같은 날 1~90일·직접 지정 기간 및 상세 보관 범위 응답으로 업데이트했고, 대시보드에 일·주·월별 집계를 연결했다.
백엔드 검증: `firebase.cmd emulators:exec --only firestore --project demo-dashboard "node --test functions/scripts/test-admin-dashboard-matches-emulator.js"`.

쿼리 참고: [Firestore 정렬·조회 한도](https://firebase.google.com/docs/firestore/query-data/order-limit-data).

## 검증

CSV 저장·로컬 API·발행 계획 검증은 `dashboard`에서 실행한다. 임시 파일과 가짜 원격 응답만 사용한다.

```powershell
npm.cmd run test:local
```

백엔드: 저장소 루트에서 실행한다. 테스트는 `demo-*` 프로젝트와 로컬 Firestore 에뮬레이터를 강제한다.

```powershell
npm.cmd --prefix functions run build
firebase.cmd emulators:exec --only firestore --project demo-dashboard "node --test functions/scripts/test-admin-dashboard-emulator.js"
```

브라우저: `dashboard`에서 실행한다. Google Chrome이 필요하다.

```powershell
firebase.cmd emulators:exec --only auth --project demo-dashboard --config tests/firebase.json "npm.cmd test"
```

브라우저 테스트는 실제 로컬 Auth 에뮬레이터에서 로그인하고, 조회 API 응답은 고정 계약 데이터로 대체한다.
CSV 편집·저장은 OS 임시 폴더의 fixture와 실제 로컬 API로 검증한다. 에뮬레이터의 실제 서버 비교·발행은 차단한다.
운영 서버에 테스트 유저를 만들지 않는다. 비관리자 차단, 환경 전환, 검색/오류, 이전 응답 무시,
재시도, 로그아웃, 모바일 화면 폭을 확인한다. 백엔드 테스트는 실제 로컬 Firestore를 읽어 권한·필드
제한·환경 분리·조회 전후 문서 불변을 검증한다. 운영자 로그인 및 실제 유저 조회는 별도 확인 대상이다.

사용 기술 근거: [Callable](https://firebase.google.com/docs/functions/callable),
[Auth 세션](https://firebase.google.com/docs/auth/web/auth-state-persistence).
