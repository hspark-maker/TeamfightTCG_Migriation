# Card Battle 운영 대시보드 — 1단계

관리자 전용 조회 화면. Firebase Authentication과 서울 리전의 조회 callable 2개를 사용한다.
게임 데이터는 명명 데이터베이스 `cardbattle`의 `envs/test`, `envs/live`에서 읽는다.

## 실행

```powershell
cd C:\Users\cookapps\TeamfightTCG_Migriation\dashboard
npm.cmd ci
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
- 수동 새로고침. 환경 변경·로그아웃·재조회 시 기존 데이터는 지운다.

Authentication 계정은 test/live가 공유한다. 세이브와 지갑만 선택 환경으로 분리된다.
미션·패스·출석 등 별도 문서, 닉네임 검색, 재화 영수증, 지급·회수·수정·배포는 포함하지 않는다.
Cloud Run의 실제 인스턴스 상태·이미지와 Functions 로그는 연결된 콘솔에서 확인한다.
표시된 전투 검증 활성 여부는 환경 설정이지 Cloud Run의 건강 상태를 뜻하지 않는다.

유저 조회는 Firestore 문서 2개와 Auth 사용자 1건, 현황 조회는 문서 10개를 읽는다.
전체 유저를 훑거나 상시 리스너를 사용하지 않는다. 읽기 실패는 오류로 표시하며, 없는 문서를 생성하지 않는다.

## API

| Callable | 입력 | 응답 |
|---|---|---|
| `adminDashboardOverview` | `{ env: "test" \| "live" }` | `{ env, fetchedAtMs, content, appPolicy, replay: { enabled, days } }` |
| `adminDashboardPlayer` | `{ env, uid }` | `{ env, uid, fetchedAtMs, account, save, wallet }` |

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
`index.html`을 직접 더블클릭하지 않고 preview 서버로 연다. 외부 서비스 계정 키가 필요 없는 정적 웹 앱이다.
웹 Hosting 공개 배포는 별도이며, 게임 리소스 사이트 `bm-cardbattle-assets`에 이 산출물을 덮어쓰지 않는다.

## 검증

백엔드: 저장소 루트에서 실행한다. 테스트는 `demo-*` 프로젝트와 로컬 Firestore 에뮬레이터를 강제한다.

```powershell
npm.cmd --prefix functions run build
firebase.cmd emulators:exec --only firestore --project demo-dashboard "node --test functions/scripts/test-admin-dashboard-emulator.js"
```

브라우저: `dashboard`에서 실행한다. Google Chrome이 필요하다.

```powershell
firebase.cmd emulators:exec --only auth --project demo-dashboard --config tests/firebase.json "npm.cmd test"
```

브라우저 테스트는 실제 로컬 Auth 에뮬레이터에서 로그인하고, 조회 API 응답만 고정 계약 데이터로 대체한다.
운영 서버에 테스트 유저를 만들지 않는다. 비관리자 차단, 환경 전환, 검색/오류, 이전 응답 무시,
재시도, 로그아웃, 모바일 화면 폭을 확인한다. 백엔드 테스트는 실제 로컬 Firestore를 읽어 권한·필드
제한·환경 분리·조회 전후 문서 불변을 검증한다. 운영자 로그인 및 실제 유저 조회는 별도 확인 대상이다.

사용 기술 근거: [Callable](https://firebase.google.com/docs/functions/callable),
[Auth 세션](https://firebase.google.com/docs/auth/web/auth-state-persistence).
