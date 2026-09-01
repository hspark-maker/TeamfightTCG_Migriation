/**
 * 스펙 표를 **블롭 문서 하나**로 읽는다. 원천은 `envs/{env}/specs/{table}/blob/current` 다.
 *
 * 업로더(`Assets/Scripts/Editor/SpecFirestoreUploader.cs`)는 같은 commit 으로 메타 · 블롭 · `rows/`
 * 를 함께 쓴다. `rows/` 는 콘솔 열람용 미러라서 런타임이 그걸 읽으면 **읽기 과금이 행 수에 비례**한다
 * — Card 41행 · Reward 85행 · CardPackDrop 322행이라 표 하나 훑을 때마다 수백 건이 찍혔다.
 * 블롭은 표 크기와 무관하게 항상 1건이다.
 *
 * 블롭이 없는 표(블롭 도입 전에 올라간 표)는 `rows/` 로 폴백한다 — 재업로드 전까지 서버가 멈추면
 * 안 되기 때문이다. 폴백은 로그를 남기므로 남은 표를 추적할 수 있다.
 *
 * 단 미러가 최신일 때만 폴백한다. 업로더는 `rows/` 미러를 끄고 올릴 수 있고(쓰기 비용 절감), 그때
 * 메타의 `rowsRevision` 이 `revision` 보다 뒤에 남는다. 그 상태에서 폴백하면 **낡은 마스터 데이터로**
 * 보상·추첨을 판정하게 되므로, 조용히 옛 값을 쓰는 대신 실패시킨다.
 */

import {createHash} from "node:crypto";
import * as logger from "firebase-functions/logger";
import {db} from "../firebaseApp";

/** 업로더 `SpecPayloadCodec.SchemaVersion` 과 맞물린 값. 어긋나면 블롭을 믿지 않고 폴백한다. */
const SCHEMA_VERSION = 4;

/** 캐시 수명. 스펙 업로드가 반영되기까지 최대 이만큼 늦는다. */
const CACHE_TTL_MS = 5 * 60 * 1000;

/** 표 한 행. 열 이름 → 값. 정수로 읽히는 값은 number, 나머지는 string 이다. */
export type SpecRow = Record<string, unknown>;

interface CacheEntry {
  rows: SpecRow[];
  expiresAt: number;
}

const cache = new Map<string, CacheEntry>();

/**
 * 블롭 payload 해시. 업로더 `HashOf` 와 같은 규칙 — MD5 앞 8바이트를 hex 로.
 * @param {string} payload 블롭의 payload 필드
 * @return {string} 16자 hex 해시
 */
export function specPayloadHash(payload: string): string {
  return createHash("md5").update(payload, "utf8").digest("hex").slice(0, 16);
}

/**
 * 값 하나를 행 문서와 같은 모양으로 되돌린다.
 *
 * 블롭 payload 는 모든 값이 문자열이지만 `rows/` 문서는 int · long 열을 `integerValue` 로 썼다.
 * `payout.finiteInteger` 처럼 `typeof value === "number"` 를 요구하는 파서가 있어 그대로 넘기면 깨진다.
 * 열 타입표가 블롭에 없으므로 "정수로 읽히면 number" 규칙을 쓴다 — 숫자만 든 문자열 열(rewardId 등)은
 * number 가 되지만 소비자가 모두 `String(...)` 로 받아 결과가 같다. 빈 문자열은 0 으로 바꾸지 않는다.
 * @param {string} text payload 의 원본 문자열 값
 * @return {string | number} 정수면 number, 아니면 원본 문자열
 */
function coerce(text: string): string | number {
  if (!/^-?\d+$/.test(text)) return text;
  const parsed = Number(text);
  return Number.isSafeInteger(parsed) ? parsed : text;
}

/**
 * payload 텍스트(`[[열…],[값…],…]`)를 행 객체 배열로 편다.
 * @param {string} payload 블롭의 payload 필드
 * @return {SpecRow[]} 열 이름이 붙은 행. 모양이 어긋나면 예외를 던진다.
 */
export function parseSpecPayload(payload: string): SpecRow[] {
  const matrix = JSON.parse(payload) as unknown;
  if (!Array.isArray(matrix) || matrix.length < 2) throw new Error("payload has no rows");

  const columns = matrix[0];
  if (!Array.isArray(columns) || columns.some((column) => typeof column !== "string")) {
    throw new Error("payload header is not a string array");
  }

  const rows: SpecRow[] = [];
  for (let index = 1; index < matrix.length; index++) {
    const values = matrix[index];
    if (!Array.isArray(values) || values.length !== columns.length) {
      throw new Error(`payload row ${index} has ${
        Array.isArray(values) ? values.length : "?"} values, expected ${columns.length}`);
    }
    const row: SpecRow = {};
    for (let column = 0; column < columns.length; column++) {
      const value = values[column];
      if (typeof value !== "string") throw new Error(`payload row ${index} is not a string array`);
      row[columns[column] as string] = coerce(value);
    }
    rows.push(row);
  }
  return rows;
}

