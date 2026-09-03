import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {withCountedTransaction} from "../observability/countedTransaction";
import {
  isKnownEnv,
  requireUid,
  saveDocument,
} from "../save/saveDocument";

/** 옛 이름 → 새 이름. 슬롯을 개명할 때마다 여기 한 줄을 더한다(값은 그대로 옮긴다). */
const SLOT_RENAMES: ReadonlyArray<{from: string; to: string}> = [
  // 2026-09-03 모험 도메인 개명(Tournament → Adventure).
  {from: "tournament", to: "adventure"},
];

/** 슬롯이 통째로 없을 때 세워 줄 빈 값. 룰의 hasAll 이 전 슬롯의 존재를 요구한다. */
const EMPTY_SLOTS: Readonly<Record<string, unknown>> = {
  adventure: {clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: ""},
};

interface RepairOutcome {
  repaired: boolean;
  renamed: string[];
  filled: string[];
}

/**
 * 개명된 세이브 슬롯을 새 이름으로 옮긴다. 초기화가 부르는 멱등 명령이다.
 *
 * 왜 서버여야 하나 — 룰의 `isValidSave` 는 최상위 필드를 전수 검증한다(hasOnly + hasAll).
 * 옛 이름이 남은 문서는 그 검사를 **어느 쪽으로도** 통과하지 못한다: 옛 키는 목록에 없어 hasOnly 가 막고,
 * 새 키는 없어 hasAll 이 막는다. 그래서 클라는 스스로 이 문서를 고칠 수 없고, 고칠 때까지
 * 그 계정의 모든 저장이 거부된다(그 상태로 접속하면 저장만 안 되고 튕긴다).
 * Admin SDK 는 룰을 우회하므로 여기서만 손댈 수 있다.
 *
 * revision 을 올리지 않는다 — 이건 값의 변경이 아니라 키 이름의 정정이다. 올리면 그 계정 클라가
 * "내가 모르는 쓰기가 서버에서 일어났다"로 읽어 세션을 막는다(PlayerSaveCloud.AdoptServerResult 의
 * `revision == Revision + 1` 계약). 같은 이유로 updatedAt 도 건드리지 않는다.
 */
export const repairSaveSlots = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }

  const reference = saveDocument(env, uid);

  const outcome = await withCountedTransaction<RepairOutcome>(
    "repairSaveSlots", async (transaction) => {
      const snapshot = await transaction.get(reference);
      if (!snapshot.exists) {
        // ensureAccount 가 먼저다. 여기서 문서를 만들면 스타터 지급이 두 곳으로 갈린다.
        throw new HttpsError(
          "failed-precondition",
          "Save document does not exist. Call ensureAccount first.",
        );
      }

      const current = snapshot.data() ?? {};
      const patch: Record<string, unknown> = {};
      const renamed: string[] = [];
      const filled: string[] = [];

      for (const rename of SLOT_RENAMES) {
        const hasOld = Object.prototype.hasOwnProperty.call(current, rename.from);
        if (!hasOld) continue;

        const hasNew = Object.prototype.hasOwnProperty.call(current, rename.to);

        // 새 키가 이미 있으면 그쪽이 최신이다 — 옛 키만 걷는다. 덮어쓰면 개명 이후의 진행이 사라진다.
        if (!hasNew) {
          patch[rename.to] = current[rename.from];
          renamed.push(`${rename.from}->${rename.to}`);
        } else {
          renamed.push(`${rename.from}(dropped)`);
        }

        patch[rename.from] = FieldValue.delete();
      }

      // 개명과 무관하게 슬롯이 비어 있는 문서도 hasAll 에 걸린다 — 빈 값으로 세워 준다.
      for (const [slot, empty] of Object.entries(EMPTY_SLOTS)) {
        if (Object.prototype.hasOwnProperty.call(current, slot)) continue;
        if (Object.prototype.hasOwnProperty.call(patch, slot)) continue;

        patch[slot] = empty;
        filled.push(slot);
      }

      if (Object.keys(patch).length === 0) {
        return {repaired: false, renamed, filled};
      }

      transaction.update(reference, patch);

      return {repaired: true, renamed, filled};
    });

  if (outcome.repaired) {
    logger.info("repairSaveSlots", {
      uid, env, renamed: outcome.renamed, filled: outcome.filled,
    });
  }

  return outcome;
});
