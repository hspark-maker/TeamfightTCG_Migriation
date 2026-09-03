"use strict";
/**
 * 이 env 에서 노출되는 카드 id 집합. 클라 CardCatalog.SetSource + CardSpec.Load 의 재현이다.
 *
 * **env 가 곧 런모드다** — ContentProfileConfig.CloudEnvId 가
 * `runMode == Test ? "test" : "live"` 라 둘 사이에 다른 축이 없다:
 *
 * 카드 표는 `Card` 하나다(Card_Test 표는 폐기) — env 로 갈리는 것은 채널 필터뿐이다:
 *
 * | env    | 카드 표 | 채널 필터                        |
 * |--------|---------|----------------------------------|
 * | live   | `Card`  | `channel == "Live"` 만           |
 * | test   | `Card`  | 없음(Test 프로필 includeTestCards=1) |
 *
 * ⚠ includeTestCards 는 런모드에서 파생되는 값이 아니라 프로필 에셋의 저작값이다
 * (Live.asset=0 · Test.asset=1). 저 저작이 바뀌면 이 표도 같이 고쳐야 서버와 클라의 풀이 안 갈린다.
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.loadCatalogIds = loadCatalogIds;
const packSpecReader_1 = require("./packSpecReader");
/** 클라 ECardChannel.Live 의 시트 표기. */
const LIVE_CHANNEL = "Live";
/**
 * 카탈로그 id 집합. 클라 CardCatalog.Contains 가 답하는 것과 같은 집합이어야 한다
 * — 여기서 갈리면 뽑히면 안 될 카드가 나오거나 뽑은 카드가 조용히 버려진다.
 * @param {string} env 환경 id
 * @return {Promise<Set<number>>} 노출 카드 id
 */
async function loadCatalogIds(env) {
    const isTest = env === "test";
    const rows = await (0, packSpecReader_1.readSpecRows)(env, "Card");
    const ids = new Set();
    for (const row of rows) {
        const id = Number(row.id);
        if (!Number.isInteger(id) || id <= 0)
            continue;
        if (!isTest && String(row.channel ?? "") !== LIVE_CHANNEL)
            continue;
        ids.add(id);
    }
    return ids;
}
//# sourceMappingURL=cardCatalog.js.map