/**
 * 블롭에서 표를 읽는다. 블롭이 없거나 무결성이 어긋나면 null 을 돌려 폴백을 부른다.
 * @param {string} env 환경 id
 * @param {string} table 표 이름
 * @return {Promise<SpecRow[] | null>} 행 배열, 못 믿으면 null
 */
async function readFromBlob(env: string, table: string): Promise<SpecRow[] | null> {
  const snapshot = await db.doc(`envs/${env}/specs/${table}/blob/current`).get();
  if (!snapshot.exists) return null;

  const data = snapshot.data() ?? {};
  const schemaVersion = Number(data.schemaVersion);
  if (schemaVersion !== SCHEMA_VERSION) {
    logger.warn("spec blob schema mismatch", {env, table, schemaVersion, expected: SCHEMA_VERSION});
    return null;
  }

  const payload = data.payload;
  if (typeof payload !== "string" || payload === "") {
    logger.warn("spec blob payload is missing", {env, table});
    return null;
  }

  // 해시·행수 대조는 클라 BattleContentSync 가 하는 검사와 같다. 반쪽 업로드된 표로
  // 보상·덱을 판정하지 않기 위해 서버도 같은 문턱을 넘긴다.
  const expectedHash = String(data.payloadHash ?? "");
  const actualHash = specPayloadHash(payload);
  if (expectedHash !== actualHash) {
    logger.warn("spec blob hash mismatch", {env, table, expectedHash, actualHash});
    return null;
  }

  let rows: SpecRow[];
  try {
    rows = parseSpecPayload(payload);
  } catch (error) {
    logger.warn("spec blob payload is unreadable", {env, table, error});
    return null;
  }

  const rowCount = Number(data.rowCount);
  if (Number.isInteger(rowCount) && rowCount !== rows.length) {
    logger.warn("spec blob row count mismatch", {env, table, rowCount, parsed: rows.length});
    return null;
  }
  return rows;
}

/**
 * 미러(`rows/`)에서 표를 읽는다. 미러가 이번 revision 을 따라오지 못했으면(업로더가 미러를 끄고 올렸다)
 * 읽지 않고 던진다 — 낡은 마스터로 판정하느니 그 callable 이 실패하는 편이 낫다.
 * @param {string} env 환경 id
 * @param {string} table 표 이름
 * @return {Promise<SpecRow[]>} 행 배열
 */
async function readFromRowsMirror(env: string, table: string): Promise<SpecRow[]> {
  // 미러 신선도는 메타에만 있다. 폴백은 드문 경로라 여기서 1건 더 읽는 값을 치를 만하다.
  const meta = await db.doc(`envs/${env}/specs/${table}`).get();
  const revision = Number(meta.data()?.revision ?? -1);
  // rowsRevision 이 없는 문서는 미러를 끌 수 없던 옛 업로더가 쓴 것이다 — 그때는 행 전량이 같은
  // commit 에 실렸으므로 미러가 최신이다. 없다고 막으면 옛 표가 전부 폴백 불가가 된다.
  const rowsRevision = Number(meta.data()?.rowsRevision ?? revision);
  if (!meta.exists || revision !== rowsRevision) {
    logger.error("spec rows mirror is stale", {env, table, revision, rowsRevision});
    throw new Error(
      `spec table ${table} has no usable blob and its rows mirror is stale ` +
      `(revision=${revision}, rowsRevision=${rowsRevision}). Re-upload the table.`);
  }

  const snapshot = await db.collection(`envs/${env}/specs/${table}/rows`).get();
  return snapshot.docs.map((document) => document.data() as SpecRow);
}

/**
 * 표 하나를 통째로 읽는다(블롭 우선 · 캐시 경유). 행은 id 오름차순이고, id 를 못 읽는 행은 버린다
 * — 정렬 비교자가 NaN 을 뱉으면 순서가 미정의가 되어 클라와 다른 카드가 뽑힌다.
 * @param {string} env 환경 id
 * @param {string} table 표 이름
 * @return {Promise<SpecRow[]>} id 오름차순 행
 */
export async function readSpecRows(env: string, table: string): Promise<SpecRow[]> {
  const key = `${env}/${table}`;
  const cached = cache.get(key);
  const now = Date.now();
  if (cached !== undefined && cached.expiresAt > now) return cached.rows;

  let source = "blob";
  let raw = await readFromBlob(env, table);
  if (raw == null) {
    // 폴백은 행 수만큼 과금된다. 로그의 source=rows 가 남아 있으면 그 표를 다시 올려야 한다.
    source = "rows";
    raw = await readFromRowsMirror(env, table);
  }

  const rows = raw
    .filter((row) => Number.isInteger(Number(row.id)))
    .sort((a, b) => Number(a.id) - Number(b.id));

  cache.set(key, {rows, expiresAt: now + CACHE_TTL_MS});
  logger.info("spec table loaded", {env, table, source, rowCount: rows.length});
  return rows;
}

/** 캐시를 비운다. 배포 직후 반영을 앞당기거나 테스트에서 격리할 때 쓴다. */
export function clearSpecCache(): void {
  cache.clear();
}
