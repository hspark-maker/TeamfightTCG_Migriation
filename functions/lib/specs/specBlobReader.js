"use strict";
/**
 * 스펙 표를 **블롭 문서 하나**로 읽는다. 표 크기와 무관하게 read 1건이다
 * — `rows/` 미러를 읽던 시절에는 Card 41행 · Reward 85행 · CardPackDrop 322행이 그대로 과금됐다.
 *
 * 원천은 `_index.tables[표].blobPath` 가 가리키는 **불변 릴리스 블롭**이다. 가변 `blob/current` 로
 * 우회하지 않는다 — 우회하면 `_index` 포인터를 옛 버전으로 되돌려도 서버는 최신을 계속 봐서
 * 클라와 서버가 다른 콘텐츠로 판정한다(반쪽 롤백).
 *
 * 예외는 `UNINDEXED_TABLES` 뿐이다. 업로더의 Composition 경로로 올라가는 표는 `_index` 에 실리지
 * 않아 가변 `blob/current` 를 읽는다. 버전 고정 대상이 아니므로 해시가 아니라 짧은 TTL 로 캐시한다.
 *
 * 블롭이 없거나 무결성이 어긋나면 `rows/` 로 우회하지 않고 즉시 실패한다 — 폴백은 표 하나에
 * 행 수만큼 과금되어, 깨진 채로 굴러가면 요금이 조용히 튄다.
 */
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.BATTLE_REPLAY_SPEC_TABLES = void 0;
exports.readSpecPins = readSpecPins;
exports.fingerprintOfSpecPins = fingerprintOfSpecPins;
exports.readPinnedSpecRows = readPinnedSpecRows;
exports.specPayloadHash = specPayloadHash;
exports.parseSpecPayload = parseSpecPayload;
exports.readSpecRows = readSpecRows;
exports.clearSpecCache = clearSpecCache;
const node_crypto_1 = require("node:crypto");
const logger = __importStar(require("firebase-functions/logger"));
const firebaseApp_1 = require("../firebaseApp");
const requestMetrics_1 = require("../observability/requestMetrics");
/** 앱이 해석할 수 있는 콘텐츠 세대. C# ContentVersion.Major 및 앱 버전 첫 자리와 같아야 한다. */
// content-version:major
const CONTENT_MAJOR = 4;
// 새 테이블 세대 배포에서는 실제로 해석 가능한 직전 세대를 함께 넣어 pointer 롤백을 허용한다.
// content-version:supported
const SUPPORTED_CONTENT_MAJORS = new Set([CONTENT_MAJOR]);
exports.BATTLE_REPLAY_SPEC_TABLES = [
    "Card", "SynergyDef", "SynergyTierDef", "SynergyEffectDef",
];
const cache = new Map();
const indexCache = new Map();
const indexLoads = new Map();
const blobLoads = new Map();
let cacheGeneration = 0;
const INDEX_CACHE_TTL_MS = 30 * 1000;
/**
 * `_index.tables` 에 실리지 않는 표. 업로더의 PublishIndex 는 `SpecPayloadCodec.TableNames` 16개만
 * 순회하고, 이 표는 Composition 경로(`SpecFirestoreUploader.Composition.cs`)가 따로 올린다.
 * 인덱스에 넣는 것이 정답이지만 그 전까지는 여기 명시된 표만 가변 블롭을 허용한다 —
 * 목록에 없는 미등재 표는 그대로 실패해야 새 표가 조용히 인덱스를 건너뛰는 것을 잡는다.
 */
