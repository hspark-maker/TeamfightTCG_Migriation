import {FieldPath} from "firebase-admin/firestore";
import {db} from "../firebaseApp";

// Server-owned documents: the existing default-deny rule blocks all direct client access.
export function mailCollection(env: string, uid: string) {
  return db.collection("envs").doc(env).collection("users").doc(uid).collection("mail");
}

export function claimableMail(env: string, uid: string, nowMs: number) {
  return mailCollection(env, uid).where("claimedAtMs", "==", null)
    .where("expiresAtMs", ">", nowMs).orderBy("expiresAtMs").orderBy(FieldPath.documentId());
}
