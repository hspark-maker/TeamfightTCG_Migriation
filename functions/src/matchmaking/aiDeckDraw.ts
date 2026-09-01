export type AiDeckRow = {
  deckId: string;
  fromTier: number;
  toTier: number;
  weight: number;
  fromLevel: number;
  toLevel: number;
  cardIds: number[];
};

export type AiDeckDraw = {
  deckId: string;
  deck: number[];
  cardLevel: number;
};

type Roll = (maxExclusive: number) => number;

function integer(value: unknown, field: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value)) {
    throw new Error(`invalid ${field}`);
  }
  return value;
}

export type AiDeckParse = {
  rows: AiDeckRow[];
  /** 저작이 깨져 후보에서 뺀 행의 사유. 호출부가 로그로 남긴다. */
  skipped: string[];
};

/**
 * 오저작 행은 후보에서 빼고 나머지로 진행한다 — 한 행이 깨졌다고 전부 던지면
 * 시트 오타 하나가 AI 매칭 전체를 멈춘다(서버에는 클라 같은 SO 폴백이 없다).
 * 남은 행이 하나도 없을 때만 호출부가 실패로 접는다.
 * @param {Record<string, unknown>[]} rows AIDeck 표의 행들
 * @return {AiDeckParse} 유효 행과 제외 사유
 */
export function parseAiDeckRows(rows: Record<string, unknown>[]): AiDeckParse {
  const seen = new Set<string>();
  const parsed: AiDeckRow[] = [];
  const skipped: string[] = [];

  for (const row of rows) {
    const deckId = String(row.deckId ?? "");
    if (deckId.length === 0 || seen.has(deckId)) {
      skipped.push(`empty or duplicate deckId: '${deckId}'`);
      continue;
    }
    seen.add(deckId);

    try {
      const fromTier = integer(row.fromTier, `${deckId}.fromTier`);
      const toTier = integer(row.toTier, `${deckId}.toTier`);
      if (fromTier < 0 || (toTier !== 0 && toTier < fromTier)) {
        throw new Error(`invalid ${deckId} tier range`);
      }

      const cardIds = [1, 2, 3, 4, 5, 6].map((slot) =>
        integer(row[`card${slot}`], `${deckId}.card${slot}`));
      if (cardIds.some((cardId) => cardId <= 0)) throw new Error(`invalid ${deckId} cards`);

      parsed.push({
        deckId,
        fromTier,
        toTier,
        weight: integer(row.weight, `${deckId}.weight`),
        fromLevel: integer(row.fromLevel, `${deckId}.fromLevel`),
        toLevel: integer(row.toLevel, `${deckId}.toLevel`),
        cardIds,
      });
    } catch (error) {
      skipped.push(error instanceof Error ? error.message : String(error));
    }
  }

  return {rows: parsed, skipped};
}

function pickWeighted(rows: AiDeckRow[], tier: number, roll: Roll): AiDeckRow | null {
  const candidates = rows.filter((row) =>
    row.fromTier <= tier && (row.toTier === 0 || tier <= row.toTier));
  const total = candidates.reduce((sum, row) => sum + (row.weight > 0 ? row.weight : 1), 0);
  if (total <= 0 || !Number.isSafeInteger(total)) return null;

  let value = roll(total);
  for (const candidate of candidates) {
    value -= candidate.weight > 0 ? candidate.weight : 1;
    if (value < 0) return candidate;
  }
  return null;
}

function cardLevel(row: AiDeckRow, roll: Roll): number {
  if (row.fromLevel <= 0 || row.toLevel <= 0) return 0;
  const min = Math.min(row.fromLevel, row.toLevel);
  const max = Math.max(row.fromLevel, row.toLevel);
  return min + roll(max - min + 1);
}

export function drawAiDeck(rows: AiDeckRow[], tier: number, roll: Roll): AiDeckDraw | null {
  if (rows.length === 0) return null;

  let selected: AiDeckRow | null = null;
  for (let candidateTier = Math.max(0, tier); candidateTier >= 0; candidateTier--) {
    selected = pickWeighted(rows, candidateTier, roll);
    if (selected !== null) break;
  }
  selected ??= rows[roll(rows.length)] ?? null;
  if (selected === null) return null;

  return {
    deckId: selected.deckId,
    deck: [...selected.cardIds],
    cardLevel: cardLevel(selected, roll),
  };
}
