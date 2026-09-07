# TeamfightTCG — 아키텍처

> Unity + Photon Fusion(Shared) 기반 **1v1 3슬롯 카드 배틀러**
> 약 90개 스크립트 / 7,000줄 · 비동기 **UniTask** · 트윈 **DOTween**
> 다이어그램 중심 문서. 세부 규칙의 진실원은 항상 코드입니다.

---

## 목차

1. [한눈에 보기](#1-한눈에-보기)
2. [레이어 구조](#2-레이어-구조)
3. [디렉터리 지도](#3-디렉터리-지도)
4. [게임 흐름](#4-게임-흐름)
5. [전투 도메인](#5-전투-도메인)
6. [네트워크 아키텍처](#6-네트워크-아키텍처)
7. [뷰 · 입력 레이어](#7-뷰--입력-레이어)
8. [횡단 관심사](#8-횡단-관심사)
9. [핵심 불변식](#9-핵심-불변식--깨면-안-되는-규칙)
10. [알려진 리스크](#10-알려진-리스크)
11. [작업 시작점 치트시트](#11-작업-시작점-치트시트)

---

## 1. 한눈에 보기

```
                        TeamfightTCG
                              │
      ┌───────────────┬───────┴───────┬───────────────┐
      │               │               │               │
   ⚔️ Battle       🃏 Card        🌐 Network       🖼️ UI
      │               │               │               │
  TurnRunner      CardData(SO)   Fusion Shared    CardView
  Attack 4종      CardInstance   바이너리 프로토콜  BattleFieldView
  BattleField     ★규칙진실원★   commit-reveal    MainMenu 로비
  MatchRandom     Keyword 9종    락스텝 재시뮬     UIPoolManager
                  Passive 9종
```

| 항목 | 값 |
|---|---|
| 네트워크 | Photon Fusion 2 · `GameMode.Shared` · `PlayerCount = 2` |
| 네트워크 모델 | **결정론 락스텝** (Fusion을 전송로로만 사용, `NetworkObject` 0개) |
| 필드 | 슬롯 3 (`BattleField.SLOT_COUNT`) + 대기큐 |
| 덱 | 6장 (`DeckSaveManager.DECK_SIZE`) · 저장 슬롯 6 |
| 씬 | `MainMenu` → `BattleScene` (+`AttackAnimTest`, `ZZamTong`) |
| 어셈블리 | asmdef 없음 → `Assembly-CSharp` 단일, 전역 네임스페이스 |

> **게임 규칙의 특이점**: 카드의 **체력(hp)이 곧 공격력**.
> 공격 = 상호 체력 교환이며 반격이 동시 해결됩니다.

---

## 2. 레이어 구조

```
┌── 🖼️ UI ── 뷰 · 입력 ────────────────────────────────────────────┐
│                                                                  │
│   CardView(838줄)   BattleFieldView   CardAnimator               │
│   MainMenu 패널들    UIPoolManager     DeckPileUI                 │
│                                                                  │
└──────────┬──────────────────────────────────────▲────────────────┘
           │                                      │
      읽기 │ TurnState (InputAllowed,        발행 │ static event
      구독 │            ForcedAttacker,           │ CardView.OnAttack
           │            LocalOwnerIndex)          │
           │ TurnEvents                           │
           ▼                                      │
┌── ⚔️ Battle ── 게임 규칙 · 상태 ─────────────────┴────────────────┐
│                                                                  │
│   TurnRunner ──▶ TurnBase 4종                                    │
│   AttackFlow · AttackSequence(연출) · AttackProcessor(규칙)       │
│   BattleField · MatchRandom · HealerEffect                       │
│                                                                  │
│   ┌─ static 권위체 ─────────────────────────┐                    │
│   │  TurnState (입력 게이팅)                │                    │
│   │  TurnEvents (Observer subject)          │                    │
│   └─────────────────────────────────────────┘                    │
│                                                                  │
└──────────┬───────────────────────────────────────────▲───────────┘
           │ TurnContext로 뷰 주입 (위로)               │
           ▼                                            │
┌── 🃏 Card ── 데이터 ────────────────────────────────┐ │
│                                                     │ │
│   CardData(SO)      CardInstance ★규칙 단일진실원★  │ │
│   CardKeyword(Flags)                                │ │
│                                                     │ │
└─────────────────────────────────────────────────────┘ │
           ▲                                            │
           │                                            │
┌── 🌐 Network ── 전송 ──────────────────────────────────┴─────────┐
│                                                                  │
│   NetworkSession ──── Runner 수명 · 콜백 → C# 이벤트             │
│   NetworkGameController ── 와이어 프로토콜 (직렬화/역직렬화)      │
│   MultiplayerTurnRunner ── 동기화 상태기 (버퍼 · TCS · 시드합의)  │
│   CardRegistry ────── CardData SO ↔ int ID 매핑                  │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### 의존 규칙 (실제로 지켜지고 있음)

| 규칙 | 설명 |
|---|---|
| `Card`는 위를 모름 | 카드 규칙은 상위 연출·UI 계층을 직접 호출하지 않음 |
| `UI → Battle`은 **간접** | 직접 호출이 아니라 `TurnState` 조회 + `OnAttack` 이벤트 발행. `CardView`는 어떤 턴 클래스가 살아있는지 모름 |
| `Battle → UI`는 **주입** | `TurnContext`로 뷰 참조를 받음 |
| `Network → Battle`은 **경유** | 상태를 직접 밀지 않고 `MultiplayerTurnRunner`를 통과 |

---

## 3. 디렉터리 지도

```
Assets/Scripts/
│
├── Battle/ ─────────────────────────────── 게임 규칙 · 상태 · 턴 진행
│   │
│   ├── [턴]      TurnRunner.cs ············ 메인 턴 루프 (싱글/멀티 공용)
│   │             TurnBase.cs ·············· 추상 (OnEnter/Execute/OnExit)
│   │             PlayerTurn.cs ············ 싱글 - 내 턴
│   │             EnemyTurn.cs ············· 싱글 - AI 턴
│   │             TurnContext.cs ··········· 턴 클래스 공유 참조 묶음
│   │             TurnState.cs ············· 입력 게이팅 (static 권위체)
│   │             TurnEvents.cs ············ 턴 이벤트 허브 (Observer)
│   │
│   ├── [공격]    AttackFlow.cs ············ 4개 턴 클래스 공유 조각
│   │             AttackSequence.cs ········ 시네마 연출 오케스트레이션
│   │             AttackProcessor.cs ······· behavior 파사드
│   │             Attack/
│   │               ├── IAttackBehavior.cs ·· 인터페이스 + AttackResult
│   │               ├── AttackBehaviorFactory.cs  키워드 → behavior
│   │               ├── NormalAttack.cs ····· 기본 (나머지 3개의 베이스)
│   │               ├── RangedAttack.cs ····· 원거리: 무반격
│   │               ├── PeerlessAttack.cs ··· 무쌍: 인접 광역
│   │               └── CunningAttack.cs ···· 교활: 공격 후 교체
│   │
│   ├── [상태]    BattleField.cs ··········· 3슬롯 + 대기큐
│   │             MatchRandom.cs ··········· 결정론 RNG + commit-reveal
│   │             HealerEffect.cs ·········· 힐러 (TurnEvents 구독자)
│   │
│   └── [초기화]    GameInitializer.cs ······· BattleScene 초기화
│                 BattleIntro.cs ··········· 인트로 연출
│                 BattleCleanup.cs ········· static 상태 정리
│                 DeckConfig.cs ············ 선택 덱 + 멀티 플래그
│                 AIDeckConfig.cs ·········· 싱글 AI 덱 풀 (SO)
│                 ※ DeckSaveManager는 OutGame/Deck/으로 이동(2026-07-30)
│
├── Card/ ───────────────────────────────── 카드 데이터 모델
│   │
│   ├── CardData.cs ························ 카드 정의 (SO)
│   ├── CardInstance.cs ··················· ★전투 규칙 단일 진실원★
│   ├── CardKeyword.cs ···················· Flags enum (9종)
│   ├── AttackEffect.cs ··················· 파티클 · 투사체 · 타이밍
│   ├── SynergyData.cs ···················· 시너지/클래스 메타
│   └── KeywordIconConfig.cs ·············· 키워드 → 아이콘/라벨
│
├── Network/ ────────────────────────────── Photon Fusion 전송 계층
│   │
│   ├── NetworkSession.cs ················· Runner 수명 + 콜백→이벤트
│   ├── NetworkGameController.cs ·········· ★와이어 프로토콜★
│   ├── MultiplayerTurnRunner.cs ·········· 동기화 상태기
│   ├── MultiplayerPlayerTurn.cs ·········· 멀티 - 내 턴
│   ├── MultiplayerOpponentTurn.cs ········ 멀티 - 상대 턴 (RPC 재생)
│   └── CardRegistry.cs ··················· CardData SO ↔ int ID (SO)
│
├── UI/
│   ├── Battle/ ··········· CardView · CardAnimator · BattleFieldView
│   │                      DeckPileUI · BattleCamera · TurnBannerUI
│   │                      GameResultPopup
│   ├── MainMenu/ ········· MainMenuManager · DeckBuilderUI · DeckGroup
│   │                      GameReadyPanel · MultiplayerLobbyPanel
│   │                      RandomMatchPanel · SceneTransitionVideo
│   ├── Input/ ············ LongPressDetector · SwipeGuide · HintArrow
│   ├── Keyword/ ·········· 키워드 설명 팝업
│   └── UIManager/ ········ UIPoolManager · PooledUIBase · UIAnimator
│
├── Audio/ ··············· SoundManager · SoundConfig · UIClickSound
├── Utils/ ··············· DataLibrary(Addressables) · ObjectPooler
│                          ParticlePooler · CameraUtil · LogUtil
└── Test/ ················ AttackAnimTester
```

---

## 4. 게임 흐름

### 4.1 씬 전이

```
      ┌─────────────────── MainMenu ───────────────────┐
      │                                                │
      │   덱 편집 ──DeckSaveManager.Save──▶ 덱 선택     │
      │                                       │        │
      │                            DeckConfig.Set      │
      │                                       ▼        │
      │                                  모드 선택      │
      │                    ┌──────────────┼──────────┐ │
      │                    ▼              ▼          ▼ │
      │                 싱글         방코드매칭   랜덤매칭│
      └────────────────────┼──────────────┼──────────┼──┘
                           │              │          │
                           │      JoinOrCreateRoom  JoinRandomRoom
                           │      로비 "CodeMatch"  로비 "RandomMatch"
                           │              │          │
                           │              ▼          ▼
                           │         ┌─────────────────┐
                           │         │  대기 (1/2명)   │◀─┐
                           │         └────────┬────────┘  │ 아직
                           │                  │ 2/2명      │
                           │      DeckConfig.SetMultiplayer(true)
                           │      마스터만 Runner.LoadScene()
                           │                  │
                           ▼                  ▼
      ┌────────────────── BattleScene ──────────────────┐
      │   초기화 ──▶ 턴 루프 ──▶ 승패 판정          │
      └────────────────────┬────────────────────────────┘
                           │ BattleCleanup.LoadScene
                           ▼
                        MainMenu
```

### 4.2 BattleScene 초기화 — `GameInitializer.StartBattleAsync()`

```
  ┌───────────────────────────────────────────────────────────────┐
  │ 1. BattleIntro.Await()                                        │
  ├───────────────────────────────────────────────────────────────┤
  │ 2. SceneTransitionVideo 종료 대기                             │
  │    └─ OnSpawn 사운드가 전환 영상과 겹치는 것 방지             │
  ├───────────────────────────────────────────────────────────────┤
  │ 3. 필드 초기화                                                │
  │                                                               │
  │    싱글 ─┬─ playerField.Initialize(내덱, 0)                   │
  │          └─ enemyField.Initialize(AI덱, 1)                    │
  │                                                               │
  │    멀티 ─┬─ MyOwnerIndex 확정 대기                            │
  │          ├─ playerField.Initialize(내덱, myIndex)             │
  │          └─ enemyField는 아직 비어 있음 (RPC로 채움)          │
  ├───────────────────────────────────────────────────────────────┤
  │ 4. InitializeViews()                                          │
  ├───────────────────────────────────────────────────────────────┤
  │ 5. [멀티만] 🔒 SyncInitialDecks()  ── 블로킹 랑데부            │
  │    ┌─────────────────────────────────────────────────┐        │
  │    │ a. 내 셔플 결과 ID 배열 broadcast               │        │
  │    │ b. 상대 덱 수신 대기 → InitializeFromRemote()   │        │
  │    │ c. SyncMatchSeed() — commit-reveal 시드 합의    │        │
  │    └─────────────────────────────────────────────────┘        │
  ├───────────────────────────────────────────────────────────────┤
  │ 6. BGM 재생 → BattleIntro.Play()                              │
  ├───────────────────────────────────────────────────────────────┤
  │ 7. TurnRunner.StartBattle()                                   │
  └───────────────────────────────────────────────────────────────┘
```

### 4.3 턴 루프 — `TurnRunner.RunTurns()`

```
   StartBattle()
        │  TurnCount = 1
        │  싱글이면 MatchRandom.SeedRandomLocal()
        │  멀티는 이미 commit-reveal로 시드됨
        ▼
  ╔═════════════════════ loop ═══════════════════════════════════╗
  ║                                                              ║
  ║  ┌────────────────────────────────────────────────────────┐  ║
  ║  │ 행동 주체 field 결정                                   │  ║
  ║  │  싱글: t_current == 0 ? playerField : enemyField       │  ║
  ║  │  멀티: playerField.OwnerIndex == t_current             │  ║
  ║  │          ? playerField : enemyField      ← ★역인덱싱★  │  ║
  ║  └────────────────────┬───────────────────────────────────┘  ║
  ║                       ▼                                      ║
  ║  TurnEvents.RaiseTurnStarted(field) ──▶ HealerEffect 등 반응 ║
  ║                       ▼                                      ║
  ║  각 카드 passive.OnTurnStart()   (justSpawned면 1회 스킵)    ║
  ║                       ▼                                      ║
  ║  턴 배너 연출                                                ║
  ║                       ▼                                      ║
  ║  ┌─────────────── 턴 클래스 선택 ────────────────────────┐   ║
  ║  │  싱글 · 내 턴  →  PlayerTurn                         │   ║
  ║  │  싱글 · AI     →  EnemyTurn                          │   ║
  ║  │  멀티 · 내 턴  →  MultiplayerPlayerTurn              │   ║
  ║  │  멀티 · 상대   →  MultiplayerOpponentTurn            │   ║
  ║  └──────────────────────┬───────────────────────────────┘   ║
  ║                         ▼                                    ║
  ║        OnEnter() → await Execute() → OnExit()                ║
  ║                         ▼                                    ║
  ║        disconnectWin || CheckGameOver() ? ──YES──▶ 승패 팝업 ║
  ║                         │ NO                        (종료)   ║
  ║                         ▼                                    ║
  ║        t_current == 1 ? TurnCount++ · 라벨 갱신               ║
  ║                         ▼                                    ║
  ║        t_current = 1 - t_current  ──────────────────┐        ║
  ╚═════════════════════════════════════════════════════╪════════╝
                                                        └─ 반복
```

> **승패 판정은 `CheckGameOver()` 한 곳에만** 있습니다.
> 턴 클래스는 전멸을 감지해도 `break`만 하고 판정을 위임합니다 (Execution 데드락 방지).

---

## 5. 전투 도메인

### 5.1 클래스 관계

```
   ┌──────────────────────┐          ┌──────────────────────────┐
   │  CardData (SO)       │◀─ data ──│  CardInstance            │
   ├──────────────────────┤          ├──────────────────────────┤
   │ displayName          │          │ hp / bonusHp             │
   │ keywords : Flags     │          │ slotIndex (-1 = 대기큐)  │
   │ maxHp / bonusHp      │          │ ownerIndex               │
   │ attackEffect         │          │ runtimeKeywords          │
   │                      │          │ justSpawned              │
   │ 보이스 · SFX 배열    │          │ savedHp / savedBonusHp   │
   └──────────────────────┘          ├──────────────────────────┤
                                     │ ★ 규칙 단일 진실원 ★     │
                                     │  AttackDamage()          │
                                     │  ClampDamage(raw)        │
                                     │  WouldDieFrom(raw)       │
                                     │  TakesCounterFrom(def)   │
                                     │  PreviewAfterDamage(dmg) │
                                     │  TakeDamage(dmg)         │
                                     └────────────▲─────────────┘
                                                  │ 0..*
                                     ┌────────────┴─────────────┐
                                     │  BattleField (Mono)      │
                                     ├──────────────────────────┤
                                     │ slots[3]                 │
                                     │ waitingQueue             │
                                     │ ownerIndex               │
                                     ├──────────────────────────┤
                                     │ FillEmptySlots()         │
                                     │ SwapWithWaiting()        │
                                     │ GetValidTargets()        │
                                     │ GetShuffledIds()   ┐멀티 │
                                     │ InitializeFromRemote()   │
                                     │ PlaceCardDirectly()┘     │
                                     └──────────────────────────┘

   ┌────────────────────┐      ┌──────────────────────────────┐
   │ «interface»        │      │ AttackResult (struct)        │
   │ IAttackBehavior    │─────▶├──────────────────────────────┤
   ├────────────────────┤ 반환 │ defenderKilled               │
   │ Execute(...)       │      │ canAttackAgain   (처형)      │
   └─────────▲──────────┘      │ attackerSwapped  (교활)      │
             │                 │ splashDefender   (무쌍)      │
   ┌─────────┴──────────┐      │ attackerKeywords (글로우용)  │
   │ NormalAttack       │      │ defenderKeywords (글로우용)  │
   ├────────────────────┤      └──────────────────────────────┘
   │ #MakeResult()      │
   │ #RemoveDead()      │      ┌──────────────────────────────┐
   └─────────▲──────────┘      │ «abstract» TurnBase          │
             │ 상속            ├──────────────────────────────┤
   ┌─────────┼──────────┐      │ #ctx : TurnContext           │
   │         │          │      │ OnEnter / Execute* / OnExit  │
 Ranged  Peerless   Cunning    └──────────────▲───────────────┘
 Attack   Attack    Attack                    │ 상속
                              ┌───────┬───────┴───────┬────────────┐
                          Player  Enemy   Multiplayer  Multiplayer
                           Turn    Turn   PlayerTurn   OpponentTurn
```

### 5.2 공격 파이프라인

```
  턴 클래스 (Player / Enemy / MultiplayerPlayer / MultiplayerOpponent)
      │
      │ ① AttackFlow.PreSelectSplash()
      │      └─ 무쌍 대상을 연출 전에 미리 확정 (연출·규칙 대상 일치 보장)
      │ ② AttackFlow.Keywords() → (preKw, atKw)
      │
      ▼
  ┌──────────────────────────────────────────────────────────────────┐
  │  AttackSequence.Play(뷰들, effect, onEffect 콜백, 키워드)        │
  │                                                                  │
  │   FadeAll → MoveToCenter → 시네마 진입 (BattleCamera)            │
  │        ▼                                                         │
  │   공격 애니 · 투사체 발사 · 사운드 · 파티클                      │
  │        ▼                                                         │
  │   hitDelay 대기                                                  │
  │        ▼                                                         │
  │  ╔══════════════════════════════════════════════════════════╗   │
  │  ║  ★ 규칙이 적용되는 유일한 지점 ★                          ║   │
  │  ║                                                          ║   │
  │  ║   onEffect() 호출                                        ║   │
  │  ║     └─▶ AttackProcessor.Execute()                        ║   │
  │  ║           └─▶ AttackBehaviorFactory.Create(attacker)     ║   │
  │  ║                 └─▶ behavior.Execute()                   ║   │
  │  ║                       └─▶ CardInstance 규칙 메서드들     ║   │
  │  ║                             ▼                            ║   │
  │  ║                        AttackResult                      ║   │
  │  ╚══════════════════════════════════════════════════════════╝   │
  │        ▼                                                         │
  │   히트 애니 → 사망 애니 → 시네마 이탈 → 원위치 복귀              │
  └──────────────────────────────────────────────────────────────────┘
      │
      │ ③ AttackFlow.RunAfterAttackPassives()
      │      └─ passive.OnAfterAttack → (처치 시) passive.OnKill
      │ ④ FillEmptySlots() + 뷰 갱신 + 채움 애니
      │      └─ [멀티] 여기서 BroadcastMySpawns()
      │ ⑤ AttackFlow.PlayResultFlourish()
      │      └─ 키워드 글로우 + 교활 등장 스케일 연출
      │ ⑥ [멀티] WaitForOpponentReady() → FlushEnemySpawns()
      ▼
   턴 계속 (처형) 또는 종료
```

> **연출과 규칙의 분리점** = `AttackSequence`가 받는 `Action _onEffect` 콜백.
> 연출은 *타이밍*만 알고, 규칙은 *콜백 안*에서만 실행됩니다.

### 5.3 behavior 선택 — 우선순위 하드코딩

```
   공격자 CardInstance
          │
          ▼
    ┌───────────┐  YES   ┌──────────────────────────────────┐
    │ Cunning?  ├───────▶│ CunningAttack                    │
    └─────┬─────┘        │  공격 후 대기카드와 교체         │
          │ NO           │  savedHp로 체력 보존             │
          ▼              └──────────────────────────────────┘
    ┌───────────┐  YES   ┌──────────────────────────────────┐
    │ Ranged?   ├───────▶│ RangedAttack                     │
    └─────┬─────┘        │  반격 없음                       │
          │ NO           └──────────────────────────────────┘
          ▼
    ┌───────────┐  YES   ┌──────────────────────────────────┐
    │ Peerless? ├───────▶│ PeerlessAttack                   │
    └─────┬─────┘        │  인접 슬롯에 원래 hp의 50% 광역  │
          │ NO           └──────────────────────────────────┘
          ▼
    ┌──────────────────────────────────────────────────────┐
    │ NormalAttack — 기본 상호 체력교환                    │
    └──────────────────────────────────────────────────────┘
```

> ⚠️ **복합 키워드 카드는 하나의 behavior만 탑니다.**
> 새 공격 유형 추가 시 이 우선순위를 반드시 확인하세요. (`AttackBehaviorFactory.cs`)

### 5.4 데미지 해결 — `NormalAttack` (동시 해결)

```
  Execute 진입
      │
      ▼
  ╔══════════════════════════════════════════════════════════════╗
  ║  📸 공격 전 수치 스냅샷                                       ║
  ║                                                              ║
  ║    t_atkDmg = attacker.AttackDamage()                        ║
  ║    t_ctrDmg = defender.AttackDamage()   ← 반격도 공격 전 수치 ║
  ║                                                              ║
  ║  hp = 공격력 이므로, 먼저 적용하면 뒤 값이 변함              ║
  ║  → 스냅샷으로 순서 의존을 제거                               ║
  ╚══════════════════════════════════════════════════════════════╝
      │
      ▼
  t_takesCounter = attacker.TakesCounterFrom(defender)
                   = !원거리(나) && !표식(상대)
      │
      ▼
  실제 적용량 계산 (ClampDamage — hp + bonusHp 상한)
      │
      ▼
  defender.TakeDamage(t_atkDmg)
      │
      ▼
  반격 자격? ──YES──▶ attacker.TakeDamage(t_ctrDmg)
      │ NO                    │
      ▼                       ▼
  패시브 통지: OnAttackedBy · OnDealDamage(반격 플래그)
      │
      ▼
  t_defKilled = (defender.hp == 0)
      │
      ▼
  RemoveDead(양쪽 필드)
      └─ 죽은 슬롯마다: passive.OnDeath() → field.RemoveCard()
      │
      ▼
  MakeResult()
      └─ canAttackAgain = 처치 && 공격자 생존 && Execution 키워드
      │
      ▼
  AttackResult
```

### 5.5 키워드 9종 (`CardKeyword`, `[Flags]`)

```
 ┌─ 🗡️ 공격형 ──────────────────────────────────────────────────┐
 │  Ranged     원거리    (1<<0)  반격 없음                      │
 │  Peerless   무쌍      (1<<1)  인접 슬롯에 원래 hp의 50%      │
 │  Execution  처형      (1<<2)  처치 시 재공격                 │
 │  Cunning    교활      (1<<4)  공격 후 교체 · 체력 보존       │
 └──────────────────────────────────────────────────────────────┘
 ┌─ 🛡️ 방어형 ──────────────────────────────────────────────────┐
 │  Taunt      도발      (1<<3)  피해 절반 · 우선 타겟 강제     │
 │  Mark       표식      (1<<5)  자신을 친 상대가 반격 면제     │
 │  Invincible 무적      (1<<7)  피해 1회 면역 (소모형)         │
 │  BonusHp    추가생명력 (1<<8) 추가 체력                      │
 └──────────────────────────────────────────────────────────────┘
 ┌─ 💚 지원형 ──────────────────────────────────────────────────┐
 │  Healer     힐러      (1<<6)  턴 시작 시 아군 1 회복         │
 └──────────────────────────────────────────────────────────────┘
```

### 5.6 패시브 훅 라이프사이클

```
   스폰            턴 시작              공격 해결 중
    │                │                      │
    ▼                ▼                      ▼
 OnSpawn      OnTurnStart          ┌─ OnAttackedBy  (피격자)
    │         (justSpawned면       ├─ OnDealDamage  (가해자, 반격 플래그)
    │          1회 스킵)           ├─ OnHit         (TakeDamage 내부)
    │                              └─ OnSwapOut     (교활 교체 성사)
    │                                     │
    └─────────────────┬───────────────────┘
                      ▼
              공격 직후 (AttackFlow)
                      │
                      ├─ OnAfterAttack
                      └─ OnKill   (defenderKilled 일 때만)
                      │
                      ▼
                    사망 (RemoveDead)
                      │
                      └─ OnDeath
```

**챔피언 패시브 9종**: Aatrox · Fizz · Gwen · Kindred · Maokai · Ornn · Poppy · Rammus · Teemo

### 5.7 BattleField — 슬롯 · 대기큐

```
   ┌────────────── BattleField (ownerIndex) ─────────────────┐
   │                                                         │
   │   ┌──────── slots[3] — 전장 ────────┐                   │
   │   │  ┌────┐   ┌────┐   ┌────┐       │                   │
   │   │  │ 0  │   │ 1  │   │ 2  │       │                   │
   │   │  └────┘   └────┘   └────┘       │                   │
   │   └──────▲──────────────┬───────────┘                   │
   │          │              │                               │
   │  FillEmptySlots()   RemoveDead()                        │
   │  빈 슬롯 순서대로    → passive.OnDeath                   │
   │          │           → RemoveCard()                     │
   │          │              │                               │
   │   ┌──────┴──────┐       ▼                               │
   │   │ waitingQueue│      💀                               │
   │   │  덱의 나머지 │                                       │
   │   └──────▲──────┘                                       │
   │          │                                              │
   │   SwapWithWaiting()  ← 교활: 나간 카드는 savedHp/       │
   │   (슬롯 ↔ 대기큐)        savedBonusHp에 체력 보존       │
   │                                                         │
   └─────────────────────────────────────────────────────────┘

   IsEmpty = 슬롯도 대기큐도 모두 빔  ──▶  패배 조건
```

---

## 6. 네트워크 아키텍처

### 6.1 핵심 설계 — Fusion을 "전송로"로만 사용

```
 ┌─ ✅ 사용하는 Fusion 기능 ─────┐   ┌─ ❌ 사용 안 하는 기능 ───────┐
 │                               │   │                              │
 │  NetworkRunner 수명           │   │  NetworkObject               │
 │  GameMode.Shared 세션/로비    │   │  NetworkBehaviour            │
 │  SendReliableDataToPlayer     │   │  [Networked] 프로퍼티        │
 │  Runner.LoadScene (마스터만)  │   │  Tick / Simulation           │
 │  IsSharedModeMasterClient     │   │  예측 · 롤백                 │
 │  OnPlayerJoined / Left        │   │  AOI                         │
 │                               │   │                              │
 └───────────────────────────────┘   └──────────────────────────────┘
```

### 6.2 결정론 락스텝 모델

```
  ┌── 📱 클라이언트 A ───────┐        ┌── 📱 클라이언트 B ───────┐
  │  (마스터, ownerIndex 0)  │        │      (ownerIndex 1)      │
  ├──────────────────────────┤        ├──────────────────────────┤
  │                          │        │                          │
  │   같은 시드              │◀══════▶│   같은 시드              │
  │   MatchRandom            │commit- │   MatchRandom            │
  │        │                 │ reveal │        │                 │
  │        ▼                 │        │        ▼                 │
  │   같은 입력              │◀══════▶│   같은 입력              │
  │   (공격 슬롯 쌍)         │ Attack │   (공격 슬롯 쌍)         │
  │        │                 │  13B   │        │                 │
  │        ▼                 │        │        ▼                 │
  │  ╔══════════════════╗    │        │  ╔══════════════════╗    │
  │  ║ 로컬 전투        ║    │        │  ║ 로컬 전투        ║    │
  │  ║ 시뮬레이션       ║    │        │  ║ 시뮬레이션       ║    │
  │  ╚══════════════════╝    │        │  ╚══════════════════╝    │
  │        │                 │        │        │                 │
  │        ▼                 │        │        ▼                 │
  │   같은 결과              │   ==   │   같은 결과              │
  └──────────────────────────┘        └──────────────────────────┘

  네트워크로 오가는 것은 "무엇을 했는가"(슬롯 쌍)뿐.
  데미지 · 사망 · 패시브 · 광역 대상은 양쪽이 각자 계산해 같은 결과에 도달.
```

### 6.3 와이어 프로토콜

`ReliableKey.FromInts(0x4255, 0, 0, 0)` 채널 · 직접 짠 **big-endian 바이너리**

```
  바이트 레이아웃 (Attack / CardSpawn — 13B)

   0        1        2        3        4        5   ...   12
  ┌────────┬────────────────────────┬────────────────────────┐
  │MsgType │  int #1 (4B, BE)       │  int #2, #3 (4B each)  │
  └────────┴────────────────────────┴────────────────────────┘
```

| MsgType | 값 | 페이로드 | 크기 | 용도 |
|---|---|---|---|---|
| `Attack` | 1 | attackerSlot, defenderSlot, cunningSwap | 13B | 공격 결정 |
| `CardSpawn` | 2 | slot, cardId, ownerIndex | 13B | 슬롯 보충 |
| `AnimReady` | 3 | — | 1B | **연출 동기화 배리어** |
| `InitialDeck` | 4 | ownerIndex, count, ids[count] | 9+4n B | 셔플 결과 공유 |
| *(결번)* | 5 | — | — | 사용 안 함 |
| `SeedCommit` | 6 | SHA256(nonce) | 33B | commit-reveal |
| `SeedReveal` | 7 | nonce | 9B | commit-reveal |

```
  송신 경로                              수신 경로
  ─────────                              ─────────
  턴 클래스                              NetworkSession
      │                                  .OnReliableDataReceived
      ▼                                        │
  NetworkGameController                        ▼
  .Send*()                               NetworkGameController
      │                                  .HandleMessage()
      ▼                                  MsgType switch
  byte[] 조립                                  │
  WriteInt (big-endian)                        ▼
      │                                  MultiplayerTurnRunner
      ▼                                  .On*Received()
  Runner.SendReliableDataToPlayer              │
  (LocalPlayer 제외 전원)                      ▼
      │                                  TCS 완료 또는 버퍼 적재
      └────────── 📡 Photon Cloud ─────────────┘
```

### 6.4 ownerIndex 규약

```
  ┌── 📱 기기 A (마스터) ────┐      ┌── 📱 기기 B (비마스터) ──┐
  │                          │      │                          │
  │  playerField             │      │  playerField             │
  │  OwnerIndex = 0  ────────┼──┐   │  OwnerIndex = 1  ────────┼──┐
  │                          │  │   │                          │  │
  │  enemyField              │  │   │  enemyField              │  │
  │  OwnerIndex = 1  ────────┼──┼───┼──────────────────────────┼──┘
  │                          │  │   │  OwnerIndex = 0          │
  └──────────────────────────┘  └───┼──────────────────────────┘
                                    │
              같은 논리적 필드를 서로 반대편에서 보고 있음
```

규약: `IsSharedModeMasterClient` → `ownerIndex 0` (선공) / 그 외 → `1` (후공)

> ⚠️ **`playerField`/`enemyField`는 "기기 기준 내 필드 / 상대 필드"입니다. 절대적인 P1/P2가 아닙니다.**
> 턴 주체 판정은 반드시 `OwnerIndex`로:
> `playerField.OwnerIndex == t_current ? playerField : enemyField`

### 6.5 commit-reveal 시드 합의

```
   클라이언트 A                              클라이언트 B
        │                                         │
        │  nonceA = 암호학적 8B 난수              │  nonceB = 암호학적 8B
        │  commitA = SHA256(nonceA)               │  commitB = SHA256(nonceB)
        │                                         │
  ┌─────┼─────────── 1단계: commit 교환 ──────────┼─────┐
  │     │                                         │     │
  │     ├──────── SeedCommit(commitA) ───────────▶│     │
  │     │◀─────── SeedCommit(commitB) ────────────┤     │
  │     │                                         │     │
  └─────┼─────────────────────────────────────────┼─────┘
        │                                         │
        │  ⚠️ 상대 commit 확보 후에야 nonce 공개   │
        │     → 상대 값을 보고 자기 값을 고를 수 없음
        │                                         │
  ┌─────┼─────────── 2단계: reveal 교환 ──────────┼─────┐
  │     │                                         │     │
  │     ├──────── SeedReveal(nonceA) ────────────▶│     │
  │     │◀─────── SeedReveal(nonceB) ─────────────┤     │
  │     │                                         │     │
  └─────┼─────────────────────────────────────────┼─────┘
        │                                         │
        │  VerifyCommit(nonceB, commitB)          │  VerifyCommit(nonceA, commitA)
        │                                         │
  ┌─────┼─────────── 3단계: 시드 도출 ────────────┼─────┐
  │     │                                         │     │
  │     │  seed = ReadU64(nonceA) XOR ReadU64(nonceB)   │
  │     │         ── 양쪽 동일 ──                 │     │
  │     ▼                                         ▼     │
  │  MatchRandom.Seed(seed)          MatchRandom.Seed(seed)
  │         splitmix64                     splitmix64   │
  └───────────────────────────────────────────────────────┘
```

### 6.6 RNG 사용 규칙 — 결정론의 핵심

```
             랜덤이 필요하다
                   │
                   ▼
      ┌────────────────────────────┐
      │ 이 결과가 양쪽에서         │
      │ 같아야 하나?               │
      └──────┬──────────────┬──────┘
         YES │              │ NO
      (게임 로직)        (연출)
             ▼              ▼
  ╔══════════════════╗  ┌──────────────────────┐
  ║ ✅ MatchRandom    ║  │ ✅ UnityEngine.Random │
  ║   splitmix64     ║  │   전역 시퀀스        │
  ║   공유 시드      ║  │   오염 방지          │
  ╚══════════════════╝  └──────────────────────┘
        │                     │
        │                     ├─ 오디오 클립 선택
        │                     ├─ 파티클 변주
        └─ 무쌍 스플래시      ├─ 내 덱 셔플
           대상 선정          │   (결과 ID를 broadcast → 무관)
           PeerlessAttack     └─ 싱글 AI 선택
           .PickSplash            (싱글 전용 → 무관)
```

> ⚠️ **로직에서 `UnityEngine.Random`을 쓰면 즉시 desync.**
> `MatchRandom`이 `System.Random` 대신 splitmix64를 쓰는 이유도 런타임 구현 의존을 없애기 위함입니다.

### 6.7 버퍼드 시그널 패턴 (3곳 반복)

RPC가 "기다리기 시작하기 전"에 먼저 도착하는 레이스를 흡수합니다.

```
   ┌─── 수신측 On*Received() ────┐   ┌─── 대기측 WaitFor*() ───────┐
   │                             │   │                             │
   │   대기 중인가?              │   │   버퍼에 있나?              │
   │   waiting && tcs != null    │   │                             │
   │        │                    │   │        │                    │
   │   YES  │  NO                │   │   YES  │  NO                │
   │   ┌────┴────┐               │   │   ┌────┴────┐               │
   │   ▼         ▼               │   │   ▼         ▼               │
   │ tcs.Try   buffer            │   │ 즉시     tcs 생성           │
   │ SetResult .Enqueue          │   │ 반환     waiting = true     │
   │ (즉시     또는              │   │          await tcs.Task     │
   │  깨움)    received=true     │   │                             │
   │             │               │   │             ▲               │
   └─────────────┼───────────────┘   └─────────────┼───────────────┘
                 │                                 │
                 └───── 나중에 소비 ───────────────┘
```

| 대상 | TCS | 버퍼 |
|---|---|---|
| 공격 RPC | `attackTcs` | `attackBuffer` (Queue) |
| 연출 완료 | `opponentReadyTcs` | `opponentReadyReceived` (bool) |
| 시드 commit | `seedCommitTcs` | `seedCommitReceived` (bool) |
| 시드 reveal | `seedRevealTcs` | `seedRevealReceived` (bool) |
| 상대 스폰 | — | `enemySpawnBuffer` (Queue → `FlushEnemySpawns`) |

### 6.8 멀티 1회 공격 전체 시퀀스

```
  A: MultiplayerPlayerTurn          A|B 네트워크        B: MultiplayerOpponentTurn
  ══════════════════════            ═══════════        ══════════════════════════

  CardView.OnAttack 수신
  ownerIndex · forcedAttacker 검증
  cunningSwap 판정
         │
         ├─ SendAttack(atk, def, swap) ──📡 Attack──▶ WaitForOpponentAttack() 완료
         │                                                    │
  ┌──────┴──────────────────┐               ┌─────────────────┴──────────────┐
  │ AttackSequence.Play     │  (병렬 진행)  │ AttackSequence.Play            │
  │  → AttackProcessor      │               │  → AttackProcessor             │
  │     .Execute()          │               │     .Execute()                 │
  │  ★ 동일 결과 보장 ★     │               │  ★ 동일 결과 보장 ★            │
  └──────┬──────────────────┘               └─────────────────┬──────────────┘
         │                                                    │
  RunAfterAttackPassives                       RunAfterAttackPassives
         │                                                    │
  ╭──────┴──── 각자 자기 필드만 채우고 브로드캐스트 ──────────┴──────╮
  │                                                                  │
  playerField.FillEmptySlots()                  playerField.FillEmptySlots()
  BroadcastMySpawns() ──📡 CardSpawn × N──▶ OnCardSpawnReceived()
                                            → enemyField.PlaceCardDirectly()
                                            → enemySpawnBuffer 적재
  OnCardSpawnReceived() ◀──📡 CardSpawn × M── BroadcastMySpawns()
  → enemySpawnBuffer 적재
  ╰──────────────────────────────────────────────────────────────────╯
         │                                                    │
  ╔══════╪════════ 🔒 프레임 랑데부 — WaitForOpponentReady() ═╪═══════╗
  ║      │                                                    │       ║
  ║  AnimReady 전송 ──────📡 AnimReady──────▶ 수신 (플래그/TCS)       ║
  ║  수신 (플래그/TCS) ◀───📡 AnimReady────── AnimReady 전송          ║
  ║      │                                                    │       ║
  ║  양쪽이 상대 AnimReady 수신 후 동시 재개                          ║
  ║  → CardSpawn RPC 전부 도착했음이 보장됨                           ║
  ╚══════╪════════════════════════════════════════════════════╪═══════╝
         │                                                    │
  FlushEnemySpawns()                            FlushEnemySpawns()
  뷰 갱신 + 채움 애니                           뷰 갱신 + 채움 애니
         │                                                    │
         ▼                                                    ▼
  canAttackAgain (처형)?                        canAttackAgain (처형)?
   YES → forcedAttacker 설정                     YES → 루프, 다음 Attack RPC 대기
         입력 재허용                             NO  → break
   NO  → turnDone = true
```

### 6.9 연결 종료 — 데드락 해제

```
   상대 이탈 (NetworkSession.OnPlayerLeft)
        │
        ▼
   TurnRunner.HandlePlayerLeft()
        │
        ├─▶ disconnectWin = true
        │
        ├─▶ 🔓 NetworkGameController.ForceOpponentReady()
        │      └─ 대기 중인 opponentReadyTcs 강제 완료
        │
        ├─▶ 🔓 MultiplayerTurnRunner.ForceOpponentAttackResolve()
        │      └─ 더미 (0, 0, false) 주입 → attackTcs 강제 완료
        │
        └─▶ 승리 팝업 → 턴 루프가 다음 체크에서 break
```

> ⚠️ **모든 대기 TCS는 강제 해제 경로가 있어야 합니다.** 없으면 영구 데드락.

---

## 7. 뷰 · 입력 레이어

### 7.1 입력 → 규칙 경로

```
   👆 사용자 드래그
        │
        ▼
   ┌────────────────────── CardView ──────────────────────┐
   │  OnMouseDown / OnMouseDrag / OnMouseUp               │
   │        │                                             │
   │        ▼                                             │
   │   TurnState.InputAllowed?  ──NO──▶ 무시              │
   │        │ YES                                         │
   │        ▼                                             │
   │   ForcedAttacker == null                             │
   │   || this == ForcedAttacker?  ──NO──▶ 무시           │
   │        │ YES                                         │
   │        ▼                                             │
   │   드래그 처리 (임계값 · 데드존 · 방향)                │
   └────────┬─────────────────────────────────────────────┘
            │
            ▼
   📢 static event CardView.OnAttack(attackerView, targetView)
            │
            ▼
   ┌── PlayerTurn / MultiplayerPlayerTurn ────────────────┐
   │  .HandleCardViewAttack()                             │
   │        │                                             │
   │        ├─ 내 카드인가? (ownerIndex == LocalOwnerIndex)│
   │        ├─ 상대 카드인가?                             │
   │        └─ forcedAttacker 일치하는가?                 │
   │              │ 모두 통과                             │
   │              ▼                                       │
   │        ExecuteAttackAsync()                          │
   └──────────────────────────────────────────────────────┘
```

### 7.2 TurnState — 입력 게이팅 단일 권위체

```
  ┌── ✍️ 쓰기 — 턴 클래스만 ──┐
  │  PlayerTurn               │
  │  MultiplayerPlayerTurn    │
  └────────────┬──────────────┘
               ▼
  ╔══════════ TurnState (static) ═══════════╗
  ║                                         ║
  ║  InputAllowed      조작 가능 여부       ║
  ║  ForcedAttacker    처형 연속공격 강제   ║
  ║  LocalOwnerIndex   내 진영 판정 기준    ║
  ║                                         ║
  ╚════════════════════╤════════════════════╝
                       ▼
  ┌── 👁️ 읽기 — 뷰만 ─────────────────────┐
  │  CardView 입력 판정                   │
  │  CardView.Update 힌트 화살표           │
  │  BattleFieldView 카드 등장 방향        │
  │  AttackSequence 연출 flip 판정         │
  └───────────────────────────────────────┘
```

### 7.3 CardView 책임 (리팩터링 1순위)

```
  ╔═══════════════ CardView — 838줄 ═══════════════╗
  ║                                                ║
  ║  🖱️ 입력    드래그 2모드 · 롱프레스            ║
  ║             스와이프 · 데드존 · 방향 임계값     ║
  ║                                                ║
  ║  🎨 렌더링  hp/bonusHp/이름/일러스트            ║
  ║             뒷면 오버레이 · 빈 슬롯 오버레이    ║
  ║                                                ║
  ║  ✨ 연출    키워드 글로우 · 패시브 글로우       ║
  ║             하이라이트 · 공격 프리뷰            ║
  ║                                                ║
  ║  ⚔️ 무기    weaponPrefab 인스턴싱               ║
  ║             애니메이터 제어                     ║
  ║                                                ║
  ║  🏷️ 아이콘  KeywordIconConfig 기반 동적 배치    ║
  ║                                                ║
  ║  📋 정적    allViews 레지스트리                 ║
  ║             FadeAll / FadeTeam / FadeCards      ║
  ║             RestoreAllFades                     ║
  ║                                                ║
  ╚══════════════════════╤═════════════════════════╝
                         │ 위임
                         ▼
            ┌────────────────────────────┐
            │ CardAnimator (242줄)       │
            │ 실제 트윈 애니메이션        │
            └────────────────────────────┘
```

### 7.4 UI 풀링

```
  Addressables            DataLibrary.Awake()          UIPoolManager
  라벨 "UIPrefab"   ──▶   전부 미리 로드         ──▶   .AddOrUpdateUI<T>(data)
                          Dictionary<Type,               │
                                     GameObject>         ▼
                          (PooledUIBase 타입 키)    인스턴스 생성/재사용/갱신
```

---

## 8. 횡단 관심사

### 8.1 이벤트 허브

```
   TurnRunner (발행자)
        │
        │ RaiseTurnStarted(field)
        │ RaiseTurnCountChanged(count)
        ▼
  ╔══════ TurnEvents (static) ══════╗
  ║   event TurnStarted             ║
  ║   event TurnCountChanged        ║
  ╚═══════════╤═════════════════════╝
              ▼
      ┌───────┴────────┐
      ▼                ▼
  HealerEffect      턴 UI

  → 구독자는 발행자(TurnRunner) 구체 타입이 아니라 이 허브에만 의존
```

### 8.2 static 싱글턴 지도

```
  ┌── DontDestroyOnLoad ──────────────────────────────────┐
  │  NetworkSession.Instance   DataLibrary.instance       │
  └───────────────────────────────────────────────────────┘

  ┌── 씬 스코프 ──────────────────────────────────────────┐
  │  NetworkGameController.Instance                       │
  │  MultiplayerTurnRunner.Instance                       │
  │  BattleCamera.Instance      SoundManager.Instance     │
  │  UIPoolManager.instance     SceneTransitionVideo.Instance │
  └───────────────────────────────────────────────────────┘

  ┌── 순수 static 상태 ───────────────────────────────────┐
  │  TurnState        TurnEvents       MatchRandom        │
  │  TurnRunner.TurnCount              DeckConfig         │
  │  CardView.allViews                                    │
  └──────────────────────┬────────────────────────────────┘
                         │ 씬 이탈 시 일괄 리셋
                         ▼
  ┌── BattleCleanup.Run() ────────────────────────────────┐
  │  DOTween.KillAll()                                    │
  │  ParticlePooler.Flush()  ObjectPooler.Flush()         │
  │  CardView.Cleanup()   → TurnState.Reset()             │
  │  TurnRunner.Cleanup() → TurnEvents.Reset()            │
  │                       → MatchRandom.Reset()           │
  └───────────────────────────────────────────────────────┘
```

### 8.3 코딩 컨벤션

| 대상 | 규칙 | 예 |
|---|---|---|
| 메서드 파라미터 | `_camelCase` | `_attacker`, `_defender` |
| 지역 변수 | `t_camelCase` | `t_result`, `t_atkDmg` |
| 인스턴스 필드 | 항상 `this.` 명시 | `this.slots[i]` |
| static 필드 | `s_` 접두사 | `s_state`, `s_anyDragging` |
| 주석 | 한국어 · 공개 API는 `<summary>` | — |
| 비동기 | `UniTask` 반환 · fire-and-forget은 `.Forget()` | — |

---

## 9. 핵심 불변식 — 깨면 안 되는 규칙

```
 ┌─ 🎲 결정론 ───────────────────────────────────────────────────┐
 │                                                               │
 │  1. RNG 분리                                                  │
 │     로직 = MatchRandom · 연출 = UnityEngine.Random            │
 │     ⚠️ 위반 시 즉시 desync                                     │
 │                                                               │
 │  3. CardRegistry.allCards 순서 고정                           │
 │     중간 삽입/삭제 금지 · 추가는 항상 배열 끝에만             │
 │     ⚠️ 양쪽 인덱스→카드 매핑이 같아야 함                       │
 │                                                               │
 │  4. 와이어 프로토콜 호환성                                    │
 │     MsgType 값 · 페이로드 레이아웃이 양쪽 동일                │
 │     ⚠️ 버전 협상 없음 → 변경 시 전체 배포 필요                 │
 │                                                               │
 └───────────────────────────────────────────────────────────────┘

 ┌─ 📐 단일 진실원 ──────────────────────────────────────────────┐
 │                                                               │
 │  2. 전투 규칙은 CardInstance에만                              │
 │     데미지 / 치사 / 반격 판정                                 │
 │     ⚠️ behavior · 프리뷰 UI에 복제 금지                        │
 │                                                               │
 │  6. 승패 판정은 CheckGameOver() 한 곳                         │
 │     턴 클래스는 전멸을 감지해도 break만                       │
 │                                                               │
 └───────────────────────────────────────────────────────────────┘

 ┌─ 🌐 멀티 규약 ────────────────────────────────────────────────┐
 │                                                               │
 │  5. 진영은 ownerIndex로 판정                                  │
 │     playerField / enemyField는 기기 상대적                    │
 │                                                               │
 │  8. 멀티에서 ctx.FillAndAnimate() 금지                        │
 │     자기 필드만 채우고 브로드캐스트                           │
 │     (상대 필드는 RPC 권위)                                    │
 │                                                               │
 │  9. 연출 중 전체 Refresh() 금지                               │
 │     RPC로 미리 배치된 신규 카드가 조기 노출됨                 │
 │     → 죽은 슬롯은 HideSlot()으로 개별 처리                    │
 │                                                               │
 └───────────────────────────────────────────────────────────────┘

 ┌─ 🔓 데드락 방지 ──────────────────────────────────────────────┐
 │                                                               │
 │  7. 모든 대기 TCS에 강제 해제 경로                            │
 │     Force* 메서드 없으면 영구 데드락                          │
 │                                                               │
 └───────────────────────────────────────────────────────────────┘
```

---

## 10. 알려진 리스크

```
   발생가능성
      높음 │  asmdef 없음 ⑥        CardView 838줄 ⑤
           │
           │        behavior 우선순위 ⑦    desync 감지 없음 ②
           │                                WaitForOpponentReady ④
      중간 │  수동 직렬화 ⑧
           │                      PlaceCardDirectly ③
           │
      낮음 │                                     클라이언트 권위 ①
           └──────────────────────────────────────────────────────
              낮음              중간                 높음
                              영향도
```

| # | 리스크 | 내용 | 개선 방향 |
|---|---|---|---|
| ① | **클라이언트 권위** | Shared 모드 + 공격 RPC 무검증. 수신측은 "살아있는 카드인가" 정도만 확인. 시드는 commit-reveal로 막았지만 **행동 자체는 무검증** | 랭크전 도입 시 서버 권위(Host/Server 모드)로 전환 |
| ② | **desync 감지 수단 없음** | 락스텝인데 상태 해시 교환이 없음. 한번 어긋나면 양쪽이 조용히 다른 게임 진행 | `AnimReady`에 `hash(슬롯별 cardId+hp+bonusHp+runtimeKeywords)` 실어 비교 |
| ③ | **`PlaceCardDirectly` 맹목 dequeue** | 상대 스폰 수신 시 어떤 카드인지 무관하게 대기큐를 하나 꺼냄. 순서가 어긋나면 조용히 틀어짐 | cardId 대조 또는 큐 인덱스 명시 |
| ④ | **`WaitForOpponentReady` 무타임아웃** | 상대가 예외로 죽으면 `PlayerLeft` 전까지 영구 대기. 앱 백그라운드 전환에도 취약 | 타임아웃 + 재동기화 경로 |
| ⑤ | **`CardView` 838줄** | 입력·렌더링·무기·아이콘·정적 레지스트리가 한 클래스 | 입력 / 렌더 / 이펙트 분리 |
| ⑥ | **asmdef 없음** | 단일 어셈블리 + 전역 네임스페이스 | 레이어별 asmdef → 의존 방향을 컴파일러가 강제 |
| ⑦ | **behavior 우선순위 하드코딩** | 복합 키워드 카드는 하나만 탐 → 조합 폭발 | 데코레이터 / 파이프라인 구성 |
| ⑧ | **수동 직렬화 오프셋** | `t_offset + 9` 같은 상수가 코드에 흩어짐 | `MsgWriter` / `MsgReader` 추상화 |

---

## 11. 작업 시작점 치트시트

```
 무엇을 하려는가?          먼저 볼 파일 (순서대로)
 ───────────────────────   ──────────────────────────────────────────────
 새 키워드 추가        →   CardKeyword.cs
                           → CardInstance 규칙 메서드
                           → AttackBehaviorFactory 우선순위 ⚠️
                           → KeywordIconConfig

 데미지/반격 규칙 변경 →   ★ CardInstance.cs (단일 진실원) ★
                           → 영향받는 behavior 확인

 공격 연출 수정        →   AttackSequence.cs
                           → Card/AttackEffect.cs

 턴 흐름 변경          →   TurnRunner.cs
                           → 해당 TurnBase 서브클래스
                           → ⚠️ 멀티 대응 쌍도 함께 수정

 네트워크 메시지 추가  →   NetworkGameController.cs (MsgType + Send/Handle)
                           → MultiplayerTurnRunner 수신 콜백

 desync 디버깅         →   MatchRandom 사용처 grep
                           → 로직에서 UnityEngine.Random 쓴 곳 확인
                           → CardRegistry.allCards 순서 확인

 매칭/로비 수정        →   MultiplayerLobbyPanel.cs / RandomMatchPanel.cs
                           → NetworkSession.cs

 입력 방식 변경        →   CardView.cs (Input 리전)
                           → UI/Input/

 덱 편집/저장          →   DeckBuilderUI.cs
                           → DeckSaveManager.cs
                           → DeckConfig.cs
```

### 전문 서브에이전트 (`.claude/agents/`)

```
 ┌── ✏️ 수정 가능 ────────────────────────────────────────────┐
 │                                                            │
 │  battle-engineer                                           │
 │    전투 · 턴 · 공격 로직 · 패시브 · 슬롯 배치              │
 │                                                            │
 │  net-engineer                                              │
 │    Fusion 멀티플레이 · 결정론 · 와이어 프로토콜            │
 │    divergence 버그 진단                                    │
 │                                                            │
 └────────────────────────────────────────────────────────────┘
 ┌── 👁️ 읽기 전용 ────────────────────────────────────────────┐
 │                                                            │
 │  architecture-engineer                                     │
 │    경계 · 결합도 · 단일진실원 · 협업 가능성 검증           │
 │                                                            │
 │  tcg-reviewer                                              │
 │    RNG 위반 · 규칙 이중화 · 데드코드 · 와이어 계약 파손    │
 │                                                            │
 └────────────────────────────────────────────────────────────┘
```

---

*문서 생성일: 2026-07-22 · 기준 커밋: `673546c`*
