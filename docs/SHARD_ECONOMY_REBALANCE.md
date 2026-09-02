# Shard 경제 · AI 난이도 곡선 재조정 (2026-08-24)

## 배경 — 실측

| 항목 | 수치 |
|---|---|
| 카드 최대 레벨 | **4** (`CardGrowthConfig.asset` `maxLevel: 4`, 0~3성) |
| 강화 비용 | Lv2 = 25 / Lv3 = 75 / Lv4 = 150 → 카드 1장 만렙 **250** |
| 덱 6장 만렙 필요 Shard | **1,500** |
| 변경 전 계정 평생 획득 총량 | **1,200** (모험 750 + 도감 450) → 충족률 0.8배 |

- Shard **반복 수급처 0건**. 전투 보상(`RewardConfig`)·랭크 보상(`RankConfig`)은 골드/다이아만 준다.
  팩 중복 환급은 `refundType: 3`으로 저작돼 있으나 `CardPackOpener`가 재화 대신 간식만 지급한다(死데이터).
- 골드는 전투에서 계속 나온다(`goldPerCard: 10`, `winFloor: 10`) → **골드를 덜어 Shard로 옮기는 재배분**이 성립한다.
- AI 레벨: `BootInstaller.EnemyCardLevelOf`가 만렙(4)으로 클램프 → 저작된 5~10이 전부 4로 잘렸다.
  변경 전 실효 곡선은 `1,1,2,3,4,4 / 4,4,4,4,4,4 / 4×6 / 4×6` — 1장 중반에서 곡선이 끝났다.
- 레벨은 HP만이 아니라 정체성 게이트다: **Lv3에서 시너지, Lv4에서 키워드가 열린다**(`firstEvolutionLevel: 3`, `secondEvolutionLevel: 4`).
  공격력 = 현재 HP(`CardInstance.AttackDamage`)라 성장축은 HP 하나뿐이다.

## 1. AI 카드 레벨 재저작 — 완료 (`Assets/SO/Tournament/TournamentConfig.asset`)

`aiCardLevel`은 스펙시트에 없어 `.asset`이 진실원이다. 수정만으로 런타임에 반영된다.

| 챕터 | 진입 등급 | 새 곡선 | 의도 |
|---|---|---|---|
| 1장 · 안개 숲 | Bronze | `1 1 2 2 2 2` | 시너지 없는 맨몸 구간 |
| 2장 · 무너진 성터 | Silver | `2 2 3 3 3 3` | 3장부터 **시너지 해금** |
| 3장 · 얼어붙은 설원 | Gold | `3 3 3 4 4 4` | 후반부터 **키워드 해금** |
| 4장 · 잿불의 화산 | Platinum | `4 4 4 4 4 4` | 만렙 고정 — 차등은 덱 구성이 진다 |

4장이 평평한 것은 레벨 상한이 4이기 때문이다. 더 잘게 쪼개려면 AI 한계돌파 하위축(+1~3 HP) 추가나
`maxLevel` 상향이 필요하고, 이번 범위에서는 채택하지 않았다.

## 2. 보상 재배분

**주의 — 보상의 진실원은 `.asset`이 아니라 구글 스펙시트다.**
`TournamentSpec` / `AlbumSpec`이 `SpecData.bytes`에서 값을 읽고, 시트에 키가 있으면 SO 값을 덮는다.
Unity에서 확인한 결과 24정점·4챕터 완주·도감 테마/전체 완성이 **전부 시트에서 나온다**.
→ 아래 표를 시트에 반영한 뒤 `CookApps > SpecData` 창에서 **"시트 적용 & CS 생성"**을 돌려야 실제로 바뀐다.
`.asset`은 같은 값으로 미러링해 두었다(폴백·에디터 표시용).

### 2-1. TournamentReward — `amount`만 변경 (ownerKey·currency·order 그대로)

