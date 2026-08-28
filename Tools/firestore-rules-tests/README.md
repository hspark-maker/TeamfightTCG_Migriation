# firestore.rules 회귀 테스트

`firestore.rules` 가 실제 세이브 문서 스키마와 어긋나지 않았는지 검증한다.

룰이 조용히 낡으면 배포하는 날 전 유저 저장이 거부된다 — 커밋 `809d040d3` 이 그 사고였고,
그때는 운영에 전면 개방 룰이 올라가 있어서 증상이 안 보였다. 이 테스트가 룰과
`Assets/Scripts/OutGame/Save/4.Cloud/PlayerSaveDocument.cs` 의 `ToFieldMap` 을 묶어 둔다.

## 돌리는 법

```bash
npm install
npm test
```

**Java 21 이상이 PATH 에 있어야 한다.** firebase-tools 15 는 그 아래를 거부한다.
Unity 번들 JDK(`.../PlaybackEngines/AndroidPlayer/OpenJDK`)는 17이라 못 쓴다.
이 머신에서는 Rider 번들 JBR 로 돌렸다:

```bash
export PATH="/c/Program Files/JetBrains/JetBrains Rider 2026.1.4/jbr/bin:$PATH"
npm test
```

`firebase login` 은 필요 없다. 에뮬레이터만 쓴다.

## 다른 에뮬레이터와 안 부딪힌다

- 포트 **8081** (루트 `firebase.json` 은 8080)
- 프로젝트 id **`tcg-rules-test`**, `singleProjectMode: false`
- hub·logging 포트는 CLI 가 알아서 비켜 잡는다

`emulator-bootstrap.rules` 는 자리채움일 뿐이다(전부 거부). 진짜 검증 대상은
`rules.test.js` 의 `initializeTestEnvironment` 가 `../../firestore.rules` 를 읽어
에뮬레이터에 **직접 주입**하는 쪽이다(`RULES_FILE` 환경변수로 다른 파일을 겨눌 수도 있다 —
다른 브랜치 룰을 대조할 때 쓴다).
(firebase-tools 가 프로젝트 디렉터리 밖 경로를 `rules` 로 안 받아서 자리채움이 필요했다.)

## 로그의 `evaluation error` 는 정상이다

거부 케이스 로그에 `evaluation error at L66:24` 처럼 룰 줄번호를 가리키는 줄이 섞인다.
`FieldValue.ServerTimestamp` 가 붙은 쓰기는 룰이 두 번 평가되는데, 값이 아직 안 풀린
첫 패스에서 `updatedAt == request.time` 이 에러가 된다. 최종 판정은 두 번째 패스가 낸다.
통과 케이스(2·9c·14)가 실제로 통과하므로 룰은 정상이다.

## 픽스처는 반드시 클라 실제 산출물이어야 한다

`fixtures/saveDocument.js` 를 손으로 그럴듯하게 지어내면 **옳은 룰을 틀렸다고 판정**한다.
한 번 데였다 — 재화를 `{ Gold: 100 }` 으로 뒀다가 "기준 룰이 신규 계정을 전부 막는다"는
오판을 냈다. 실제로는 `CurrencySaveData.Normalize` 가 4재화를 다 채운다.
의심스러우면 에뮬레이터에 클라를 붙여 만들어진 문서를 그대로 떠 와라.

## create 는 서버만 한다 (R4~)

문서 생성은 `functions/src/commands/ensureAccount.ts`(Admin SDK)가 하고 룰은 `allow create: if false` 다.
**Admin SDK 는 룰을 안 타므로 이 하네스는 서버 쓰기 자체를 볼 수 없다.** 여기서 고정하는 것은 셋뿐이다:

1. 클라 create 는 어떤 페이로드로도 거부 — `1` · `6` · `8b` · `14d`
2. 서버가 만든 문서 위에서 클라 update 가 통과 — `14` · `14c` · `9c`
3. update 계약 전수 검증 — 나머지

서버 산출물이 실제로 그 모양인지는 반대편에서 `functions/scripts/test-fresh-account.js` 가
`fixtures/saveDocument.js` 의 `serverFreshAccountDocument()` 와 필드 단위로 대조해 못박는다.
**두 파일은 쌍둥이다 — 한쪽을 고치면 다른 쪽도 고쳐야 한다.**

> **create 를 닫으면 페이로드 검증이 공허해질 수 있다.** 거부 케이스를 seed 없이 create 로 쓰면
> "create 라서" 실패해 통과한다 — `isValidSave()` 를 통째로 지워도 초록이 된다.
> 그래서 페이로드 검증 케이스는 전부 `seed(1)` + `saveDocument(2, {위반})` 의 **update 기반**이다.
> 새 거부 케이스를 추가할 때도 이 규칙을 지켜라.

## 룰을 고쳤으면

1. `npm test` 로 33개 전부 통과 확인 — 판정은 종료코드가 아니라 **`# pass 33`** 줄로 한다
2. **일부러 깨보기** — 테스트가 룰을 실제로 물고 있는지 확인한다:
   - `isValidSave()` 를 `return true` 로 무력화 → **7 · 7b · 7c · 7d · 10 · 11 · 11b · 13 · 13b · 14b**
     열 개가 깨져야 한다. 안 깨지면 그 케이스가 create 기반으로 되돌아간 것이다
   - 재화 4키 검증 중 `balances.Diamond is int` 줄 제거 → **13b** 가 깨져야 한다
   - `allow create` 를 `if true` 로 되돌리기 → **1 · 6 · 8b · 14d** 가 깨져야 한다

   원본을 안 건드리고 검증하려면 훼손본을 만들어 `RULES_FILE=<경로> npm test` 로 겨눈다.

   `hasAll` 15키와 `revision > 0` 은 빼도 안 깨진다 — 슬롯별 검증이 같은 구멍을 이미 막는다.
   명시성·방어 겹으로 남겨 둔 것이지 단독으로 뭘 막고 있지는 않다.
3. 세이브 도메인(`OutGame/Save/2.Domain/*SaveData.cs`)이 바뀌었으면
   `fixtures/saveDocument.js` 를 먼저 맞춘다
4. **`UserSaveData.VERSION` 을 올렸으면 `functions/src/save/saveDocument.ts` 의 `SCHEMA_VERSION` 도
   같이 올려라.** R4 부터 새 문서의 버전 앵커는 룰이 아니라 그 상수다 — 룰의 `allow update` 는
   `>=` 라 단조 증가만 막을 뿐 새 문서의 값을 고정하지 못한다.
