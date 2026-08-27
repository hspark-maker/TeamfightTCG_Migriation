import {setGlobalOptions} from "firebase-functions";
import {onRequest} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {initializeApp} from "firebase-admin/app";
import {getFirestore} from "firebase-admin/firestore";

setGlobalOptions({maxInstances: 10, region: "asia-northeast3"});

const app = initializeApp();

/** Firestore 데이터베이스 ID. 클라이언트 FirebaseRootPath.DatabaseId 와 같아야 한다. */
const DATABASE_ID = "cardbattle";

export const ping = onRequest(async (request, response) => {
  logger.info("ping called");
  const db = getFirestore(app, DATABASE_ID);
  let dbOk = false;
  let dbError: string | null = null;
  try {
    await db.collection("_health").doc("ping").get();
    dbOk = true;
  } catch (e) {
    dbError = e instanceof Error ? e.message : String(e);
  }
  response.json({ok: true, database: DATABASE_ID, dbOk, dbError});
});
