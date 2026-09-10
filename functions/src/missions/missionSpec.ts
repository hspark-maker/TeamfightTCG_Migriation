import {readSpecRows} from "../specs/specBlobReader";
import {MissionDef, parseMissionCatalog} from "./catalog";

/**
 * 다른 스펙과 같은 _index 포인터·캐시를 사용한다. 트랜잭션 밖에서 한 번 읽어 판정에 넘긴다.
 * @param {string} env 대상 환경
 * @return {Promise<Array>} 발행된 미션 정의
 */
export async function readMissionCatalog(env: string): Promise<readonly MissionDef[]> {
  return parseMissionCatalog(await readSpecRows(env, "Mission"));
}
