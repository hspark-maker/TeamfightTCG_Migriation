# 티어 기반 멀티 매치메이킹 — 설계 확정본 (구현 대기)

작성 2026-08-25. 브랜치 `orch/agent/s9-room-room`. **코드 변경 없음 — 설계만 확정.**
중단 사유: 전날 작업 수정사항 우선 처리. 재개 시 이 문서부터 읽는다.

## 1. 현재 실태 (실측, 재조사 불필요)

매칭 진입이 **두 경로로 갈라져 있다.**

| 경로 | 진입 | 상대 | 티어 반영 |
|---|---|---|---|
| 정식 로비 | `UI/Lobby/LobbyMatchLauncher.cs:250` → `MatchmakingShell.RunMatchAsync(IMatchmaker)` | AI | O (`RankManager.TierIndex`) |
| 멀티 | `UI/MainMenu/RandomMatchPanel.cs:40` → `NetworkSession.JoinRandomRoom()` | 사람 | **X** |

핵심 사실:

- `Network/NetworkSession.cs:42-58` `JoinRandomRoom` — `GameMode.Shared`, `SessionName = null`,
  `CustomLobbyName = "RandomMatch"`, `PlayerCount = 2`. `SessionProperties` **없음** → 아무나 랜덤 조인.
- `Network/NetworkSession.cs:127` `OnSessionListUpdated` — **빈 구현**. 자리만 있음.
- `Network/NetworkSession.cs:98` `BuildStartGameArgs` — 코드 매칭용 별도 조립(`CustomLobbyName = "CodeMatch"`).
- 랭크: `OutGame/Rank/RankConfig.cs:223` `ERankGrade` 5종(Bronze/Silver/Gold/Platinum/Diamond)
  × `RankConfig.cs:10` `DivisionsPerGrade = 4` = **티어 20단계**.
  점수 `winPoints = 10` / `losePoints = 10` (1승 = 별 1칸, 승패 대칭). **숨은 MMR 없음.**
- `OutGame/Match/IMatchmaker.cs` 계약:
  `UniTask<MatchOpponent?> FindOpponentAsync(CancellationToken _ct)` — 취소·실패는 **null 반환, 예외 금지**.
- `MatchOpponent.IsValid` 주석에 이미 명시: *"실제 매칭으로 갈아끼우면 상대 덱이 배틀 씬(SyncInitialDecks)에서
  도착해 여기는 빈 채로 온다"* → 실 매칭 대비가 설계에 들어가 있다.

## 2. 확정 결정 (Claude·Codex 합의)

1. **매칭 버킷 키 = 등급 5개** (`ERankGrade`). TierIndex 20단계 아님 — 동접 적으면 풀이 굶는다.
   Division 단위는 운영 지표 쌓인 뒤 동접 충분한 구간만 좁힌다.
2. **대기시간 기반 밴드 확대**

   | 대기 | 탐색 범위 |
   |---|---|
   | 0~10초 | 같은 등급만 |
   | 10~25초 | 인접 등급(±1) |
   | 25~35초 | 전체 등급 |
   | 35초~ | `FakeMatchmaker` AI 폴백 |

3. **구현 방식 = 커스텀 로비 세션 목록**.
   Fusion `SessionProperties` 완전일치 필터는 밴드 확대가 안 된다(등급마다 조인 재시도 필요).
   대신 `JoinSessionLobby(SessionLobby.Custom, "RandomMatch")` → `OnSessionListUpdated` 로 목록 수신 →
   밴드 안 + 빈자리 있는 세션을 골라 `SessionName` 지정 조인 → 없으면 내 등급 프로퍼티 달고 생성.
4. **구조**

   ```text
   LobbyMatchLauncher
     └ FallbackMatchmaker
          ├ PhotonMatchmaker (35초)
          └ FakeMatchmaker
   ```

   `RandomMatchPanel → NetworkSession.JoinRandomRoom()` 직접 진입은 **폐기**.
   멀티·AI 모두 `IMatchmaker` 한 계약으로 통합 → 취소·`MatchOpponentHandoff` 규약도 한 벌.
5. **세션 프로퍼티**: `grade`(int), `wireVersion`(int), 생성 시각.
   `wireVersion` 불일치는 **절대 매칭 금지** — `NetworkGameController.HandleMessage` 포맷이 바뀌면 divergence.
   이 검사는 밴드 확대와 무관하게 항상 완전일치.
6. **숨은 MMR 미도입**. 랭크 포인트가 이미 승패 대칭(±10)이라 MMR 역할을 한다.
   나중에 `RankSaveData` 필드 추가로 분리 가능.
7. **동시 방 생성 레이스 방지**: 생성 전 0~0.6초 랜덤 지연(`UnityEngine.Random` 명시 —
   전투 결정론 RNG `MatchRandom` 오염 금지) + 대기 중 5초마다 목록 재확인,
   더 오래된 적합 방 발견 시 그쪽으로 이사.

## 3. 반드시 지킬 제약

- `IMatchmaker` 계약 유지 — 취소·실패는 null, 예외 던지지 않는다.
- 사람 매칭 시 `MatchOpponent.Deck` 은 **비운다**. 덱은 배틀 씬 `SyncInitialDecks` 로 도착.
  `IsValid == false` 경로(덱 미리보기 생략)가 살아 있어야 한다.
- 매칭 지연·지터에 `UnityEngine.Random` 을 명시적으로 쓴다(`FakeMatchmaker` 선례 따름).
- 씬·프리팹 변경 범위 최소화(머지 대상은 main).

## 4. 다음 단계 (재개 시 순서)

1. `PhotonMatchmaker : IMatchmaker` 작성 —
   `OnSessionListUpdated` 수신 → 밴드 필터 → `SessionName` 조인 / 없으면 지터 후 생성 / 이사 로직.
2. `NetworkSession` 배선 — `JoinSessionLobby` 추가, `SessionProperties(grade, wireVersion, createdAt)` 주입,
   `JoinRandomRoom`/`BuildStartGameArgs` 의 `StartGameArgs` 조립을 **한 곳으로 단일화**.
3. `FallbackMatchmaker` 데코레이터 작성, `LobbyMatchLauncher.cs:73` `Matchmaker` 프로퍼티 교체.
4. `RandomMatchPanel` 경로 폐기 + `MainMenuManager` 쪽 참조 정리.
5. `net-engineer` + `architecture-engineer` 병렬 검수 → unity-mcp 컴파일 확인.

## 5. 미결

- 세션 프로퍼티 키 이름·타입 확정 (`grade`:int, `wire`:int, `born`:int 제안).
- 밴드 확대 시간값(10/25/35초) 저작 위치 — SO로 뺄지 상수로 둘지.
- `RandomMatchPanel` 폐기 시 `MainMenuManager` 참조 정리 범위 미조사.
- 신규 유저 보호 풀(첫 N판 AI/신규끼리) 도입 여부.

## 6. 작업 중 발견한 구조 부채 (별건, 이 작업이 일부 해소)

- 매칭 진입 이중 경로 — 위 4번으로 해소 예정.
- `NetworkSession.cs:47` / `:98` `StartGameArgs` 이중 조립 — 프로퍼티 추가 시 한쪽만 고칠 위험. 2번에서 단일화.
- `RandomMatchPanel` 매칭 타임아웃·재시도 없음(`:40` 실패 시 statusText만 변경),
  `OnEnable` async 시작인데 CancellationToken 없어 패널 파괴 후에도 콜백 생존. 폐기로 해소.
