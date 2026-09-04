# Battle Replay Cloud Run

Unity와 같은 `Assets/Scripts/BattleCore`를 실행하는 비공개 Cloud Run 서비스다. TS 전투 시뮬레이터를 호출하지 않는다.

## 계약

`POST /v1/battle/replay`

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

실행 서비스 계정에는 named Firestore DB `cardbattle`의 spec 문서를 읽을 최소 권한이 필요하다. Functions에서 호출할 때는 Cloud Run Invoker 권한과 ID 토큰을 사용한다. 현재 섀도 단계에서는 정산 트랜잭션이 끝난 뒤 재생을 호출하며, 권위 전환 때는 재생 결과를 먼저 확보한 뒤 별도 정산 트랜잭션에 반영하는 2단계 흐름으로 바꾼다.

Functions에는 `BATTLE_REPLAY_URL`을 Cloud Run 서비스 기본 URL로 설정한다. 커스텀 audience를 쓸 때만 `BATTLE_REPLAY_AUDIENCE`를 추가한다. `BATTLE_REPLAY_BEARER_TOKEN`은 Firebase emulator에서만 읽히며 배포 환경에서는 무시된다.
