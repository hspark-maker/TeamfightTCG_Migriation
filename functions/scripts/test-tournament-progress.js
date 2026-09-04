// reportAdventureWin 의 해금 판정 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다
// (test-tutorial-grant.js 관용구).
//
// 여기서 지키는 것은 네 가지다.
//  1) 사슬이 챕터 경계를 넘는다 — 챕터 1 을 안 깨고 챕터 2 첫 정점을 신고할 수 없다.
//  2) 사슬을 잴 수 없는 표는 통과가 아니라 거절이다 — prevNodeId 가 없던 구 블롭이
//     "전부 첫 정점"으로 읽히면 순서 검사가 통째로 무력해진다.
//  3) 랭크 잠금의 축은 점수다 — 서버에 ERankGrade 가 없으므로 requiredPoints 로만 잰다.
//  4) 챕터 완주 모수(chapterNodeIds)는 파서를 이사한 뒤에도 그대로다.
const assert = require("node:assert/strict");
const {
  chapterNodeIds,
  hasNode,
  judgeNodeUnlock,
  parseChapterNodeRows,
  readNodeIdList,
} = require("../lib/adventureTable.js");

// AdventureChapter 한 줄. 실제 시트의 컬럼 이름 그대로다
// (id | chapterId | nodeId | order | prevNodeId | requiredPoints).
const row = (id, chapterId, nodeId, order, prevNodeId, requiredPoints = 0) =>
  ({id, chapterId, nodeId, order, prevNodeId, requiredPoints});

// 챕터 2개 × 정점 2개. 사슬은 c1n1 → c1n2 → c2n1 → c2n2 로 챕터를 넘어 이어진다.
// 챕터 2 는 랭크 잠금이 걸려 있다(requiredPoints 260).
const TABLE = parseChapterNodeRows([
  row(1, "c1", "c1n1", 0, ""),
  row(2, "c1", "c1n2", 1, "c1n1"),
  row(3, "c2", "c2n1", 0, "c1n2", 260),
  row(4, "c2", "c2n2", 1, "c2n1", 260),
]);

const none = new Set();
const cleared = (...ids) => new Set(ids);

// ── 정상 사슬 ────────────────────────────────────────────────
{
  // 전역 첫 정점은 아무것도 안 깬 상태에서 통과한다.
  assert.deepEqual(judgeNodeUnlock(TABLE, "c1n1", none, 0), {ok: true});

  // 직전 정점을 깼으면 다음이 열린다.
  assert.deepEqual(judgeNodeUnlock(TABLE, "c1n2", cleared("c1n1"), 0), {ok: true});
}

// ── 순서 건너뛰기 ────────────────────────────────────────────
{
  // 같은 챕터 안에서 직전을 건너뛴다.
  assert.deepEqual(
    judgeNodeUnlock(TABLE, "c1n2", none, 0),
    {ok: false, reason: "ChainBlocked"});

  // 챕터 경계를 건너뛴다 — order 만으로는 못 막는 자리다(c2n1 의 order 는 0 이라
  // 챕터 안에서는 첫 정점처럼 보인다). prevNodeId 가 이 케이스의 존재 이유다.
  assert.deepEqual(
    judgeNodeUnlock(TABLE, "c2n1", cleared("c1n1"), 999),
    {ok: false, reason: "ChainBlocked"});

  // 챕터 1 을 완주하면 챕터 2 가 열린다(점수 충족 시).
  assert.deepEqual(
    judgeNodeUnlock(TABLE, "c2n1", cleared("c1n1", "c1n2"), 260),
    {ok: true});
}