// 비어 있다 — 모든 표가 `_index` 로 공개된다. AdventureChapter 는 SO 저작을 따로 올리는 경로가 있어
// 여기 있었지만, 그 경로를 지우고 시트(스펙 표) 하나로 합치면서 다른 표와 같은 규칙으로 돌아왔다.
// 서버와 클라가 같은 발행본을 보게 하는 것이 이 목록을 비워 두는 이유다.
const UNINDEXED_TABLES = new Set();
const UNINDEXED_CACHE_TTL_MS = 30 * 1000;
async function readPublishedSpec(env, table) {
    let cached = indexCache.get(env);
    if (cached === undefined || cached.expiresAt <= Date.now()) {
        cached = await loadOnce(indexLoads, env, () => loadPublishedIndex(env), "specIndex");
    }
    else {
        (0, requestMetrics_1.recordMetric)("specIndexCacheHits");
    }
    const published = cached.tables[table];
    if (published === undefined)
        throw new Error(`published content index has no ${table} entry`);
    return published;
}
async function loadPublishedIndex(env) {
    const generation = cacheGeneration;
    const now = Date.now();
    const snapshot = await (0, requestMetrics_1.measurePhase)("specIndexRead", () => firebaseApp_1.db.doc(`envs/${env}/specs/_index`).get());
    (0, requestMetrics_1.recordMetric)("specIndexReads");
    if (!snapshot.exists)
        throw new Error(`published content index is missing for env ${env}`);
    const data = snapshot.data() ?? {};
    const major = Number(data.major);
    const minor = Number(data.minor);
    if (!SUPPORTED_CONTENT_MAJORS.has(major) || !Number.isInteger(minor) || minor < 0) {
        throw new Error(`published content ${major}.${minor} is incompatible`);
    }
    const rawTables = data.tables;
    if (rawTables == null || typeof rawTables !== "object") {
        throw new Error("published content index has no tables map");
    }
    const tables = {};
    for (const [name, raw] of Object.entries(rawTables)) {
        if (raw == null || typeof raw !== "object") {
            throw new Error(`published content index entry ${name} is invalid`);
        }
        const entry = raw;
        const blobPath = String(entry.blobPath ?? "");
        const payloadHash = String(entry.payloadHash ?? "");
        if (blobPath === "" || payloadHash === "") {
            throw new Error(`published content index entry ${name} has no blobPath or payloadHash`);
        }
        tables[name] = { blobPath, payloadHash };
    }
    const loaded = { expiresAt: now + INDEX_CACHE_TTL_MS, tables };
    if (generation === cacheGeneration)
        indexCache.set(env, loaded);
    return loaded;
}
// 실패한 Promise는 남기지 않는다. clear 뒤 같은 키로 시작한 새 조회도 지우지 않는다.
async function loadOnce(pending, key, load, metric) {
    const existing = pending.get(key);
    if (existing !== undefined) {
        (0, requestMetrics_1.recordMetric)(`${metric}SharedLoads`);
        return (0, requestMetrics_1.measurePhase)(`${metric}SharedWait`, () => existing);
    }
    (0, requestMetrics_1.recordMetric)(`${metric}Loads`);
    const loading = load();
    pending.set(key, loading);
    try {
        return await loading;
    }
    finally {
        if (pending.get(key) === loading)
            pending.delete(key);
    }
}
/**
 * 매치 생성 시점의 불변 릴리스 blob 포인터를 고정한다.
 * 결과 재생은 현재 인덱스가 아니라 이 값을 읽는다.
 * @param {string} env 대상 환경
 * @param {string[]} tables 고정할 표 이름들
 * @return {Promise<SpecPins>} 표 이름 → blob 포인터
 */
async function readSpecPins(env, tables) {
    const entries = await Promise.all(tables.map(async (table) => {
        if (UNINDEXED_TABLES.has(table))
            throw new Error(`unindexed spec cannot be pinned: ${table}`);
        const published = await readPublishedSpec(env, table);
        return [table, { ...published }];
    }));
    return Object.fromEntries(entries);
}
/**
 * 클라이언트 SpecPayloadCodec.CombinedHash와 같은 식.
 * 현재 전투 합의 지문은 Card pin 하나만 접는다.
 * @param {string} env 대상 환경
 * @param {SpecPins} pins 고정된 blob 포인터들
 * @param {string[]} tables 지문에 접을 표 이름들
 * @return {string} 소문자 hex SHA-256
 */
function fingerprintOfSpecPins(env, pins, tables) {
    let source = env;
    for (const table of tables) {
        const pin = pins[table];
        if (pin == null || pin.payloadHash === "")
            throw new Error(`spec pin missing: ${table}`);
        source += `|${table}=${pin.payloadHash}`;
    }
    return (0, node_crypto_1.createHash)("sha256").update(source, "utf8").digest("hex");
}
/**
 * 고정된 매치가 참조하는 불변 blob을 읽는다.
 * 현재 `_index`는 의도적으로 조회하지 않는다.
 * @param {string} env 대상 환경
 * @param {string} table 표 이름
 * @param {SpecPin} pin 매치에 고정된 blob 포인터
 * @return {Promise<SpecRow[]>} id 오름차순 행
 */
