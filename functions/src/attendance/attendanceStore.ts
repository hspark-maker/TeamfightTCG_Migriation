import {Firestore} from "firebase-admin/firestore";

export function attendanceRef(db: Firestore, env: string, uid: string) {
  return db.doc(`envs/${env}/users/${uid}/attendance/current`);
}
