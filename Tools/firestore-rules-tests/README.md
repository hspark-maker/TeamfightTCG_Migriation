# firestore.rules.prod 회귀 테스트

`firestore.rules.prod` 가 실제 세이브 문서 스키마와 어긋나지 않았는지 검증한다.

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
`rules.test.js` 의 `initializeTestEnvironment` 가 `../../firestore.rules.prod` 를 읽어
에뮬레이터에 **직접 주입**하는 쪽이다. 루트 `firebase.json` 이 어떤 룰을 가리키든 무관하다.
(firebase-tools 가 프로젝트 디렉터리 밖 경로를 `rules` 로 안 받아서 자리채움이 필요했다.)

## 로그의 `evaluation error` 는 정상이다

거부 케이스 로그에 `evaluation error at L66:24` 처럼 룰 줄번호를 가리키는 줄이 섞인다.
`FieldValue.ServerTimestamp` 가 붙은 쓰기는 룰이 두 번 평가되는데, 값이 아직 안 풀린
첫 패스에서 `updatedAt == request.time` 이 에러가 된다. 최종 판정은 두 번째 패스가 낸다.
통과 케이스(1·2·9c·14)가 실제로 통과하므로 룰은 정상이다.

## 룰을 고쳤으면

1. `npm test` 로 30개 전부 통과 확인
2. **일부러 깨보기** — 셋 다 해보면 테스트가 룰을 실제로 물고 있는지 확인된다:
   - `hasOnly` 목록에서 슬롯 키 하나 제거 → **1·2·9c·14** 가 깨져야 한다
   - `hasAll` 을 메타 5키로 줄이고 슬롯 검사를 `!('x' in ...) ||` 형태로 되돌림
     (= 감사 이전 상태) → **7c·7d**(세이브 비우기 우회) 가 깨져야 한다
   - `allow create` 의 `schemaVersion == 7` 줄 제거 → **8b** 가 깨져야 한다
3. 세이브 도메인(`OutGame/Save/2.Domain/*SaveData.cs`)이 바뀌었으면
   `fixtures/saveDocument.js` 를 먼저 맞춘다
4. **`UserSaveData.VERSION` 을 올렸으면 룰의 `allow create` 안 `schemaVersion == 7` 도 같이 올려라.**
   안 올리면 기존 계정은 멀쩡한데 신규 계정만 안 만들어지는 부분 고장이 된다 — `8b` 가 잡는다
