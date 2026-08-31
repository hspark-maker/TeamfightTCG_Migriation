// grantTutorialCards 순수 모듈 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-claim-reward.js 관용구).
//
// 여기서 지키는 것은 세 가지다.
//  1) 못 읽는 줄이 소유 목록에 들어가지 않는다 — 존재하지 않는 카드 id 는 클라에서 되돌릴 방법이 없다.
//  2) 지급 순서가 order 로 결정된다 — 튜토리얼은 준 순서대로 연출을 태운다.
//  3) 재호출이 무해하다 — 이 명령에는 낙인이 없어서 멱등성이 유일한 안전장치다.
const assert = require("node:assert/strict");
const {
  parseTutorialGrantRows,
  stepGrantCardIds,
} = require("../lib/tutorialGrantTable.js");
const {
  buildOwnershipSlotFromIds,
  readOwnedIds,
} = require("../lib/packs/packSlots.js");

// 표 한 줄. 실제 TutorialGrant 시트의 컬럼 이름 그대로다(id | stepId | cardId | order).
const row = (id, stepId, cardId, order) => ({id, stepId, cardId, order});

// ── 못 읽는 줄은 버린다 ─────────────────────────────────────────────────────
{
  const rows = parseTutorialGrantRows([
    row(1, 2, 1, 1),
    row(2, 0, 28, 2),
    row(3, -1, 20, 3),
    row(4, 2, 0, 4),
    row(5, 2, -7, 5),
    row(6, 2, 6, 6),
    {id: "x", stepId: "2", cardId: "11", order: "7"},
  ]);
  assert.deepEqual(rows.map((r) => r.cardId), [1, 6, 11], "stepId·cardId 가 0 이하인 줄만 빠져야 한다");
  assert.deepEqual(stepGrantCardIds(rows, 2), [1, 6, 11]);
}

// ── order 정렬 · 중복 제거 ──────────────────────────────────────────────────
{
  const rows = parseTutorialGrantRows([
    row(10, 2, 30, 6),
    row(11, 2, 1, 1),
    row(12, 2, 20, 3),
    row(13, 2, 28, 2),
    row(14, 2, 8, 7),
    row(15, 2, 6, 4),
    row(16, 2, 11, 5),
    row(17, 3, 2, 1),
  ]);
  // 실측 저작값: stepId 2 → 1,28,20,6,11,30,8
  assert.deepEqual(stepGrantCardIds(rows, 2), [1, 28, 20, 6, 11, 30, 8]);
  assert.deepEqual(stepGrantCardIds(rows, 3), [2], "다른 단계 행이 섞이지 않는다");

  // order 가 같으면 id 가 순서를 가른다 — 표 입력 순서에 기대지 않는다.
  const tied = parseTutorialGrantRows([row(9, 15, 4, 1), row(2, 15, 26, 1), row(5, 15, 3, 1)]);
  assert.deepEqual(stepGrantCardIds(tied, 15), [26, 3, 4]);

  // 같은 카드가 두 줄이면 앞선 자리 하나만 남는다.
  const duped = parseTutorialGrantRows([row(1, 4, 2, 2), row(2, 4, 1, 1), row(3, 4, 2, 3)]);
  assert.deepEqual(stepGrantCardIds(duped, 4), [1, 2]);
}

// ── 없는 stepId 는 빈 배열 — 거절은 호출부 몫이다 ───────────────────────────
{
  const rows = parseTutorialGrantRows([row(1, 2, 1, 1), row(2, 3, 2, 1)]);
  assert.deepEqual(stepGrantCardIds(rows, 99), []);
  assert.deepEqual(stepGrantCardIds(rows, 1), []);
}

// ── 소유 슬롯: 기존 순서 보존 · 멱등 ────────────────────────────────────────
{
  const owned = [5, 3, 9];
  const granted = [1, 2, 20, 8, 28, 6];

  const once = buildOwnershipSlotFromIds(owned, granted);
  assert.deepEqual(once.cardIds, [5, 3, 9, 1, 2, 20, 8, 28, 6], "기존 소유 순서 뒤에 신규만 붙는다");

  // 같은 입력 2회가 같은 출력 — 낙인이 없으므로 재호출은 여기서 무해해져야 한다.
  const twice = buildOwnershipSlotFromIds(once.cardIds, granted);
  assert.deepEqual(twice.cardIds, once.cardIds);

  // 이미 가진 카드는 조용히 skip 하고, 0 이하는 들어가지 않는다.
  assert.deepEqual(buildOwnershipSlotFromIds([1, 2], [2, 0, -3, 7, 1]).cardIds, [1, 2, 7]);
  assert.deepEqual(buildOwnershipSlotFromIds([], []).cardIds, []);

  // 문서에서 읽어 온 소유도 같은 규약이다(readOwnedIds → 슬롯 빌더).
  const fromDocument = readOwnedIds({cardIds: [5, "3", 3, 0, 9]});
  assert.deepEqual(buildOwnershipSlotFromIds(fromDocument, granted).cardIds, once.cardIds);
}

// ── 표 전량이 비면 어떤 stepId 도 빈 배열 ───────────────────────────────────
{
  const rows = parseTutorialGrantRows([]);
  assert.equal(rows.length, 0);
  for (const stepId of [2, 3, 4, 15]) {
    assert.deepEqual(stepGrantCardIds(rows, stepId), [], `표가 비면 step ${stepId} 도 빈 배열이다`);
  }

  // 전 줄이 못 읽는 값이어도 같다 — 부분 지급이 새어 나가면 안 된다.
  assert.deepEqual(stepGrantCardIds(parseTutorialGrantRows([row(1, 0, 0, 0)]), 2), []);
}

console.log("test-tutorial-grant: ok");
