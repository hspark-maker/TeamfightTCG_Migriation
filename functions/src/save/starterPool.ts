import {STARTER_DECK_SIZE} from "./freshAccount";

/** 스타터 덱을 저작한 팩. 클라 Assets/SO/CardPack/TutorialPack/StarterPack.asset 의 packId. */
export const STARTER_PACK_ID = "StarterPack";

/**
 * 시트에서 스타터를 해석하지 못했을 때 쓰는 목록.
 * 진실원은 Assets/SO/CardPack/TutorialPack/StarterPack.asset 의 poolIds — 저기가 바뀌면 여기도 바꾼다.
 */
export const FALLBACK_STARTER_CARD_IDS = [1, 28, 20, 6, 11, 30];

/** 클라 ERankGrade 의 쌍둥이. 값 순서가 등급 높낮이다. */
const GRADE_ORDER: Record<string, number> = {
  Bronze: 0, Silver: 1, Gold: 2, Platinum: 3, Diamond: 4,
};

/** 신규 계정은 rank.points 가 0 이라 언제나 최하위 등급이다. */
export const FRESH_ACCOUNT_GRADE = GRADE_ORDER.Bronze;

const GRADE_MAX = GRADE_ORDER.Diamond;

/** CardPackDrop 표의 한 행. 업로더(SpecFirestoreUploader)가 만드는 필드 중 해석에 쓰는 것만. */
export interface DropRow {
  id: number;
  minGrade: string;
  cardId: number;
}

/**
 * 등급 문자열을 순번으로. 클라 PackSpec.ParseGrade 는 Enum.TryParse 라 이름과 숫자 문자열을
 * 모두 받고 실패하면 최하위로 떨어진다 — 시트가 숫자로 저작돼도 두 쪽이 같은 행을 고르도록 맞춘다.
 * @param {string} value minGrade 열 값
 * @return {number} 등급 순번
 */
export function parseGrade(value: string): number {
  const named = GRADE_ORDER[value];
  if (named !== undefined) return named;

  // Enum.TryParse 는 정의 범위 밖 정수도 받아들이지만, 그런 값은 어떤 등급과도 같지 않아
  // 아래 best 비교에서 자연히 탈락한다. 범위 안일 때만 등급으로 인정한다.
  const numeric = Number(value);
  if (Number.isInteger(numeric) && numeric >= 0 && numeric <= GRADE_MAX) return numeric;

  return 0;
}

/**
 * 드롭 행에서 스타터 카드 목록을 뽑는다. 클라 PackSpec.ResolveDrops + StarterDeck.TakeDeckCards 의 재현이다.
 *
 * 만족하는 등급 중 **가장 높은 하나만** 쓴다(하위 합산 없음). weight 는 보지 않는다 —
 * 스타터는 추첨이 아니라 풀 앞에서부터의 고정 순서 복사다.
 * @param {DropRow[]} rows StarterPack 행 전부
 * @param {number} grade 해석 기준 등급 순번
 * @param {Set<number>} knownCardIds 카탈로그에 있는 카드 id. 비어 있으면 존재 검사를 건너뛴다
 * @return {number[]} 카드 id (STARTER_DECK_SIZE 장, 못 채우면 빈 배열)
 */
export function resolveStarterCardsFromRows(
  rows: DropRow[],
  grade: number,
  knownCardIds: Set<number> = new Set(),
): number[] {
  // id 를 못 읽는 행은 순서를 정할 수 없다 — 비교자가 NaN 을 뱉어 정렬 자체가 미정의가 된다.
  // 클라는 이런 표를 통째로 거부하므로 여기서도 버리고, 6장을 못 채우면 폴백으로 간다.
  const usable = rows.filter((row) => Number.isInteger(row.id));
  const affordable = usable.filter((row) => parseGrade(row.minGrade) <= grade);
  if (affordable.length === 0) return [];

  let best = parseGrade(affordable[0].minGrade);
  for (const row of affordable) {
    const value = parseGrade(row.minGrade);
    if (value > best) best = value;
  }

  // 문서 id 는 표의 id 열이고 업로더가 정수 오름차순으로 올린다.
  // 문자열 정렬로 두면 "10" 이 "2" 앞에 서서 클라와 다른 카드가 뽑힌다.
  const selected = affordable
    .filter((row) => parseGrade(row.minGrade) === best)
    .sort((a, b) => a.id - b.id);

  const cardIds: number[] = [];
  for (const row of selected) {
    if (!Number.isInteger(row.cardId) || row.cardId <= 0) continue;
    // 클라 PackSpec 은 CardCatalog.Contains 로 거른다. 서버가 이 필터를 잃으면 카탈로그에 없는
    // 카드가 덱에 실리고, 클라 DeckSaveManager.IsSlotValid 가 그 슬롯을 무효로 봐 덱 0개로 초기화된다
    // (StarterDeck 은 CardIds 가 비지 않아 재지급도 안 한다).
    if (knownCardIds.size > 0 && !knownCardIds.has(row.cardId)) continue;
    if (cardIds.includes(row.cardId)) continue;

    cardIds.push(row.cardId);
    if (cardIds.length === STARTER_DECK_SIZE) break;
  }

  // 덱은 정확히 STARTER_DECK_SIZE 장이어야 성립한다 — 모자라면 부분 지급 대신 폴백으로 간다.
  return cardIds.length === STARTER_DECK_SIZE ? cardIds : [];
}
