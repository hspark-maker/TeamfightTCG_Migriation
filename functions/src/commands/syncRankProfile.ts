import {isDeepStrictEqual} from "node:util";
import {onDocumentWritten} from "firebase-functions/v2/firestore";
import {db, DATABASE_ID} from "../firebaseApp";
import {isKnownEnv} from "../save/environments";
import {publicRankProfile} from "../rank/publicProfile";

export const syncRankProfile = onDocumentWritten({
  document: "envs/{env}/users/{uid}/save/current", database: DATABASE_ID, retry: true,
}, async (event) => {
  const {env, uid} = event.params;
  if (!isKnownEnv(env) || !event.data) return;
  const {before, after} = event.data;
  if (before.exists === after.exists && isDeepStrictEqual(
    publicRankProfile(before.data()?.profile), publicRankProfile(after.data()?.profile))) return;

  const saveRef = db.doc(`envs/${env}/users/${uid}/save/current`);
  const boardRef = db.doc(`envs/${env}/rankings/${uid}`);
  await db.runTransaction(async (transaction) => {
    // 이벤트 재전송·역순 도착에도 최신 프로필을 쓴다. 점수·시즌은 이 경로에서 쓰지 않는다.
    const [save, board] = await transaction.getAll(saveRef, boardRef);
    if (!board.exists) return; // 랭크 최초 생성은 writeRank가 담당한다.
    const profile = publicRankProfile(save.data()?.profile);
    if (!isDeepStrictEqual(board.data()?.profile, profile)) {
      transaction.set(boardRef, {profile}, {mergeFields: ["profile"]});
    }
  });
});
