# TeamfightTCG_Migriation

## 언어

항상 한국어로 대답할 것.

## Agent 활용 정책 (기본 = 적극 위임)

실질적 작업은 사용자가 매번 지시하지 않아도 **스스로 판단해 커스텀 subagent 팀에 자동 위임**하고, 각 피드백을 취합한 뒤 진행한다. 위임을 물어보지 말고 그냥 소집한다(사용자가 "직접 해" 하면 인라인 처리).

라우팅(작업 성격 → 소집):
- 새 시스템·기능 설계 / 이해가 먼저 필요한 일 → `cavecrew-investigator`(실태 매핑) + 도메인 팀(`battle-engineer`/`net-engineer`/`architecture-engineer`) **병렬** 소집 → 취합 제안
- 전투·턴·공격 로직 → `battle-engineer`
- 멀티플레이·결정론·와이어 프로토콜 → `net-engineer`
- 아웃게임(세이브·재화·시간/생산·도감 소유상태·성장/보상) → `outgame-engineer`
- 구조·경계·협업성·단일진실원 판정 → `architecture-engineer`
- 코드 리뷰·검수 → `tcg-reviewer`
- 위치 찾기 / 기계적 1~2파일 수정 → `cavecrew-investigator`/`cavecrew-builder`
- 구현 완료 후 → `tcg-reviewer` 검수 게이트 + 메인이 `Unity_ReadConsole`로 컴파일 검증

가드(과잉 위임 방지):
- 사소한 단일 편집·오타·한 값 조회는 인라인. 위임 안 함.
- 독립 작업은 한 메시지에 동시 소집(병렬 우선).
- 설계 확정 후 다건 구현은 `Workflow`로 파이프라인(정찰→설계→시공(worktree 격리)→검수→통합). 단 Workflow는 토큰 많이 쓰므로 큰 구현 단계에서만.

상세: 메모리 [[agent-team-workflow]], [[refactor-backlog]].

## 아웃게임 운영 정책 (게이트·브리핑) — outgame-engineer 라우팅 작업에 적용

**게이트: 사용자 승인은 도메인/Phase당 1회.**
- 도메인·Phase 착수 시에만 사용자 승인을 받는다. 승인 대상 = 설계 + 태스크 분해 + `docs/OutGamePlan/STRUCTURE.md`의 mermaid 구조도. 절차는 `outgame-design-session` 스킬을 따른다.
- 승인 이후 개별 태스크는 **사전 플랜 승인 없이 자율 진행**한다. 태스크마다 진행 여부를 묻지 마라.
- 단, 다음 상황은 즉시 중단하고 사용자에게 보고: 승인된 설계에서 벗어나는 구조 변경 / 세이브 스키마·재화 API 등 공유 계약 변경 / 경계 절대규칙과의 충돌.
- 기존 품질 게이트는 유지: 구현 후 `tcg-reviewer` 검수 + 메인이 `Unity_ReadConsole` 컴파일 검증.

**시각 브리핑: 태스크 완료마다 필수.**
작업결과 데이터와 별도로, 사용자(신입 개발자)가 구조·흐름·원리를 따라올 수 있게 시각 중심 브리핑을 남긴다:
1. **구조 위치** — 이번 태스크가 전체 구조 어디에 붙었는지. `docs/OutGamePlan/STRUCTURE.md`의 해당 다이어그램을 갱신(신규 노드 표식)하고, 그 발췌를 응답에도 포함한다.
2. **흐름 시퀀스** — 이 태스크가 관여하는 대표 시나리오 1개(부트 로드, 수확, 구매 등 유저/시스템 행동 기준)를 mermaid sequenceDiagram으로.
3. **원리 한 장** — 이 구조가 그렇게 생긴 이유(관용구·규약·트레이드오프)를 3줄 이내 + 사용자가 이후 수정할 가능성 높은 지점 1~2곳(파일:라인).
다이어그램은 STRUCTURE.md에 누적하고(서사적 기록), 응답에도 표시한다(즉서 확인). 형식 예시는 STRUCTURE.md의 "Save·재화 구조" 섹션.

**구조도 유지.**
- 도메인 설계 확정 시, 그리고 구조가 바뀔 때마다 `docs/OutGamePlan/STRUCTURE.md`의 mermaid 구조도를 갱신한다. 사용자의 설계 승인과 구조 파악은 이 문서를 기준으로 한다.