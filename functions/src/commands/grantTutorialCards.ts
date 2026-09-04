import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SaveMutation,
} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {readCardPackRow, readDropRows, readSpecRows} from "../packs/packSpecReader";
import {loadCatalogIds} from "../packs/cardCatalog";
import {judgeTutorialGrant} from "../packs/tutorialGrantPack";
import {buildOwnershipSlotFromIds, readOwnedIds} from "../packs/packSlots";
import {grantsRef, readPackGrants, writePackGranted} from "../growth/tutorialGrants";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 * permission-denied 로 나간다(save/domainReject): 튜토리얼 지급 실패로 세션을 끊지 않는다.
 */
type GrantReject = "GrantNotFound" | "GrantNotAllowed";

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {GrantReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason: GrantReject, message: string, context: Record<string, unknown>): never {
  rejectDomain(reason, message, context);
}

/**
 * 튜토리얼 무료 팩 카드 지급. 무엇을 줄지는 CardPack·CardPackDrop 시트가 정하고 클라는 packId 만 보낸다.
 *
 * 팩별 지급 낙인을 grants 문서에 남긴다. 오늘 이 낙인이 막는 것은 없다 — 소유가 단조 집합이고
 * 이 명령은 유니온만 하므로 같은 팩 재청구는 이미 no-op 이다. 남기는 값어치는 둘이다:
 * 장래 무료 팩이 재화·조각처럼 멱등하지 않은 것을 실을 때의 유일한 방어선, 그리고 감사 기록.
 *
 * **재청구는 거절하지 않는다.** 클라는 txId 를 안 보내므로 재시도가 영수증 replay 로 덮이지 않고,
 * 호출이 fire-and-forget 이라 여기서 거절해 봐야 증상은 한참 뒤 덱 게이트에서 소유가 비어 나타난다
 * — 원인과 증상이 갈라지는 그 형태가 무료 한 방 영구 정지 사고와 같은 종류다.
 * 그래서 낙인 히트는 소유 유니온을 그대로 하고 로그에만 alreadyGranted 를 남긴다.
 */
export const grantTutorialCards = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const packId = String(request.data?.packId ?? "");

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (packId === "") {
    throw new HttpsError("invalid-argument", `packId must be a non-empty string, got '${request.data?.packId}'.`);
  }

  const context = {uid, env, packId};

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  const dropTable = await readSpecRows(env, "CardPackDrop");
  const [dropRows, packRow, catalogIds] = await Promise.all([
    readDropRows(env, packId),
    readCardPackRow(env, packId),
    // 카탈로그 대조는 openPack 과 같은 헬퍼를 쓴다 — 규칙이 두 벌이 되면 서버가 서로 다른 카드를 인정한다.
    loadCatalogIds(env),
  ]);

  const verdict = judgeTutorialGrant(packRow, dropRows, catalogIds);
  if (!verdict.ok) {
    if (dropTable.length === 0) {
      // 표를 통째로 못 읽은 것은 그 팩이 미저작인 것과 다르다 — 배포/업로드 사고이고 유저 잘못이 아니다.
      logger.error("CardPackDrop spec is empty or unreadable", {...context, specRowCount: dropTable.length});
    }
    reject(verdict.reason, `Tutorial grant is refused for pack '${packId}'.`, {
      ...context,
      specRowCount: dropTable.length,
      dropRowCount: dropRows.length,
      catalogSize: catalogIds.size,
      price: packRow?.priceAuthored === true ? packRow.price : null,
    });
  }

  const cardIds = verdict.cardIds;

  let granted: number[] = [];
  // 이 팩을 이미 받은 계정인가. 트랜잭션 재실행마다 다시 재므로 콜백 안에서 세운다.
  let alreadyGranted = false;
  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
  let replayed = true;
  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  const result = await mutateSave(env, uid, "grantTutorialCards", {kind: "client", txId},
    async (current, transaction): Promise<SaveMutation> => {
      // 읽기는 반드시 트랜잭션 안이다 — 동시 호출 둘이 같은 "미지급"을 보면 낙인이 어긋난다(enhanceCard 와 같은 규약).
      const grantsReference = grantsRef(db, env, uid);
      alreadyGranted = readPackGrants(await transaction.get(grantsReference)).has(packId);
      if (!alreadyGranted) {
        writePackGranted(transaction, grantsReference, packId, FieldValue.serverTimestamp());
      }

      // 트랜잭션이 재실행되면 차분도 다시 잰다 — 응답의 granted 는 실제로 늘어난 것만 담아야 한다.
      const owned = readOwnedIds(current.ownership);
      const ownedSet = new Set(owned);
      granted = cardIds.filter((cardId) => !ownedSet.has(cardId));

      // 낙인이 있어도 소유 유니온은 조건 없이 한다 — 낙인만 남고 소유가 빈 계정을 스스로 치유한다.
      // 지갑은 건드리지 않는다(claimReward 와 같은 정책) — 빈 지급으로 rev 만 올리면 클라가
      // 달라진 것 없는 잔액을 채택한다.
      return {slots: {ownership: buildOwnershipSlotFromIds(owned, cardIds)}};
    },
    (adopted) => {
      replayed = false;
      return {...adopted, cardIds, granted};
    });

  if (replayed) {
    logger.info("receipt replay",
      {uid, env, source: "grantTutorialCards", txId, revision: result.revision});
  } else {
    logger.info("grantTutorialCards", {
      uid, env, packId,
      // 이 팩을 이미 받은 계정이다 — 소유는 유니온이라 그대로 지나가고, 갈래를 로그로만 가른다.
      alreadyGranted,
      cardIds: cardIds.join(","),
      // 영수증 hit(replay)은 mutate 콜백을 타지 않아 위 granted 가 [] 로 남는다 — 응답과 어긋나지 않게 result 에서 읽는다.
      granted: result.granted.join(","),
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
});
