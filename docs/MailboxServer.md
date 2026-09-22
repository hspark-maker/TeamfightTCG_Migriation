# 우편함 서버 API

## 범위와 저장

- Firebase Callable: `sendMail`, `getMailbox`, `claimMail`, `claimAllMail`.
- 기존 `cardbattle` DB의 `envs/{env}/users/{uid}/mail/{mailId}`에 저장한다. `env`는 `test` 또는 `live`다.
- 우편 내용은 발송 후 불변이다. 수령 때 `claimedAtMs`만 바뀐다. 제목·본문·첨부 수량은 발송 시 확정한다.
- 재화, 고정 카드, 고정 팩을 지원한다. 팩은 **수령 시 현재 서버 표로 개봉하여 카드 지급**한다. 선택형 팩·미개봉 팩 보관·전체/예약 발송·읽음/삭제 API는 이번 범위에 없다.
- 직접 Firestore 접근은 기존 기본 거부 규칙으로 차단된다. 조회와 수령은 인증된 본인의 Callable만 사용한다.
- 수령 이력은 우편 문서에 영구 보존한다. 만료와 삭제는 다르다. 우편을 삭제한 뒤 같은 ID로 재발송하면 중복 방지 근거가 사라지므로 자동 TTL/삭제를 설정하지 않는다.

## 발송: sendMail

인증 토큰의 `admin === true`가 필요하다. 관리자용 호출이며, 플레이어 클라이언트에 발송 권한을 주지 않는다. 수신자의 `save/current`가 있어야 한다.

```json
{
  "env": "test",
  "uid": "RECIPIENT_UID",
  "mailId": "maintenance-20260922-v1",
  "title": "점검 보상",
  "body": "점검에 협조해 주셔서 감사합니다.",
  "expiresAtMs": 1790812800000,
  "rewards": {
    "currencies": [{"currency": "Gold", "amount": 100}],
    "items": [{"rewardType": "Card", "rewardId": "1", "amount": 1}]
  }
}
```

`expiresAtMs`는 미래의 Unix 밀리초로 지정한다. 예시 카드 ID는 대상 환경에 실제 존재하는 ID로 바꾼다. 팩 첨부는 `{"rewardType":"Pack","rewardId":"실제 packId","amount":1}` 모양이다.

- `mailId`: 영문·숫자·`_`·`-`, 1~128자. 한 발송 건에 고정 ID를 사용한다.
- 제목: 공백만인 값 금지, 최대 100자. 본문: 최대 4,000자.
- 첨부: 두 배열 합계 1~10개. 빈 배열도 명시한다.
- 재화: 기존 `CURRENCY_KEYS`의 키, 양의 안전 정수, 항목당 `CURRENCY_MAX` 이하.
- 카드·팩: 항목당 1~10개. 팩 개봉을 포함한 예상 지급 카드가 우편당 100장을 넘으면 거절한다.
- 동일 수신자·환경·`mailId`·내용으로 재시도하면 `{"mailId":"...","created":false}`를 반환한다. 내용이 달라지면 `MailIdReused`로 거절한다. 첫 발송은 `created:true`다.
- 발송자 UID를 `createdBy`로 남긴다. 발송은 수신자의 지갑·세이브 revision을 바꾸지 않는다.

## 조회: getMailbox

요청: `{"env":"test","limit":20}`. `limit`는 기본 20, 최대 50이다. 다음 페이지는 응답의 `nextCursor`를 그대로 `cursor`에 넣는다.

```json
{
  "mails": [
    {
      "mailId": "maintenance-20260922-v1",
      "title": "점검 보상",
      "body": "점검에 협조해 주셔서 감사합니다.",
      "rewards": {"currencies": [{"currency":"Gold","amount":100}], "items": []},
      "createdAtMs": 1790035200000,
      "expiresAtMs": 1790812800000,
      "claimedAtMs": null,
      "state": "Claimable"
    }
  ],
  "nextCursor": null,
  "hasClaimable": true,
  "serverNowMs": 1790035200000
}
```

최신 발송순이며 같은 시각에는 ID 내림차순이다. 수령·만료 우편도 목록에 포함한다. 상태는 서버가 계산하는 `Claimable` / `Claimed` / `Expired`다. 수령 이력이 있으면 만료 시간이 지나도 `Claimed`다.

`hasClaimable`은 현재 페이지만이 아닌 전체 우편함의 수령 가능 여부다. 목록과 알림 조회는 독립 읽기라 동시 수령 직후 잠시 다를 수 있다. 조회는 수령·읽음 상태를 바꾸지 않는다. 관리자 UID와 내부 지문은 응답에 노출하지 않는다.

## 수령: claimMail / claimAllMail

- 개별: `{"env":"test","mailId":"maintenance-20260922-v1","txId":"claimMail:고유요청ID"}`.
- 모두 받기: `{"env":"test","txId":"claimAllMail:고유요청ID"}`.
- `txId`는 기존 서버 명령 영수증 규칙을 따른다. 응답 유실 재시도만 같은 ID를 사용하고, 다른 우편이나 다음 일괄 요청에는 새 ID를 쓴다.
- 클라이언트 연동 시 반드시 `ServerSaveCommands.InvokeAsync`를 통해 호출한다.

