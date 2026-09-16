import {objectRecord} from "../match/payloadGuards";

/**
 * Only settled server match records can determine battle experience.
 * @param {unknown} raw Server match document.
 * @param {string} uid Authenticated caller.
 * @return {object} Eligibility and server-determined outcome.
 */
export function confirmedBattleExperienceOutcome(raw: unknown, uid: string):
  {allow: true; won: boolean} | {allow: false; reason: string} {
  const match = objectRecord(raw);
  const participants = match?.participantUids;
  if (match?.seedSource !== "server" ||
      !Array.isArray(participants) || !participants.includes(uid)) {
    return {allow: false, reason: "MatchNotOwned"};
  }
  if (match.status !== "confirmed") return {allow: false, reason: "MatchNotConfirmed"};
  // resultProtocol is authored only for solo matches; paired PvP records have no such field.
  const validIdentity = match.mode === "solo" ?
    match.resultProtocol === 1 && match.expectedParticipants === 1 && participants.length === 1 :
    match.mode === "pvp" && match.expectedParticipants === 2 && participants.length === 2;
  if (!validIdentity) return {allow: false, reason: "MatchNotOwned"};

  if (typeof match.adventureNodeId === "string") {
    const simulation = objectRecord(match.serverSimulation);
    if (match.mode !== "solo" || match.expectedParticipants !== 1 ||
        participants.length !== 1 || participants[0] !== uid ||
        simulation?.ok !== true || typeof simulation.draw !== "boolean" ||
        (!simulation.draw && simulation.winnerOwner !== 0 && simulation.winnerOwner !== 1)) {
      return {allow: false, reason: "MatchOutcomeUnavailable"};
    }
    return {allow: true, won: !simulation.draw && simulation.winnerOwner === 0};
  }

  const payout = objectRecord(objectRecord(match.payouts)?.[uid]);
  if (typeof payout?.won !== "boolean") return {allow: false, reason: "MatchOutcomeUnavailable"};
  return {allow: true, won: payout.won};
}
