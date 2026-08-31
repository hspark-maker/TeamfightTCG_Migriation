import {createHash} from "node:crypto";
import {Timestamp} from "firebase-admin/firestore";

export type Submission = {
  uid: string;
  seedSource?: "server" | "commit_reveal";
  myNonce: string;
  opponentNonce: string;
  myDeckHash: string;
  opponentDeckHash: string;
  finalStateHash: string;
  stateHashChain: string;
  stateHashChainPrev: string;
  stateHashChainLength: number;
  contentFingerprint: string;
  won: boolean;
  myRemaining: number;
  opponentRemaining: number;
  rankPointsBefore?: number;
  commandLogVersion?: number;
  commandLog?: string;
  commandLogHash?: string;
  commandCount?: number;
  commandLogTruncated?: boolean;
  /** 셔플 후 초기 보드 순서. 서버가 시드로 산출할 수 없어 클라가 실어 보낸다 —
   *  신뢰는 양쪽 제출이 같은지로만 세운다(sameBoardOrder).
   *  **Firestore 는 중첩 배열을 저장하지 못한다** — 그래서 [][] 가 아니라 소유자별 맵이다. */
  boardOrder?: {owner0: number[]; owner1: number[]};
  /** 무승부(양쪽 동시 전멸). true면 양쪽 won 이 모두 false 이므로 승자 대조를 건너뛴다. */
  draw?: boolean;
  /** 전투 종료 시점 보드 해시. finalStateHash(= 마지막 합의 해시)와 다르다 —
   *  마지막 턴은 상대와 교환할 기회가 없어 합의 목록에 없다. 서버 재시뮬 대조는 이 값과 한다. */
  endStateHash?: string;
  submittedAt: Timestamp;
};

export function sameSubmission(a: Submission, b: Submission): boolean {
  return a.uid === b.uid && (a.seedSource ?? "commit_reveal") === (b.seedSource ?? "commit_reveal") &&
    a.myNonce === b.myNonce && a.opponentNonce === b.opponentNonce &&
    a.myDeckHash === b.myDeckHash && a.opponentDeckHash === b.opponentDeckHash &&
    a.finalStateHash === b.finalStateHash && a.stateHashChain === b.stateHashChain &&
    a.stateHashChainPrev === b.stateHashChainPrev &&
    a.stateHashChainLength === b.stateHashChainLength &&
    a.contentFingerprint === b.contentFingerprint && a.won === b.won &&
    a.myRemaining === b.myRemaining && a.opponentRemaining === b.opponentRemaining &&
    a.rankPointsBefore === b.rankPointsBefore &&
    (a.commandLogVersion ?? 0) === (b.commandLogVersion ?? 0) &&
    (a.commandLog ?? "") === (b.commandLog ?? "") &&
    (a.commandLogHash ?? "") === (b.commandLogHash ?? "") &&
    (a.commandCount ?? 0) === (b.commandCount ?? 0) &&
    Boolean(a.commandLogTruncated) === Boolean(b.commandLogTruncated);
}

export function expectedMatchId(myNonce: string, opponentNonce: string): string {
  const a = Buffer.from(myNonce, "hex");
  const b = Buffer.from(opponentNonce, "hex");
  const seed = Buffer.alloc(8);
  for (let i = 0; i < seed.length; i++) seed[i] = a[i] ^ b[i];
  return createHash("sha256").update(seed).digest("hex").slice(0, 32);
}

/**
 * 두 제출의 보드 순서가 같은가. 한쪽이라도 없으면 대조 자체를 못 하므로 불일치로 본다.
 * @param {Submission} a 한쪽 제출.
 * @param {Submission} b 다른 쪽 제출.
 * @return {boolean} 두 제출의 보드 순서가 완전히 같으면 true.
 */
export function sameBoardOrder(a: Submission, b: Submission): boolean {
  const x = a.boardOrder;
  const y = b.boardOrder;
  if (x == null || y == null) return false;
  for (const side of ["owner0", "owner1"] as const) {
    const left = x[side];
    const right = y[side];
    if (!Array.isArray(left) || !Array.isArray(right) || left.length !== right.length) return false;
    for (let i = 0; i < left.length; i++) if (left[i] !== right[i]) return false;
  }
  return true;
}

export function submissionsAgree(a: Submission, b: Submission): string | null {
  if (a.uid === b.uid) return "same_uid";
  // 무승부는 양쪽 won 이 모두 false 다 — 승자 대조를 그대로 돌리면 winner_conflict 로 튕긴다.
  // 한쪽만 무승부를 주장하면 판정이 갈린 것이므로 불일치다.
  if ((a.draw ?? false) !== (b.draw ?? false)) return "draw_conflict";
  if (!(a.draw ?? false) && a.won === b.won) return "winner_conflict";
  if ((a.draw ?? false) && (a.won || b.won)) return "draw_conflict";
  const seedSource = a.seedSource ?? "commit_reveal";
  if (seedSource !== (b.seedSource ?? "commit_reveal")) return "seed_source_mismatch";
  if (seedSource === "commit_reveal" &&
      (a.myNonce !== b.opponentNonce || a.opponentNonce !== b.myNonce)) return "nonce_mismatch";
  if (a.myDeckHash !== b.opponentDeckHash || a.opponentDeckHash !== b.myDeckHash) return "deck_mismatch";
  if (a.finalStateHash !== b.finalStateHash) return "state_hash_mismatch";
  const chainsAgree = a.stateHashChainLength === b.stateHashChainLength ?
    a.stateHashChain === b.stateHashChain :
    a.stateHashChainLength === b.stateHashChainLength + 1 ?
      a.stateHashChainPrev === b.stateHashChain :
      b.stateHashChainLength === a.stateHashChainLength + 1 &&
        b.stateHashChainPrev === a.stateHashChain;
  if (!chainsAgree) return "state_chain_mismatch";
  if (a.contentFingerprint !== b.contentFingerprint) return "content_mismatch";
  if (a.myRemaining !== b.opponentRemaining || a.opponentRemaining !== b.myRemaining) return "remaining_mismatch";
  const commandVersion = a.commandLogVersion ?? 0;
  if (commandVersion !== (b.commandLogVersion ?? 0)) return "command_log_version_mismatch";
  if (commandVersion > 0) {
    if (a.commandLogTruncated || b.commandLogTruncated) return "command_log_truncated";
    if (a.commandCount !== b.commandCount || a.commandLogHash !== b.commandLogHash || a.commandLog !== b.commandLog) {
      return "command_log_mismatch";
    }
  }
  return null;
}

export type MatchDecision = {
  status: "pending" | "flagged" | "confirmed";
  reason?: string;
};

export function decideMatch(
  entries: Submission[],
  createdAtMs: number,
  nowMs: number,
  deadlineMs: number,
): MatchDecision {
  if (entries.length < 2) {
    return nowMs - createdAtMs > deadlineMs ?
      {status: "flagged", reason: "single_submission"} : {status: "pending"};
  }
  if (entries.length > 2) return {status: "flagged", reason: "too_many_submissions"};
  const reason = submissionsAgree(entries[0], entries[1]);
  return reason ? {status: "flagged", reason} : {status: "confirmed"};
}