응답은 기존 `revision`, `updatedSlots`, `wallet`에 다음 필드를 더한다.

| 필드 | 의미 |
|---|---|
| `claimedMailIds` | 이번 트랜잭션에서 수령한 우편 ID |
| `granted` | 첨부 재화 + 중복 카드 보상 |
| `cards`, `packs` | 기존 카드·팩 보상 표시용 결과 |
| `hasMore` | 일괄 선택 시 20통 뒤에 더 있었는지. 계속 받기는 새 `txId`로 요청 |
| `missions` | 카드 획득으로 반영된 가이드 진행도, 해당할 때만 포함 |
| `statistics`, `achievements` | 팩 개봉 통계가 반영된 경우 기존 공통 응답 계약 |

모두 받기는 **만료 임박순 최대 20통을 한 트랜잭션**으로 처리한다. 수령·만료 우편은 건너뛴다. 선택된 우편의 보상 지급이 실패하면 그 묶음 전체를 롤백한다. 개별 호출의 `hasMore`는 항상 false이며 전체 우편함 여부는 `getMailbox.hasClaimable`로 확인한다.

우편 수령 낙인, 지갑, 소유·성장, 영수증을 함께 커밋한다. 요청 ID가 바뀌거나 영수증 TTL이 지나도 수령 낙인으로 재지급을 막는다. 수령 가능한지 서버 시각으로 판정하고 보상 준비 후 다시 만료를 확인한다. 같은 묶음에 같은 카드가 여러 번 있으면 첫 지급 이후 것은 중복으로 처리한다.

재화 잔액 상한 처리는 기존 지갑 정책(`grant`의 상한 제한)을 따른다. 팩 내용·중복 카드 환산은 수령 시점의 표를 따른다. 발송된 우편이 참조하는 카드·팩을 만료 전에 표에서 제거하면 해당 우편 수령이 실패할 수 있다.

기존 영수증 재생은 세이브 revision 일치를 요구한다. 응답 유실 상태에서 다른 쓰기를 먼저 실행해 revision이 달라지면 기존 세션 복구 경로로 재동기화해야 한다. 클라이언트는 서버 응답을 채택한 후 수령 결과를 표시하고 목록을 갱신한다.

## 거절과 배포

도메인 거절은 기존 `rejectDomain` 계약대로 `permission-denied`, 메시지 접두어 및 `details.reason`에 사유를 싣는다.

- `MailNotFound`: 본인 우편함에 없음.
- `MailAlreadyClaimed`: 이미 수령함.
- `MailExpired`: 만료됨. 수령 준비 중 만료된 경우도 포함.
- `MailNothingToClaim`: 모두 받기 대상이 없음.
- `MailIdReused`: 발송 ID에 다른 내용을 사용함.
- `TxIdReused`: 수령 요청 ID를 다른 명령·우편에 재사용함.

입력 오류는 `invalid-argument`, 미등록 수신자는 `not-found`, 깨진 문서·보상 표는 `failed-precondition`이다.

배포 시 `firestore.indexes.json`의 `mail(claimedAtMs ASC, expiresAtMs ASC)` 인덱스를 준비한 뒤 네 함수를 배포한다. 에뮬레이터는 운영 인덱스 준비 여부를 검증하지 못하므로 배포 시 준비 상태를 확인해야 한다.

2026-09-22: 사용자 요청에 따른 실계정 연동 준비로 `bm-cardbattle`의 `asia-northeast3`에 `sendMail`, `getMailbox`, `claimMail`, `claimAllMail`을 배포하고 모두 `ACTIVE`임을 확인했다. `cardbattle` DB 인덱스 배포와 등록도 확인했다. 인덱스의 실제 조회 가능 여부와 실계정 발송·수령은 아직 확인하지 못했다. Unity MCP 연결 해제로 현재 계정을 확인할 수 없어 테스트 우편은 발송하지 않았다.

## 로컬 검증

`functions`에서 `npm.cmd run build`, `npm.cmd run lint`를 실행한다. 로컬 Firestore 에뮬레이터 실행 후:

```powershell
$env:FIRESTORE_EMULATOR_HOST = '127.0.0.1:8189'
$env:GCLOUD_PROJECT = 'demo-mailbox'
node --test scripts/test-mailbox-emulator.js
```

테스트는 로컬 주소와 `demo-*` 프로젝트만 허용한다. 실제 우편·지갑·세이브 트랜잭션을 실행하고, 스펙만 테스트 표로 대체한다.

2026-09-22 검증: 빌드·린트, 우편함 에뮬레이터 테스트 16개, 공통 팩 통계 에뮬레이터 회귀 테스트 4개 통과. 기존 `test-attendance.js`는 테스트용 문서 참조의 `parent`가 없어 실패한다. 변경 전 HEAD의 `saveDocument.js`로도 같은 실패를 재현했으며 이번 작업에서 그 테스트 모형은 수정하지 않았다.
