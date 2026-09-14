---
name: claude-to-codex-migration
description: Claude Code 하네스(.claude/ 폴더, CLAUDE.md, memory/)를 OpenAI Codex CLI 등가 파일로 변환하는 스킬. 사용자가 "Codex로 옮겨줘", "하네스 마이그레이션", "CLAUDE.md를 Codex용으로 변환", "Claude Code 설정을 Codex에서 쓰게 해줘" 처럼 Claude Code → Codex 전환을 요청할 때 반드시 이 스킬을 사용한다. 변환 완료 후 migration-report.md에 성공·부분완료·미완료 항목을 정리한다.
---

# Claude → Codex 마이그레이션

## 0. 시작 전 확인

### 소스 경로
- 사용자가 소스 프로젝트 경로를 지정했으면 그 경로를 사용
- 지정하지 않았으면 현재 작업 디렉토리를 소스로 사용하되 한 번 확인한다

### 대상 경로
- 사용자가 출력 경로를 지정했으면 그 경로를 사용
- 지정하지 않았으면 `<소스-상위>/<프로젝트명>-codex/`를 제안하고 확인받는다

### 소스 탐색
다음 경로를 Glob으로 스캔해 존재 여부를 파악한다.
```
CLAUDE.md
.claude/settings.json
.claude/settings.local.json
.claude/skills/**/SKILL.md
.claude/agents/*.md
.mcp.json
memory/MEMORY.md
memory/*.md
```

---

## 1. 매핑 절차

각 항목을 순서대로 처리한다. 대상 파일이 이미 존재하면 덮어쓰기 전에 사용자에게 확인한다.
소스 파일은 읽기만 하고 수정하지 않는다.

### 1-1. CLAUDE.md → AGENTS.md

두 파일 모두 Markdown이고 AI 세션 시작 시 읽히는 역할이 동일하다.
내용을 그대로 복사하고 파일명을 `AGENTS.md`로 바꿔 대상 루트에 저장한다.

**처리 방법**
1. `CLAUDE.md` 전체 내용을 Read
2. `<대상>/AGENTS.md`에 Write

---

### 1-2. memory/ → AGENTS.md 통합

Auto Memory의 자동 축적 메커니즘은 도구마다 달라 자동 마이그레이션이 불가능하다.
대신 MEMORY.md와 링크된 파일들을 읽고, **앞으로도 지속적으로 지켜야 할 규칙·컨벤션**만
골라서 AGENTS.md 하단 `## Imported from Memory` 섹션에 추가한다.

필터 기준:
- 아키텍처 결정, 빌드 명령어, 코드 컨벤션 → 포함
- 특정 작업의 진행 상태, 임시 메모 → 제외

처리 후 "Auto Memory 내용 일부를 AGENTS.md에 통합했습니다. 원본 memory/ 폴더는 유지됩니다."라고 안내한다.

---

### 1-3. Skills → .agents/skills/

Claude Code와 Codex 모두 `SKILL.md` + frontmatter(name, description) 구조를 사용한다.
경로만 `.claude/skills/` → `.agents/skills/`로 변경하면 대부분 그대로 동작한다.

**처리 방법**
1. `.claude/skills/` 하위 SKILL.md 목록을 Glob으로 수집
2. 각 SKILL.md를 Read
3. frontmatter에 `disable-model-invocation: true`가 있으면 → 1-4(Commands 처리)로 분기
4. 없으면 동일 내용을 `<대상>/.agents/skills/<스킬명>/SKILL.md`에 Write
5. 스킬 폴더 하위의 assets/, references/, scripts/ 폴더도 동일 경로로 복사한다

---

### 1-4. Commands → Skills + agents/openai.yaml

Claude Code의 Commands는 SKILL.md에 `disable-model-invocation: true`를 달아
사용자가 `/skill-name`으로 명시적 호출할 때만 실행되는 스킬이다.
Codex에서는 같은 역할을 Skills의 `agents/openai.yaml`로 구현한다.

**처리 방법**
1. frontmatter에서 `disable-model-invocation: true` 제거
2. SKILL.md를 `<대상>/.agents/skills/<스킬명>/SKILL.md`에 Write
3. 동일 폴더에 `agents/openai.yaml`을 Write:

```yaml
policy:
  allow_implicit_invocation: false
```

사용자에게 "호출 문법이 `/skill-name`에서 `$skill-name`으로 바뀝니다"라고 안내한다.

---

### 1-5. Hooks → config.toml [hooks]

Claude Code는 `settings.json`의 `hooks` 섹션에서 라이프사이클 이벤트를 정의한다.
Codex는 `config.toml`의 `[hooks]` 섹션을 사용하며 지원 이벤트는 아래와 같다.

지원 이벤트: `PreToolUse`, `PermissionRequest`, `PostToolUse`, `PreCompact`,
`PostCompact`, `SessionStart`, `UserPromptSubmit`, `SubagentStart`, `SubagentStop`, `Stop`

**처리 방법**
1. `settings.json`에서 `hooks` 키 추출
2. JSON 구조를 TOML로 변환해 `<대상>/.codex/config.toml`의 `[hooks]` 섹션에 병합
3. Claude Code에만 있는 이벤트 타입이 있으면 보고서의 "부분완료" 섹션에 기록

