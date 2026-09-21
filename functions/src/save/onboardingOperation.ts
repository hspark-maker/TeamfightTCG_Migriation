import {createHash} from "node:crypto";
import {isClientReceiptId} from "./receiptId";

/**
 * 서버가 실행하는 인자만 정규화하여 명령 재사용을 검증한다.
 * @param {string} command 서버 명령
 * @param {Record<string, unknown>} args 명령 인자
 * @return {string} 인자 지문
 */
export function onboardingFingerprint(command: string, args: Record<string, unknown>): string {
  let normalized: Record<string, unknown>;
  switch (command) {
  case "openPack":
  case "grantTutorialCards":
    normalized = {packId: String(args.packId ?? "")};
    break;
  case "enhanceCard":
  case "enhanceSynergyIntroduction":
    normalized = {cardId: Number(args.cardId ?? 0), amount: args.amount ?? 1,
      freeShot: command === "enhanceSynergyIntroduction" || args.freeShot === true};
    break;
  // 폐기된 한계돌파의 과거 처리 기록 조회용 지문. 실행 callable은 제공하지 않는다.
  case "limitBreakCard":
    normalized = {cardId: Number(args.cardId ?? 0)};
    break;
  // 폐기 명령의 읽기 전용 복구 호환. 과거 영수증 조회용 지문만 유지하며 callable은 제공하지 않는다.
  case "enhanceKeyword":
    normalized = {keyword: Number(args.keyword ?? 0), freeShot: args.freeShot === true};
    break;
  default:
    throw new Error(`Unsupported onboarding command: ${command}`);
  }
  return createHash("sha256").update(JSON.stringify({command, args: normalized})).digest("hex");
}

/**
 * 온보딩 요청은 재시도 가능한 클라이언트 식별자를 반드시 지닌다.
 * @param {Record<string, unknown>} data 요청
 * @param {string} command 서버 명령
 * @return {object} 선택적 온보딩 지문
 */
export function onboardingReceipt(
  data: Record<string, unknown>, command: string,
): {onboarding?: string} {
  if (data.onboarding !== true) return {};
  if (!isClientReceiptId(data.txId)) throw new Error("Onboarding requires a valid client txId.");
  return {onboarding: onboardingFingerprint(command, data)};
}

/**
 * 과거 명령 결과에 최신 상태로 오인할 수 있는 스냅샷을 싣지 않는다.
 * @param {Record<string, unknown>} cached 영수증 캐시
 * @return {Record<string, unknown>} 불변 결과
 */
export function onboardingResult(cached: Record<string, unknown>): Record<string, unknown> {
  const result = {...cached};
  delete result.updatedSlots;
  delete result.slotKeys;
  delete result.wallet;
  delete result.missions;
  delete result.achievements;
  delete result.statistics;
  return result;
}
