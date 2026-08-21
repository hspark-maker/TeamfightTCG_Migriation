# 카드 강화 등급제(0~3성) 밸런스 테이블

커밋 `1d3cd65fb 카드 강화 레벨제(Lv1~10)를 등급제(0~3성)로 전환` 이후 기준.
이 문서는 **제안 수치**다. 확정 값의 진실원은 `Assets/SO/CardGrowth/CardGrowthConfig.asset`과 카드 스펙시트다.

## 좌표계 — 성과 레벨이 1 어긋난다

내부는 여전히 레벨이다. 표시만 성으로 바꾼다.

| 표시 | 내부 레벨 | 비고 |
|---|---:|---|
| 0성 | 1 | `CardGrowth.BaseLevel`, 미강화 |
| 1성 | 2 | 첫 강화 |
| 2성 | 3 | `firstEvolutionLevel` — 진화 1 + 시너지 해금 |
| 3성 | 4 | `secondEvolutionLevel` = `maxLevel` — 진화 2 + 키워드 강화 |

변환은 `GrowthStar.FromLevel`(= `Level - 1`) 한 곳에서만 한다.
`CardGrowthConfig`, 스펙시트 `hp2`~`hp4`, `RankConfig.aiCardLevels`는 **전부 레벨 좌표**다 — 기획 문서의 성 표기와 1 어긋나므로 값을 옮길 때 주의한다.

## ① 공통 강화 단계표

`CardGrowthConfig.levelSteps` 3행에 대응한다.

| 강화 | 내부 Lv | 표시 | 재화 | 비용 | 성공률 | 평균 HP | 해금 |
|---|---:|---|---|---:|---:|---:|---|
| 1회 | 2 | 0→1성 | 조각(Shard) | 25 | 100% | +4.50 | 카드 키워드 |
| 2회 | 3 | 1→2성 | 조각(Shard) | 75 | 100% | +4.07 | 진화 1 + 시너지 |
| 3회 | 4 | 2→3성 | 조각(Shard) | 150 | 100% | +4.17 | 진화 2 + 키워드 강화 |

현재 에셋 대비 변경점:

- **마지막 단계 재화를 다이아 50 → 조각 150으로 변경.** 지금은 3회차만 유료 축(Diamond)이라 성격이 튄다. 조각은 카드팩 중복 환급으로 들어오므로 곡선이 이어진다.
- 강화가 3회뿐이라 실패는 두지 않는다 — 성공률 100% 유지.
- `levelSteps.hpGain`은 `4/4/4`로 채운다. 신규 카드가 개별 커브 없이 들어왔을 때의 안전값이며, 기존 30장은 카드별 `hp2`~`hp4`가 우선하므로 수치가 바뀌지 않는다.

`ECurrencyType`: `Gold=0, Diamond=1, Energy=2, Shard=3`.

## ② 카드별 HP 커브

스펙시트 `hp2`/`hp3`/`hp4` 열에 그대로 붙는 형태다. 인덱스가 곧 레벨이라 `hpGainByLevel = [0, 0, hp2, hp3, hp4]`가 된다.

`keywordUnlockLevel = 0`(1성에 해금 보상이 없는 카드) 중 `hp2 < 5`인 9장은 최종 HP 일부를 1성으로 앞당겼다. **카드별 누적 HP 총합은 전부 유지한다.**

