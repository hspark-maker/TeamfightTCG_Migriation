# Battle Replay Cloud Run

Unity와 같은 `Assets/Scripts/BattleCore`를 실행하는 비공개 Cloud Run 서비스다. TS 전투 시뮬레이터를 호출하지 않는다.

## 계약

`POST /v1/battle/replay`

재생기는 **완성된 덱 두 벌(owner 0/1)**만 받는다. 카드 구성, AI 덱 선택, 카드 레벨·성장치 적용은
호출자인 Functions의 책임이며, 이 서비스는 전달받은 카드 스냅샷과 보드 순서로 시뮬레이션만 수행한다.
현재 솔로 AI 덱은 `deckValidation.ts`의 `buildAiDeckSnapshots()`가 완성해 요청의 두 번째 덱으로 보낸다.

- 요청: `env`, `rulesetVersion`, `contentFingerprint`, `specPins`, `seedHex`, `decks`, `commandLog`(base64)
- 응답: 승패, 잔존 카드, owner별 파괴 수, 최종 상태 해시, RNG draw count
- `contentFingerprint`는 현재 클라이언트 계약과 동일하게 `_index.tables.Card.payloadHash`로 전투 지문을 계산해 검증한다.
- 실제 규칙 객체는 매치 생성 시 고정한 `specPins`의 `Card`, `SynergyDef`, `SynergyTierDef`, `SynergyEffectDef` 네 불변 blob만 읽어 만든다. 현재 `_index`로 우회하지 않는다.
- 파싱 결과는 고정된 네 표의 payloadHash 조합을 키로 보관한다. 따라서 같은 릴리스는 재사용하고 다른 릴리스는 별도 캐시된다. 요청 밖 백그라운드 갱신은 없다.

강제 캐시 초기화는 새 Cloud Run revision 배포로 수행한다. 서비스 자체를 Cloud Run IAM으로 비공개 유지해야 한다.

## 빌드 및 배포

저장소 루트를 Docker build context로 사용한다.

```powershell
gcloud builds submit `
  --project bm-cardbattle `
  --config Services/BattleReplay/cloudbuild.yaml `
  --substitutions _TAG=manual `
  .

gcloud run deploy battle-replay `
  --project bm-cardbattle `
  --region asia-northeast3 `
  --image asia-northeast3-docker.pkg.dev/bm-cardbattle/backend/battle-replay:manual `
  --no-allow-unauthenticated `
  --min 1 `
  --set-env-vars FIRESTORE_DATABASE_ID=cardbattle
```

## 운영 토글과 비용 제어

Functions의 호출 스위치는 Firestore `envs/{envId}/config/battleReplay.enabled`다. 문서가 없으면 off이며,
Functions 인스턴스별로 최대 60초 캐시한다. Unity의 `Tools > Card Battle > 릴리즈 관리`에서 환경별로
상태 조회와 켜기/끄기, 최근 7일 발산 집계를 확인할 수 있다.

켜는 순서는 Cloud Run 최소 인스턴스를 1로 올린 뒤 Firestore 토글을 켜는 것이다. 끌 때는 반대로
Firestore 토글을 먼저 끄고 60초 기다린 뒤 최소 인스턴스를 0으로 내린다. 토글이 off면 Cloud Run 호출
자체가 생략되므로 대역폭도 발생하지 않지만, 정산 권위는 두 클라이언트 합의로 후퇴한다.
현재 `live`와 `test`는 같은 `BATTLE_REPLAY_URL`을 공유하며 요청의 `env`로 데이터만 분리한다. 따라서 두 환경은
같은 Cloud Run 인스턴스 예산과 장애 범위를 공유한다.

최소 인스턴스는 릴리즈 관리 창이 `gcloud` CLI 를 **직접 실행**해 조회·변경한다(`CloudRunControl`).
"상태 조회" 가 읽는 `minScale` 이 Cloud Run 의 실측값이므로, 창의 토글 상태와 실제 과금 상태를 따로 본다.
gcloud 인증이 만료되면 창이 그 사유를 표시하고 `gcloud auth login` 을 띄우는 버튼을 내놓는다.
CLI 가 없는 환경을 위해 같은 명령을 복사하는 버튼도 남겨 뒀다.

```powershell
gcloud run services update battle-replay --project bm-cardbattle --region asia-northeast3 --min 1
gcloud run services update battle-replay --project bm-cardbattle --region asia-northeast3 --min 0
```

이 서비스는 `live` 와 `test` 가 공유하므로 최소 인스턴스 변경은 두 환경에 함께 적용된다.

일별 집계는 UTC 기준 `envs/{envId}/telemetry/replayDaily/days/{yyyy-MM-dd}`에 저장된다. 단일 일자 문서는
트래픽이 커지면 쓰기 경합이 생길 수 있으므로, 지속적으로 초당 1건을 넘기기 전에 샤딩해야 한다.

실행 서비스 계정에는 named Firestore DB `cardbattle`의 spec 문서를 읽을 최소 권한이 필요하다. Functions에서 호출할 때는 Cloud Run Invoker 권한과 ID 토큰을 사용한다.

## 정산 경로 (권위)

`submitMatchResult` 는 2단계로 돈다. 재생이 HTTP 호출이라 Firestore 트랜잭션 안에서 돌릴 수 없기 때문이다.

1. 트랜잭션이 재생 입력(덱·시드·명령 로그·보드 순서·specPins)을 조립하고, 아무것도 쓰지 않은 채 요청 지문과 함께 물러난다.
2. 트랜잭션 밖에서 `POST /v1/battle/replay` 를 호출한다.
3. 같은 트랜잭션 본문에 재생 결과를 넣어 다시 들어간다. 지문이 어긋나면(동시 제출로 입력이 바뀌었다) 결과를 버린다. 두 번째에도 어긋나면 아무것도 쓰지 않고 `unavailable`로 내려 클라이언트 재제출에 맡긴다.

응답 처리:

| 응답 | 정산 |
| --- | --- |
| 200 `ok:true` | 승패·잔존·무승부의 진실원. 이 값으로 지급한다 |
| 409 / 422 | 재현 가능한 거절. 매치를 `flagged`(`server_simulation_<reason>`)로 닫는다 |
| 400 | 호출자 계약 결함. `replay_request_<reason>`으로 기록하고 매치는 열어 둔다 |
| 503 · 5xx · 타임아웃 · 미배선 | **매치를 닫지 않는다.** 제출만 보존하고 `pending` 으로 열어 둔 뒤 문서에 `replayUnavailable` 을 남긴다 — 서비스 장애로 멀쩡한 플레이어의 보상을 지우지 않기 위해서다 |

토글이 켜진 상태에서 `BATTLE_REPLAY_URL` 이 비어 있으면 ruleset 2 이상 매치는 **정산되지 않는다**. Functions에는 이 값을 Cloud Run 서비스 기본 URL로 반드시 설정한다. 커스텀 audience를 쓸 때만 `BATTLE_REPLAY_AUDIENCE`를 추가한다. `BATTLE_REPLAY_BEARER_TOKEN`은 Firebase emulator에서만 읽히며 배포 환경에서는 무시된다.

## 규칙 회귀

전투 규칙 사본은 이제 하나뿐이라(`Assets/Scripts/BattleCore`) 사본 간 대조 테스트는 없다. 대신 Unity 가 캡처한 골든 코퍼스로 회귀를 막는다.

```powershell
cd functions; npm run test:battle-golden
```