TOML 변환 예시:
```toml
[hooks.PreToolUse]
[[hooks.PreToolUse]]
matcher = "Bash"

[[hooks.PreToolUse.hooks]]
type = "command"
command = "echo 'tool use started'"
```

---

### 1-6. MCP → config.toml [mcp_servers]

MCP는 오픈 표준이므로 서버 정의 자체는 변경 불필요하다. 설정 파일 위치와 포맷만 바뀐다.

**처리 방법**
1. 프로젝트 루트 `.mcp.json`에서 `mcpServers` 키 추출 (Claude Code의 MCP 서버는 settings.json이 아니라 `.mcp.json`에 정의됨)
2. 각 서버 항목을 TOML로 변환해 `<대상>/.codex/config.toml`에 추가

변환 예시 (서버 이름을 테이블 키로 사용 — `[mcp_servers.<이름>]`):
```toml
[mcp_servers.unity]
command = "uvx"
args = ["mcp-server-unity"]
# env = { KEY = "value" }   # stdio 서버는 env 테이블로 환경변수 전달
```

---

### 1-7. Subagents → .codex/agents/

Claude Code는 `.claude/agents/`에 Markdown 파일로 에이전트를 정의한다.
Codex는 `.codex/agents/`에 TOML 파일을 사용하며 파일명만 `.md` → `.toml`로 바뀐다.

권한 모델 매핑:
| Claude Code `allowed-tools` | Codex `sandbox_mode` |
|---|---|
| 읽기 전용 도구만 포함 | `read-only` |
| 파일 생성·수정 도구 포함 | `workspace-write` |
| 명확하지 않은 경우 | `workspace-write`를 기본으로 사용하되 주석으로 표시 |

**처리 방법**
1. `.claude/agents/*.md` 목록 수집
2. 각 파일에서 frontmatter(name, description 등)와 본문 추출
3. `<대상>/.codex/agents/<에이전트명>.toml`로 Write:

```toml
name = "에이전트명"
description = "설명"
sandbox_mode = "workspace-write"
developer_instructions = """
에이전트 지시문 본문
"""
```

---

### 1-8. Config → config.toml

`settings.json`의 나머지 설정(agents 병렬 실행 등)을 config.toml로 변환한다.

**처리 방법**
1. `settings.json`에서 hooks 외 항목 추출 (mcpServers는 `.mcp.json`에서 별도 처리)
2. 다음 키를 매핑:

| settings.json | config.toml |
|---|---|
| `agents.maxThreads` | `[agents] max_threads` |
| `agents.maxDepth` | `[agents] max_depth` |

3. 매핑 표에 없는 키는 보고서의 "미완료" 섹션에 기록

---

## 2. 미완료 항목 처리

위 매핑 표에 없는 항목이 나오면:
1. `mcp__plugin_context7_context7__resolve-library-id`와 `query-docs`로 `/openai/codex` 문서를 검색
2. 공식 문서에서 대응 방식을 찾으면 변환
3. 찾지 못하면 보고서의 "미완료" 섹션에 기록하고 건너뜀

**보고 없이 건너뛰는 것은 금지한다.** 변환하지 못한 항목은 반드시 보고서에 남긴다.

수업에서 다룬 항목 중 자동 변환을 보류할 항목:
- `permissions.allow` (bash 명령 화이트리스트) — Codex 대응 방식 불명확
- `settings.local.json` 전체 — 로컬 오버라이드 구조 차이

---

## 3. migration-report.md 생성

모든 항목 처리 후 `<대상>/migration-report.md`를 작성한다.

```markdown
# Migration Report

생성일: <날짜>
소스: <소스 경로>
대상: <대상 경로>

## 완료

| 소스 파일 | 대상 파일 | 비고 |
|---|---|---|
| CLAUDE.md | AGENTS.md | |
| .claude/skills/... | .agents/skills/... | |
...

## 부분완료 (수동 작업 필요)

- **memory/ 통합**: AGENTS.md에 규칙성 있는 항목만 통합. 임시 메모는 제외됨.
  원본: `memory/` 폴더 유지 중
- **Commands 호출 문법 변경**: `/skill-name` → `$skill-name` (팀 공유 필요)
...

## 미완료 (Codex 공식 대응 없음)

- `permissions.allow`: Bash 명령 화이트리스트. Codex config.toml에서 동등 설정을 확인하세요.
...
```

---

## 4. 완료 안내

보고서 생성 후 다음을 출력한다.

```
✅ 마이그레이션 완료

생성된 파일:
  <대상>/AGENTS.md
  <대상>/.agents/skills/
  <대상>/.codex/config.toml
  <대상>/.codex/agents/
  <대상>/migration-report.md

수동 작업이 필요한 항목은 migration-report.md의 "부분완료" 섹션을 확인하세요.
Codex로 완전 전환 전까지는 CLAUDE.md와 AGENTS.md를 같은 저장소에 공존시키지 않는 것을 권장합니다.
```