| ownerKey | currency | 변경 전 | 변경 후 |
|---|---|---:|---:|
| node_01 | Gold | 200 | **180** |
| node_02 | Shard | 50 | **60** |
| node_03 | Energy | 40 | 40 |
| node_04 | Gold | 250 | **220** |
| node_05 | Shard | 60 | **80** |
| node_06 | Diamond | 10 | 10 |
| chapter_01 | Gold | 300 | **250** |
| chapter_01 | Shard | 40 | **60** |
| node_07 | Gold | 250 | **220** |
| node_08 | Shard | 60 | **140** |
| node_09 | Energy | 50 | 50 |
| node_10 | Gold | 300 | **260** |
| node_11 | Shard | 70 | **160** |
| node_12 | Diamond | 15 | 15 |
| chapter_02 | Gold | 450 | **350** |
| chapter_02 | Shard | 50 | **150** |
| node_13 | Gold | 300 | **220** |
| node_14 | Shard | 70 | **160** |
| node_15 | Energy | 70 | 70 |
| node_16 | Gold | 400 | **300** |
| node_17 | Shard | 80 | **180** |
| node_18 | Diamond | 25 | 25 |
| chapter_03 | Gold | 550 | **380** |
| chapter_03 | Shard | 50 | **160** |
| node_19 | Gold | 400 | **280** |
| node_20 | Shard | 80 | **180** |
| node_21 | Energy | 90 | 90 |
| node_22 | Gold | 500 | **350** |
| node_23 | Shard | 90 | **200** |
| node_24 | Diamond | 30 | 30 |
| chapter_04 | Gold | 600 | **420** |
| chapter_04 | Shard | 50 | **170** |

### 2-2. AlbumReward — `amount`만 변경

| themeId | pageId | currency | 변경 전 | 변경 후 |
|---|---|---|---:|---:|
| Theme_Nature | (비움) | Diamond | 20 | 20 |
| Theme_Nature | (비움) | Shard | 150 | **250** |
| (비움) | (비움) | Gold | 1500 | **1000** |
| (비움) | (비움) | Diamond | 30 | 30 |
| (비움) | (비움) | Shard | 300 | **400** |

도감 페이지 보상(P1~P4 Energy 50)은 시트에 행이 없어 `.asset` 값이 그대로 산다. 손대지 않았다.

## 3. 결과

| | 변경 전 | 변경 후 |
|---|---:|---:|
| 모험 Shard | 750 | **1,700** |
| 도감 Shard | 450 | **650** |
| **총 Shard** | **1,200** | **2,350** (×1.96) |
| 모험 Gold | 4,500 | 3,430 |
| 도감 Gold | 1,500 | 1,000 |
| **총 Gold** | **6,000** | **4,430** (−26%) |

Energy·Diamond는 손대지 않았다(Energy는 키워드 강화 재화 `KeywordGrowthConfig`).

### 챕터 클리어 시점의 누적 Shard vs 강화 목표

| 시점 | 누적 Shard | 덱 6장 목표 | 필요량 |
|---|---:|---|---:|
| 1장 완주 | 200 | 전원 Lv2 | 150 |
| 2장 완주 | 650 | 전원 Lv3 | 600 |
| 3장 완주 | 1,150 | 절반 Lv4 | 1,050 |
| 4장 완주 | 1,700 | 전원 Lv4 | 1,500 |
| + 도감 전체 | 2,350 | 여분 3~4장 Lv4 | — |

각 챕터의 AI 레벨이 그 시점에 유저가 도달 가능한 레벨과 맞물린다.

## 4. 남은 위험 — 반복 수급처는 여전히 0건

2,350은 **계정 평생 총량**이다. 다 쓰면 강화 시스템은 영구 정지한다.
덱을 갈아타거나 카드 40장(만렙 10,000 필요)을 키우려는 유저는 막힌다.
수도꼭지를 하나 여는 선택지(미채택):
- `RewardConfig`에 전투 승리 Shard 추가 — 유일한 진짜 반복 수급
- `RankConfig` 티어 보상에 Shard 편성 — 랭크가 오를수록 AI가 세지는 축과 맞물림
- 팩 중복 환급 되살리기 — "중복 보상 = 간식" 설계 결정을 뒤집는 것이라 별도 판단 필요

## 5. 검증

- Unity 재임포트 후 확인: 노드 24개 / 챕터 4개, `AiCardLevelOrBase` = `1 1 2 2 2 2 / 2 2 3 3 3 3 / 3 3 3 4 4 4 / 4 4 4 4 4 4`, `CardGrowthManager.MaxLevel = 4` → 클램프로 잘리는 값 없음.
- 보상 수치는 시트 반영 + `CookApps > SpecData` 재생성 후 `TournamentSpec.TryGetRewards` 덤프로 재확인할 것.
