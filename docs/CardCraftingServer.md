# 카드 제작 서버

UI 연결 전 서버 구현이다. 이 변경 자체로 원격 스펙 업로드나 배포를 실행하지 않는다.

## 경제 설정

- 제작 전용 재화: `CardDust`. 강화 재화 `Shard`와 별도이며 기존 잔액을 전환하지 않는다.
- `Reward_sheet.csv`의 `CardDuplicate` 보상은 Common/Rare/Arcane/Mythic 순서로 CardDust 1/2/5/10이다.
- 유료 팩 및 직접 카드 보상의 중복 지급이 공통 보상 경로를 사용한다. 기존 무료 팩의 중복 보상 제외는 유지한다.
- 서버 전용 `CardCraft_sheet.csv`가 등급별 `cost`, `enabled`를 정의한다. 초기 가격 **20/40/100/200은 임시 밸런스**다.
- 활성 등급이고 현재 환경의 카드 카탈로그에 있는 미보유 카드만 제작한다. Live 환경에서는 Test 채널 카드가 제외된다.
- 신규 카드의 성장값은 기존 지급 규칙과 같다. Arcane/Mythic은 레벨 2, 나머지는 기본 레벨 1이며 기존 카드 성장값은 유지한다.

## Callable 계약

인증은 두 API 모두 필수다.

`getCardCrafting({env})`

```json
{"currency":"CardDust","cards":[{"cardId":1,"grade":"Common","cost":20}]}
```

목록은 소유 여부와 관계없는 환경별 제작 카탈로그다. 가격·활성 여부는 서버 스펙을 기준으로 한다.

`craftCard({env, cardId, txId})`

- `cardId`는 양의 정수 숫자다. `txId`는 요청별로 발급하는 8~128자 영숫자/콜론/밑줄/하이픈 문자열이며 재시도에는 같은 값을 사용한다.
- 클라이언트가 보내는 가격·재화·지급량으로 제작하지 않는다. 한 번에 카드 한 장을 제작한다.
- 응답: 기존 `revision`, `updatedSlots`, `wallet` 채택 계약과 `cardId`, `currency`, `cost`, `missions`, `achievements`.
- 지갑 차감, 카드 소유, 초기 성장, 소유 관련 가이드, 도감 업적, 영수증이 동일 트랜잭션에서 처리된다. 팩 개봉 미션·업적은 증가하지 않는다.
- 보유 카드 `AlreadyOwned`, 잔액 부족 `NotAffordable`, 제작 불가 카드 `CardNotCraftable`, 다른 카드에 같은 txId 재사용 `TxIdReused`는 `permission-denied`와 `details.reason`으로 거절한다.
- 잘못된 인자는 `invalid-argument`, 제작 표 누락·비정상 가격은 `unavailable`이다. 기본 가격이나 무료 제작으로 대체하지 않는다.
- 응답 유실 직후의 동일 요청은 영수증을 재생하고 추가 차감하지 않는다. 레시피가 이후 비활성화되어도 영수증이 유효하면 재생한다. 기존 세이브 계약상 중간에 다른 세이브 변경으로 revision이 진행된 경우 `failed-precondition`으로 재동기화를 요구한다.
- 영수증 만료 이후에도 이미 가진 카드는 다시 차감하지 않는다. 같은 카드 또는 같은 지갑에 대한 동시 요청은 트랜잭션 충돌 검증으로 재판정한다.

## 발행 순서

1. **default와 currency 두 Functions 코드베이스**에 CardDust를 인식하는 코드를 먼저 배포한다. 구 currency 서버는 알 수 없는 재화 키를 정규화 중 제거할 수 있다.
2. CSV 발행 경로로 CardCraft 및 변경된 Reward를 같은 환경에 발행한다. 신규 CardCraft는 서버 전용이며 생성 C# 테이블/SpecData.bytes에 넣지 않는다.
3. 환경별 callable 검증 뒤 클라이언트에서 재화 표시·제작 API를 연결한다. 이번 구현에는 UI 및 클라이언트 재화 enum/DTO 변경이 포함되지 않는다.

기존 앱은 CardDust를 표시하지 못한다. 서버 지갑에는 쌓이지만 중복 보상이 사라진 것처럼 보이므로, 사용자에게 보이는 환경의 Reward 전환은 클라이언트 대응과 함께 공개한다. 서버 전용 표 및 CardDuplicate 보상은 기존 클라이언트의 스펙 초기화를 막지 않는다.

잔액 필드가 없는 기존 지갑은 CardDust 0으로 읽는다. 일괄 세이브 마이그레이션은 필요 없다. CardDust 지급 이후에는 이전 재화 코드로 되돌리지 말고 두 코드베이스의 신규 키 지원을 유지한다.

## 로컬 검증

- 두 Functions 코드베이스의 lint/build.
- `node --test functions/scripts/test-card-crafting.js functions/scripts/test-card-dust.js`
- `node functions/scripts/test-publish-all-csv-spec.js`
- 로컬 Firestore 에뮬레이터와 `demo-*` 프로젝트에서 `functions/scripts/test-card-crafting-emulator.js` 실행.

이번 변경 검증 결과: 신규 경제·제작 단위 테스트 11개, 실제 Firestore 트랜잭션 테스트 15개, CSV 발행 오프라인 검증 38개 통과. 서버 두 코드베이스 lint/build 통과. Unity 편집기 연동 응답이 없어 C# 업로더의 Unity 컴파일 확인은 완료하지 못했다.
