import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
import {readSpecRows} from "../packs/packSpecReader";
import {computeCurrencyPayout, CurrencyPayout} from "../payout";
import {parseRewardRows, RewardRow} from "../rewardTable";
import {CURRENCY_KEYS, CurrencyKey} from "../currency/currencyKeys";
import {grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {mutateWallet} from "../currency/walletTransaction";
import {LOCKED_DECK_SIZE} from "../deckValidation";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 * permission-denied 로만 나간다(save/domainReject): 보상 지급 실패로 세션을 끊지 않는다.
 */
type BattleRewardReject = "RewardUnavailable";

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {BattleRewardReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason: BattleRewardReject, message: string, context: Record<string, unknown>): never {
  rejectDomain(reason, message, context);
}

/**
 * 생존 카드 수를 [0, LOCKED_DECK_SIZE] 로 자른다. **거절하지 않는다** —
 * 클라가 이상한 수를 하나 보냈다고 세션을 끊으면 전투를 이기고도 진행이 막힌다.
 * @param {number} remaining 클라가 보낸 생존 수
 * @return {number} 잘라낸 생존 수
 */
function clampRemaining(remaining: number): number {
  if (!Number.isFinite(remaining)) return 0;
  return Math.min(Math.max(Math.trunc(remaining), 0), LOCKED_DECK_SIZE);
}

/**
 * Battle 지급량을 구한다. computeCurrencyPayout 은 표가 비었거나 win.perCard·win.floor·lose.flat 행이
 * 없거나 승/패 재화가 갈리면 던진다 — 그대로 새면 internal 이 되어 클라 분류가 흐려지므로 여기서 거절로 바꾼다.
 * @param {boolean} won 승리 여부
 * @param {number} remaining 생존 카드 수
 * @param {RewardRow[]} rows Reward 표 전량
 * @param {Record<string, unknown>} context 로그 맥락
 * @return {CurrencyPayout} 지급 재화와 수량
 */
function resolvePayout(
  won: boolean,
  remaining: number,
  rows: RewardRow[],
  context: Record<string, unknown>,
): CurrencyPayout {
  try {
    return computeCurrencyPayout(won, remaining, rows);
  } catch (error) {
    // 저작 사고라 유저가 할 수 있는 것이 없다 — 어느 ownerId 가 깨졌는지가 유일한 단서다.
    logger.error("Battle reward rows are unusable", {...context, error: String(error)});
    reject("RewardUnavailable", `Battle reward rows are unusable: ${String(error)}`, context);
  }
}

/**
 * 싱글 전투 1판의 보상 지급. 금액 공식(perCard × 생존 수 vs floor · 패배는 flat)은 payout.ts 가 갖고,
 * 여기서는 표 읽기 · 클램프 · 지급만 한다.
 *
 * 세이브 문서는 건드리지 않는다 — 이 명령이 움직이는 것은 잔액뿐이라 진행도 슬롯도 revision 도 오를 이유가 없다.
 * 지급량이 0 이하로 나오면 **아무것도 쓰지 않는다** — 빈 지급으로 지갑 rev 만 올리면
 * 클라가 잔액을 갈아끼우고도 달라진 것이 없어 사고를 못 알아챈다.
 */
export const claimBattleReward = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const won = request.data?.won === true;

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }

  const requested = Number(request.data?.remaining ?? 0);
  const remaining = clampRemaining(requested);
  if (remaining !== requested) {
    logger.warn("claimBattleReward remaining clamped", {uid, env, won, requested: request.data?.remaining, remaining});
  }

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  const rows = parseRewardRows(await readSpecRows(env, "Reward"));
  const context = {
    uid, env, won, remaining,
    rowCount: rows.length,
    battleOwnerIds: rows.filter((r) => r.ownerType === "Battle").map((r) => r.ownerId),
  };

  const payout = resolvePayout(won, remaining, rows, context);
  const currency: CurrencyKey | undefined = CURRENCY_KEYS.find((key) => key === payout.currency);
  if (currency === undefined) {
    // parseCurrency 의 Gold 폴백을 쓰지 않는다 — 저작 오타를 조용히 금화로 바꾸면 표가 틀린 채로 굳는다.
    // 그냥 흘려보내도 안 된다: wallet 의 changeBalances 가 NaN 을 만들고 normalize 가 그 키를 버려
    // **0 지급 + 성공 응답**이 된다.
    logger.error("Battle reward currency is not a known key", {...context, currency: payout.currency});
    reject("RewardUnavailable", `Battle reward currency is unknown: ${payout.currency}`, context);
  }
  if (payout.amount <= 0) {
    // error 가 아니라 warn 이다 — 표가 깨진 것이 아니라 "이번엔 줄 것이 없다" 일 수 있다(예: lose.flat 0 저작).
    // 그래도 거절로 접는다: 지급 0으로 지갑을 쓰면 rev 만 오르고 클라는 달라진 것 없는 잔액을 채택한다.
    // 클라는 이 거절을 경고 한 줄로 삼키고 캐리어를 세우지 않는다(획득 연출 없음) — 0 지급의 옳은 표면이다.
    logger.warn("Battle reward amount is not positive", {...context, amount: payout.amount});
    reject("RewardUnavailable", `Battle reward amount is not positive: ${payout.amount}`, context);
  }

  const amount = payout.amount;
  const wallet = await mutateWallet("claimBattleReward", env, uid, (current) =>
    nextWallet(current, grant(current.balances, [{currency, amount}])));

  logger.info("claimBattleReward", {uid, env, won, remaining, currency, amount, rev: wallet.rev});

  return {wallet, granted: {currency, amount}};
});
