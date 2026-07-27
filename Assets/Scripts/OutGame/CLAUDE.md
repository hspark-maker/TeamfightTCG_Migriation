# OutGame (아웃게임/메타 레이어) 작업 규약

> 이 파일은 `Assets/Scripts/OutGame/` 작업 시 자동 로드되는 치트시트다.
> 상세 규약을 여기서 재정의하지 않는다 — **핵심만 요약 + 진실원 링크**. 이중 진실원 금지.

## 이 파트는

수집형 1vs1 카드배틀의 **아웃게임(메타) 레이어**: 세이브 · 재화 · 도감/소유 · 방치 생산 · 카드팩 · 보상.

- 성장 루프·Phase 로드맵 → `@docs/OutGamePlan/OUTGAME_ROADMAP.md`
- 승인·구조 기준 문서(진실원) → `@docs/OutGamePlan/STRUCTURE.md` (구조 변경 시 여기부터 갱신)

## 네이밍 (기술보다 개념)

- 클래스/메서드는 **게임 도메인 동사·명사**로. 기술 용어는 인프라 레이어에만.
  - 예: `CardPackOpener.TryPurchase`, `OwnershipManager.Grant`/`Revoke`, `CollectionProductionManager.Harvest`
  - 인프라 예외: `JsonFileRepository`, `IRepository`
- enum 접두사 `E` (`ECurrencyType`). 실패 가능 조회는 `Try*` + `out`.
- 필드 접두사: `s_`(static) · `m_`(인스턴스) · `t_`(지역) · `_`(파라미터).
  get-only 프로퍼티는 PascalCase, `[SerializeField]`는 camelCase.

## 코드 스타일

- **주석은 간략하게** — "왜"만 한 줄로. 코드가 곧 설명이면 주석을 달지 않는다. 불변식·비직관적 방어 로직·확장 축처럼 코드로 안 드러나는 의도만 남긴다. 장문 서술 금지.
- **클래스 내 멤버 순서: 위에서부터 필드/프로퍼티 → public 메서드 → private 메서드.** (읽는 사람이 공개 API를 먼저 보고, 세부 구현은 아래에서 찾게.)


구조 변경 시 `STRUCTURE.md` 갱신 — **근거 없는 노드 금지(실제 파일 또는 승인된 설계만)**.
