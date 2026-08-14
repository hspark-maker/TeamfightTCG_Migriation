# TeamfightTCG_Migriation

## 언어

항상 한국어로 대답할 것.

## Agent 활용 정책 (기본 = 적극 위임)

실질적 작업은 사용자가 매번 지시하지 않아도 **스스로 판단해 커스텀 subagent 팀에 자동 위임**하고, 각 피드백을 취합한 뒤 진행한다. 위임을 물어보지 말고 그냥 소집한다(사용자가 "직접 해" 하면 인라인 처리).

라우팅(작업 성격 → 소집):
- 새 시스템·기능 설계 / 이해가 먼저 필요한 일 → `cavecrew-investigator`(실태 매핑) + 도메인 팀(`battle-engineer`/`net-engineer`/`architecture-engineer`) **병렬** 소집 → 취합 제안
- 전투·턴·공격 로직 → `battle-engineer`
- 멀티플레이·결정론·와이어 프로토콜 → `net-engineer`
- 구조·경계·협업성·단일진실원 판정 → `architecture-engineer`
- 코드 리뷰·검수 → `tcg-reviewer`
- 위치 찾기 / 기계적 1~2파일 수정 → `cavecrew-investigator`/`cavecrew-builder`
- 구현 완료 후 → `tcg-reviewer` 검수 게이트 + 메인이 `Unity_ReadConsole`로 컴파일 검증

가드(과잉 위임 방지):
- 사소한 단일 편집·오타·한 값 조회는 인라인. 위임 안 함.
- 독립 작업은 한 메시지에 동시 소집(병렬 우선).
- 설계 확정 후 다건 구현은 `Workflow`로 파이프라인(정찰→설계→시공(worktree 격리)→검수→통합). 단 Workflow는 토큰 많이 쓰므로 큰 구현 단계에서만.

상세: 메모리 [[agent-team-workflow]], [[refactor-backlog]].

## 기능 지도 — 코드 위치를 찾기 전에 먼저 읽어라

자체 코드가 `Assets/Scripts/` 409파일 65,801줄이라 grep 부터 시작하면 헤맨다.
**위치를 찾는 일이면 `.claude/orch-feature-map.md` 를 먼저 Read 해라.** 묻지 말고 읽어라.
지도의 타입 이름은 후보다 — `rg` 로 존재를 확인하고 진행한다.
서브에이전트(특히 `cavecrew-investigator`, 도메인 엔지니어)를 소집할 때도 이 파일을 먼저 읽으라고 지시해라.

측정치(같은 질문 3개, 지도 있음/없음): 탐색 총 입력 토큰 -67%, 턴 -59%, 도구 호출 -67%.

지도를 고쳤으면 `node .claude/check-feature-map.js` 로 타입 실재를 검증한다.

파일 목록만 필요할 때는 `.claude/orch-pathmap.md` (orch 자동 생성, 약 15k 토큰 — 웬만하면 쓰지 말 것).