// ── 랭크 잠금 ────────────────────────────────────────────────
{
  // 사슬은 통과하는데 점수가 모자란다 — 사슬보다 먼저 걸린다.
  assert.deepEqual(
    judgeNodeUnlock(TABLE, "c2n1", cleared("c1n1", "c1n2"), 259),
    {ok: false, reason: "RankLocked"});

  // 경계값: 정확히 요구 점수면 통과한다.
  assert.deepEqual(
    judgeNodeUnlock(TABLE, "c2n1", cleared("c1n1", "c1n2"), 260),
    {ok: true});

  // requiredPoints 0 인 챕터는 신규 계정(points 0)도 통과한다 — 업로더가 첫 등급을
  // 0 으로 낮추는 이유다(RankConfig.ResolveTierIndex 가 첫 등급 미만도 인덱스 0 으로 읽는다).
  assert.deepEqual(judgeNodeUnlock(TABLE, "c1n1", none, 0), {ok: true});
}

// ── 표를 못 읽는 상태 ────────────────────────────────────────
{
  // 표에 없는 정점.
  assert.deepEqual(
    judgeNodeUnlock(TABLE, "nope", none, 0),
    {ok: false, reason: "NodeNotFound"});

  // 구 블롭 — prevNodeId 열이 통째로 없다. 전부 뿌리로 보이므로 사슬을 잴 수 없다.
  const legacy = parseChapterNodeRows([
    {id: 1, chapterId: "c1", nodeId: "c1n1", order: 0},
    {id: 2, chapterId: "c1", nodeId: "c1n2", order: 1},
  ]);
  assert.equal(legacy[0].prevNodeId, "");
  assert.equal(legacy[0].requiredPoints, 0);
  assert.deepEqual(
    judgeNodeUnlock(legacy, "c1n2", none, 0),
    {ok: false, reason: "ChainUnreadable"});
  // 첫 정점조차 통과시키지 않는다 — 뿌리가 둘이면 어느 쪽이 진짜인지 알 수 없다.
  assert.deepEqual(
    judgeNodeUnlock(legacy, "c1n1", none, 0),
    {ok: false, reason: "ChainUnreadable"});

  // prevNodeId 가 표에 없는 키를 가리킨다 — 저작이 깨졌거나 행이 빠졌다.
  const dangling = parseChapterNodeRows([
    row(1, "c1", "c1n1", 0, ""),
    row(2, "c1", "c1n2", 1, "ghost"),
  ]);
  assert.deepEqual(
    judgeNodeUnlock(dangling, "c1n2", cleared("c1n1"), 0),
    {ok: false, reason: "ChainUnreadable"});
}

// ── 파서 이사 후에도 완주 모수는 그대로 ──────────────────────
{
  assert.deepEqual(chapterNodeIds(TABLE, "c1"), ["c1n1", "c1n2"]);
  assert.deepEqual(chapterNodeIds(TABLE, "c2"), ["c2n1", "c2n2"]);
  // 모수 0 은 완성이 아니다 — 그 판정은 completionTable.isCompleted 몫이고 여기선 빈 배열만 확인한다.
  assert.deepEqual(chapterNodeIds(TABLE, "없는챕터"), []);

  // 키가 빈 줄은 버린다(기존 규약).
  const dirty = parseChapterNodeRows([
    row(1, "", "c1n1", 0, ""),
    row(2, "c1", "", 1, ""),
    row(3, "c1", "c1n1", 0, ""),
  ]);
  assert.equal(dirty.length, 1);
}

// ── 낙인 대조와 슬롯 읽기 ────────────────────────────────────
{
  // claimReward 가 표 밖 낙인을 거르는 근거.
  assert.equal(hasNode(TABLE, "c2n2"), true);
  assert.equal(hasNode(TABLE, "위조노드"), false);

  // 손상된 슬롯에서도 판정이 죽지 않는다.
  assert.deepEqual(readNodeIdList(["a", "a", "", 7, null, "b"]), ["a", "b"]);
  assert.deepEqual(readNodeIdList(undefined), []);
  assert.deepEqual(readNodeIdList("a"), []);
  // 길이 상한은 룰과 같은 값으로 지킨다.
  assert.deepEqual(readNodeIdList(["x".repeat(65)]), []);
  assert.deepEqual(readNodeIdList(["x".repeat(64)]), ["x".repeat(64)]);
}

console.log("test-Adventure-progress: ok");