async function readPinnedSpecRows(env, table, pin) {
    const expectedPrefix = `envs/${env}/specs/`;
    if (!pin.blobPath.startsWith(expectedPrefix) || pin.payloadHash === "") {
        throw new Error(`invalid spec pin for ${table}`);
    }
    return readCachedBlob(env, table, pin.blobPath, pin.payloadHash, true);
}
/**
 * 블롭 payload 해시. 업로더 `HashOf` 와 같은 규칙 — MD5 앞 8바이트를 hex 로.
 * @param {string} payload 블롭의 payload 필드
 * @return {string} 16자 hex 해시
 */
function specPayloadHash(payload) {
    return (0, node_crypto_1.createHash)("md5").update(payload, "utf8").digest("hex").slice(0, 16);
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
function coerce(text) {
    if (!/^-?\d+$/.test(text))
        return text;
    const parsed = Number(text);
    return Number.isSafeInteger(parsed) ? parsed : text;
}
/**
 * payload 텍스트(`[[열…],[값…],…]`)를 행 객체 배열로 편다.
 * @param {string} payload 블롭의 payload 필드
 * @return {SpecRow[]} 열 이름이 붙은 행. 모양이 어긋나면 예외를 던진다.
 */
function parseSpecPayload(payload) {
    const matrix = JSON.parse(payload);
    if (!Array.isArray(matrix) || matrix.length < 2)
        throw new Error("payload has no rows");
    const columns = matrix[0];
    if (!Array.isArray(columns) || columns.some((column) => typeof column !== "string")) {
        throw new Error("payload header is not a string array");
    }
    const rows = [];
    for (let index = 1; index < matrix.length; index++) {
        const values = matrix[index];
        if (!Array.isArray(values) || values.length !== columns.length) {
            throw new Error(`payload row ${index} has ${Array.isArray(values) ? values.length : "?"} values, expected ${columns.length}`);
        }
        const row = {};
        for (let column = 0; column < columns.length; column++) {
            const value = values[column];
            if (typeof value !== "string")
                throw new Error(`payload row ${index} is not a string array`);
            row[columns[column]] = coerce(value);
        }
        rows.push(row);
    }
    return rows;
}
/**
 * 블롭 문서 하나를 읽어 행으로 편다. 실패는 원인별로 던진다 — 호출부가 한 문장으로 뭉개면
 * callable 에러만 보고는 문서 부재인지 해시 불일치인지 파싱 실패인지 구분할 수 없다.
 * @param {string} env 환경 id
 * @param {string} table 표 이름
 * @param {string} blobPath 읽을 블롭 문서 경로
 * @param {string | null} indexHash `_index` 가 선언한 해시. 비인덱스 표는 null 이라 대조를 건너뛴다
 * @return {Promise<{rows: SpecRow[], payloadHash: string}>} 행 배열과 실제 payload 해시
 */
async function readFromBlob(env, table, blobPath, indexHash) {
    const snapshot = await (0, requestMetrics_1.measurePhase)("specBlobRead", () => firebaseApp_1.db.doc(blobPath).get());
    (0, requestMetrics_1.recordMetric)("specBlobReads");
    if (!snapshot.exists) {
        throw new Error(`spec blob ${table} document is missing at ${blobPath}`);
    }
    const data = snapshot.data() ?? {};
    // major가 없는 기존 blob은 schemaVersion을 호환 필드로 읽는다.
    const contentMajor = Number(data.major ?? data.schemaVersion);
    if (!SUPPORTED_CONTENT_MAJORS.has(contentMajor)) {
        throw new Error(`spec blob ${table} content major ${contentMajor} is not in [${[...SUPPORTED_CONTENT_MAJORS]}]`);
    }
    const payload = data.payload;
    if (typeof payload !== "string" || payload === "") {
        throw new Error(`spec blob ${table} payload is empty`);
    }
    // 해시·행수 대조는 클라 BattleContentSync 가 하는 검사와 같다. 반쪽 업로드된 표로
    // 보상·덱을 판정하지 않기 위해 서버도 같은 문턱을 넘긴다.
    const expectedHash = String(data.payloadHash ?? "");
    const actualHash = specPayloadHash(payload);
    if (expectedHash !== actualHash || (indexHash !== null && indexHash !== actualHash)) {
        throw new Error(`spec blob ${table} hash mismatch (doc=${expectedHash} actual=${actualHash} index=${indexHash ?? "-"})`);
    }
    let rows;
    try {
        rows = parseSpecPayload(payload);
    }
    catch (error) {
        throw new Error(`spec blob ${table} payload is unreadable: ${String(error)}`);
    }
    const rowCount = Number(data.rowCount);
    if (Number.isInteger(rowCount) && rowCount !== rows.length) {
        throw new Error(`spec blob ${table} row count mismatch (meta=${rowCount} parsed=${rows.length})`);
    }
    return { rows, payloadHash: actualHash };
}
/**
 * id 오름차순으로 세운다. id 를 못 읽는 행은 버린다 — 정렬 비교자가 NaN 을 뱉으면 순서가
 * 미정의가 되어 클라와 다른 카드가 뽑힌다.
 * @param {SpecRow[]} raw 파싱된 행
 * @return {SpecRow[]} id 오름차순 행
 */
function sortById(raw) {
    return raw
        .filter((row) => Number.isInteger(Number(row.id)))
        .sort((a, b) => Number(a.id) - Number(b.id));
}
/**
 * 표 하나를 블롭에서 읽고 캐시한다. 인덱스 표는 payloadHash 로, 비인덱스 표는 짧은 TTL 로 판정한다.
 * @param {string} env 환경 id
 * @param {string} table 표 이름
 * @return {Promise<SpecRow[]>} id 오름차순 행
 */
async function readSpecRows(env, table) {
    if (UNINDEXED_TABLES.has(table)) {
        const blobPath = `envs/${env}/specs/${table}/blob/current`;
        return readCachedBlob(env, table, blobPath, null);
    }
    const published = await readPublishedSpec(env, table);
    return readCachedBlob(env, table, published.blobPath, published.payloadHash);
}
async function readCachedBlob(env, table, blobPath, payloadHash, pinned = false) {
    const currentKey = `${env}/${table}`;
    const pinnedKey = `${env}/${table}/${payloadHash}`;
    const key = pinned ? pinnedKey : currentKey;
    // 완료 캐시는 기존처럼 해시로 재사용한다. 발행 때 경로만 바뀐 표를 다시 읽지 않고,
    // 현재 표는 env/table당 한 벌만 보관한다. 이전 버전은 고정 pin 조회에만 보관한다.
    const cached = [cache.get(key), cache.get(pinned ? currentKey : pinnedKey)].find((entry) => entry !== undefined && (payloadHash === null ? entry.expiresAt !== null && entry.expiresAt > Date.now() :
        entry.expiresAt === null && entry.payloadHash === payloadHash));
    if (cached !== undefined) {
        (0, requestMetrics_1.recordMetric)("specBlobCacheHits");
        cache.set(key, cached);
        return cached.rows;
    }
    // 진행 중 요청은 경로·기대 해시까지 같아야 다운로드·검증·정렬을 공유한다.
    const loadKey = JSON.stringify([env, table, blobPath, payloadHash]);
    const generation = cacheGeneration;
    const loaded = await loadOnce(blobLoads, loadKey, async () => {
        const now = Date.now();
        const read = await readFromBlob(env, table, blobPath, payloadHash);
        const rows = sortById(read.rows);
        const entry = {
            payloadHash: read.payloadHash, rows,
            expiresAt: payloadHash === null ? now + UNINDEXED_CACHE_TTL_MS : null,
        };
        if (generation === cacheGeneration)
            cache.set(key, entry);
        logger.info("spec table loaded", {
            env, table, source: payloadHash === null ? "unindexed-blob" : "published-blob", rowCount: rows.length,
        });
        return entry;
    }, "specBlob");
    // 같은 조회를 기다린 current/pin 소비자 각각의 캐시에도 검증된 결과를 채운다.
    if (generation === cacheGeneration)
        cache.set(key, loaded);
    return loaded.rows;
}
/** 캐시를 비운다. 배포 직후 반영을 앞당기거나 테스트에서 격리할 때 쓴다. */
function clearSpecCache() {
    cacheGeneration++;
    cache.clear();
    indexCache.clear();
    indexLoads.clear();
    blobLoads.clear();
}
//# sourceMappingURL=specBlobReader.js.map