```csv
name,displayName,grade,keywordUnlockLevel,hp2,hp3,hp4
Data_Card_BalloonPeng,풍선펭,Silver,2,6,4,3
Data_Card_Bombbat,폭탄밤,Silver,0,5,4,3
Data_Card_Campbean,모닥콩,Prism,0,5,4,3
Data_Card_CaptainBeak,대장부리,Gold,2,4,3,7
Data_Card_Cloudmong,솜구름몽,Silver,0,5,4,3
Data_Card_Crystalhorn,수정뿔루,Silver,0,5,5,3
Data_Card_DizzyOctopus,해롱문어,Gold,2,3,4,5
Data_Card_Dreameater,꿈먹이,Gold,2,4,3,7
Data_Card_Flarelux,화르룩스,Prism,2,3,4,5
Data_Card_Gearmole,톱니두더,Gold,2,4,4,5
Data_Card_Honeybee,꿀꿀비,Silver,0,5,4,4
Data_Card_Icekomi,얼음꼬미,Silver,2,5,4,4
Data_Card_IronMongchi,철갑몽치,Gold,0,5,4,6
Data_Card_KingChestnutHedgehog,왕밤도치,Prism,2,3,4,6
Data_Card_MagnetCrab,자석게,Prism,2,3,5,5
Data_Card_Mapletail,단풍꼬리,Gold,0,5,4,3
Data_Card_MushroomCat,버섯냥,Silver,2,4,4,4
Data_Card_Nightchestnut,깜밤이,Gold,2,3,4,6
Data_Card_Poslamb,포슬램,Silver,2,4,5,4
Data_Card_Rockbean,바위콩,Gold,0,5,4,3
Data_Card_Sandmong,모래몽,Silver,2,6,4,3
Data_Card_SnowballBear,눈덩곰,Prism,2,3,5,5
Data_Card_Sparkfin,찌릿핀,Gold,2,4,4,4
Data_Card_Startori,별토리,Silver,0,5,4,4
Data_Card_Swampfrog,늪꾸리,Silver,2,4,4,4
Data_Card_Thunderhorn,번개뿔,Prism,0,5,4,3
Data_Card_Waggledodo,와글도도,Silver,2,6,5,3
Data_Card_WaterdropLong,물방울룽,Silver,2,6,4,3
Data_Card_Waveri,파도리,Silver,0,5,4,3
Data_Card_Woodhorn,우드혼,Gold,0,5,3,4
```

변경 대상 9장: 모닥콩, 솜구름몽, 수정뿔루, 단풍꼬리, 바위콩, 별토리, 번개뿔, 파도리, 우드혼.

## ③ AI 성급 곡선

`RankConfig.aiCardLevels`(20칸, 레벨 좌표)를 성 표기로 옮긴 것이다.

| 랭크 구간 | AI 성급 | 플레이어 권장 상태 |
|---|---:|---|
| 브론즈 1~3 | 0성 | 기본 카드 학습 |
| 브론즈 4 | 1성 | 주력 카드 1성 시작 |
| 실버 1~3 | 1성 | 주력 덱 1성 |
| 실버 4 | 2성 | 첫 시너지 덱 완성 |
| 골드 1~4 | 2성 | 주력 덱 2성 완성 |
| 플래티넘 1 | 2성 | 3성 준비 |
| 플래티넘 2~4 | 3성 | 주력 카드 3성 |
| 다이아 1~4 | 3성 | 완성 덱 경쟁 |

운영 목표: 첫 1성 3판 이내, 첫 2성 15판 이내, 첫 3성 35판 이내.
AI 성급이 오르는 지점에서 승률이 8%p 이상 떨어지면 비용이나 HP를 조정한다.

## ④ 적용 전 같이 잡을 것

- **구 세이브 정규화** — Lv5~10으로 저장된 값을 로드 시 Lv4로 영구 정규화. 등급제 전환 전 세이브가 그대로 들어오면 상한을 넘는 레벨이 남는다.
- **검증기 범위 검사 추가** — `levelSteps`는 Lv2~4만, 카드 커브는 `hp2`~`hp4`만, `aiCardLevels`는 1~4만 허용하는지 검사.

## ⑤ 알려진 구조 문제

표를 적용해도 남는 것들이다.

1. **HP 전역 레버가 죽어 있다.** `CardGrowthConfig.StepAt`은 `levelSteps` 행 → 카드 커브 순으로 HP를 덮는데, 카드 커브가 있으면 항상 그쪽이 이긴다. `hpPerLevel`과 `GrowthLevelStep.hpGain`은 커브가 없는 카드에만 걸린다. 전체 강화 파워를 한 손잡이로 조절할 수단이 없다.
2. **스펙시트에 죽은 열이 남아 있다.** `CardSpecImporter.Columns`에 `hp5`~`hp10`이 그대로 있어 시트·표 대조·임포트 경로를 계속 통과하지만 읽히지 않는다.
3. **해금 이벤트가 1성에 몰려 있다.** `keywordUnlockLevel`이 0(12장) 또는 2(18장) 두 값뿐이다. 0인 12장은 1성 구간에 HP 외의 보상이 없다 — 위 HP 앞당김은 그 공백을 메우는 임시 처방이고, 근본적으로는 해금 축을 3단계에 분산할지 결정이 필요하다.
