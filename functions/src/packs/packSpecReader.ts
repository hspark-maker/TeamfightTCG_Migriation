/**
 * 카드팩 판정에 필요한 스펙 표 읽기. `envs/{env}/specs/{table}/rows` 가 원천이다.
 *
 * 팩 1회 개봉에 표 4개(CardPack · CardPackDrop · Card|Card_Test · RankGrade)를 봐야 하므로
 * env·표 단위로 짧게 캐시한다. 스펙은 릴리즈 때만 바뀌고, 캐시가 없으면 호출 비용이 그대로 곱해진다.
 *
 * **orderBy 를 쓰지 않는다.** 이 프로젝트에는 firestore.indexes.json 이 없어 복합 인덱스를 만들 수 없다
 * — 정렬은 메모리에서 id 를 **숫자로** 비교해서 한다(문자열 정렬은 "10" 이 "2" 앞에 서서 풀 순서를 바꾼다).
 */

import * as logger from "firebase-functions/logger";
import {db} from "../firebaseApp";
import {DropRow} from "./packDraw";
import {RankGradeRow} from "./rankGrade";

/** 캐시 수명. 스펙 업로드가 반영되기까지 최대 이만큼 늦는다. */
const CACHE_TTL_MS = 5 * 60 * 1000;

/** 표 한 행의 날 것. 업로더가 만든 필드가 그대로 들어 있다. */
export type SpecRow = Record<string, unknown>;

interface CacheEntry {
  rows: SpecRow[];
  expiresAt: number;
}

const cache = new Map<string, CacheEntry>();

/**
 * 표 하나를 통째로 읽는다(캐시 경유). 행은 id 오름차순이고, id 를 못 읽는 행은 버린다
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

  const snapshot = await db.collection(`envs/${env}/specs/${table}/rows`).get();
  const rows = snapshot.docs
    .map((document) => document.data() as SpecRow)
    .filter((row) => Number.isInteger(Number(row.id)))
    .sort((a, b) => Number(a.id) - Number(b.id));

  cache.set(key, {rows, expiresAt: now + CACHE_TTL_MS});
  logger.info("spec table loaded", {env, table, rowCount: rows.length});
  return rows;
}

/** 캐시를 비운다. 배포 직후 반영을 앞당기거나 테스트에서 격리할 때 쓴다. */
export function clearSpecCache(): void {
  cache.clear();
}

/** CardPack 표의 한 행. 클라 CardPackData 가 시트 우선으로 읽는 값과 같은 열이다. */
export interface CardPackRow {
  packId: string;
  priceType: string;
  price: number;
  drawCount: number;
  uniqueDraw: boolean;
  refundType: string;
  refundAmount: number;
  minRankGrade: string;
}

/** 클라 ECurrencyType 의 이름. CurrencyCode.TryParse 가 통과시키는 값이 이 넷뿐이다. */
export const CURRENCY_KEYS = ["Gold", "Diamond", "Energy", "Shard"] as const;

/**
 * 재화 이름을 정규화한다. 클라 CardPackData.ParseCurrency 재현이다
 * — 대소문자를 안 가리고, 못 읽으면 Gold 로 떨어진다(팩 가격은 오타여도 화면이 서야 한다).
 * @param {string} value priceType·refundType 열 값
 * @return {string} 재화 키
 */
export function parseCurrency(value: string): string {
  const lowered = value.trim().toLowerCase();
  const key = (CURRENCY_KEYS as readonly string[]).find((k) => k.toLowerCase() === lowered);
  return key ?? "Gold";
}

/**
 * 팩 행 하나. 시트에 없으면 null — 클라는 이때 SO 인스펙터 값으로 폴백하지만 서버는 SO 를 못 본다.
 * @param {string} env 환경 id
 * @param {string} packId CardPack.packId
 * @return {Promise<CardPackRow | null>} 팩 행
 */
export async function readCardPackRow(env: string, packId: string): Promise<CardPackRow | null> {
  const rows = await readSpecRows(env, "CardPack");
  const row = rows.find((r) => String(r.packId ?? "") === packId);
  if (row === undefined) return null;

  return {
    packId,
    priceType: parseCurrency(String(row.priceType ?? "")),
    price: Number(row.price ?? 0),
    // 클라 CardPackData.DrawCount 가 Mathf.Max(1, …) 로 조인다.
    drawCount: Math.max(1, Number(row.drawCount ?? 0)),
    uniqueDraw: Number(row.uniqueDraw ?? 0) !== 0,
    refundType: parseCurrency(String(row.refundType ?? "")),
    refundAmount: Number(row.refundAmount ?? 0),
    minRankGrade: String(row.minRankGrade ?? ""),
  };
}

/**
 * 이 팩의 드롭 행. 표를 통째로 읽어 캐시한 뒤 메모리에서 거른다
 * — packId 마다 where 질의를 던지면 캐시가 팩 수만큼 갈라진다.
 * @param {string} env 환경 id
 * @param {string} packId CardPack.packId
 * @return {Promise<DropRow[]>} id 오름차순 드롭 행
 */
export async function readDropRows(env: string, packId: string): Promise<DropRow[]> {
  const rows = await readSpecRows(env, "CardPackDrop");
  return rows
    .filter((row) => String(row.packId ?? "") === packId)
    .map((row) => ({
      id: Number(row.id),
      packId,
      minGrade: String(row.minGrade ?? ""),
      cardId: Number(row.cardId ?? 0),
      weight: Number(row.weight ?? 0),
    }));
}

/**
 * RankGrade 행. 클라 RankConfig 의 grades 리스트에 대응한다.
 * @param {string} env 환경 id
 * @return {Promise<RankGradeRow[]>} id 오름차순 등급 행
 */
export async function readRankGradeRows(env: string): Promise<RankGradeRow[]> {
  const rows = await readSpecRows(env, "RankGrade");
  return rows.map((row) => ({
    id: Number(row.id),
    gradeKey: String(row.gradeKey ?? ""),
    entryPoints: Number(row.entryPoints ?? Number.NaN),
  }));
}
