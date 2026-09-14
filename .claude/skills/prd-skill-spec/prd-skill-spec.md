# PRD 스킬 명세

## 스킬 개요

| 항목 | 내용 |
|------|------|
| 커맨드 | `/prd` |
| 트리거 | 사용자가 `/prd <기능명>` 또는 "PRD 작성해줘"처럼 요청할 때 |
| 목적 | Unity 게임 기능 하나에 대한 요구사항 문서(PRD)를 `docs/requirements/<기능명>.md`에 생성한다 |

---

## 동작 절차

1. **기능명 파악** — 명시됐으면 그대로 사용, 없으면 한 줄 질문으로 확인
2. **PRD 초안 작성** — 아래 표준 섹션을 순서대로 채운다

   | 섹션 | 내용 |
   |------|------|
   | Overview | 기능 한 줄 요약 + 게임 내 맥락 |
   | Goals | 구현 완료 후 달성해야 할 상태 (동사+명사 bullet) |
   | Out of Scope | 이번 구현에서 명시적으로 제외하는 항목 |
   | Technical Requirements | Unity 컴포넌트·API·물리 설정 등 구체 스펙 |
   | Acceptance Criteria | 완료 판단 기준 (체크리스트 형식) |
   | Open Questions | 미결 사항 |

3. **리뷰 요청** — 초안을 사용자에게 보여주고 수정 피드백을 받는다
4. **파일 저장** — 확정되면 `docs/requirements/<기능명>.md` 에 저장
5. **사용법 안내** — 저장 완료 후 아래 메시지를 출력한다

   ```
   ✅ docs/requirements/<기능명>.md 저장 완료
   이제 Claude에게 이렇게 요청하세요:
   "@docs/requirements/<기능명>.md 참고해서 구현해줘"
   ```

---

## 제약

- 파일은 반드시 `docs/requirements/` 폴더에 저장 (다른 위치 금지)
- 마크다운만 사용, 코드 블록은 Technical Requirements 섹션에만 허용
- Unity 맥락(컴포넌트명·API)을 모르면 추측하지 말고 질문
- 기능명은 kebab-case로 파일명에 사용 (예: `inventory-system.md`)
- 이미 파일이 존재하면 덮어쓰기 전에 사용자에게 확인
