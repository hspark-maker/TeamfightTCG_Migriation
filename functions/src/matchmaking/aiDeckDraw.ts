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

export function parseAiDeckRows(rows: Record<string, unknown>[]): AiDeckRow[] {
  const seen = new Set<string>();
  return rows.map((row) => {
    const deckId = String(row.deckId ?? "");
    if (deckId.length === 0 || seen.has(deckId)) throw new Error("invalid AIDeck.deckId");
    seen.add(deckId);

    const fromTier = integer(row.fromTier, `${deckId}.fromTier`);
    const toTier = integer(row.toTier, `${deckId}.toTier`);
    if (fromTier < 0 || (toTier !== 0 && toTier < fromTier)) {
      throw new Error(`invalid ${deckId} tier range`);
    }

    const cardIds = [1, 2, 3, 4, 5, 6].map((slot) =>
      integer(row[`card${slot}`], `${deckId}.card${slot}`));
    if (cardIds.some((cardId) => cardId <= 0)) throw new Error(`invalid ${deckId} cards`);

    return {
      deckId,
      fromTier,
      toTier,
      weight: integer(row.weight, `${deckId}.weight`),
      fromLevel: integer(row.fromLevel, `${deckId}.fromLevel`),
      toLevel: integer(row.toLevel, `${deckId}.toLevel`),
      cardIds,
    };
  });
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
