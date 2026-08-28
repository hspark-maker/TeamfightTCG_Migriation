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

export function submissionsAgree(a: Submission, b: Submission): string | null {
  if (a.uid === b.uid) return "same_uid";
  if (a.won === b.won) return "winner_conflict";
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
