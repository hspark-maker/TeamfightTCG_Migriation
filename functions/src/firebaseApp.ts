import {setGlobalOptions} from "firebase-functions";
import {initializeApp} from "firebase-admin/app";
import {getFirestore} from "firebase-admin/firestore";

// 모든 명령 모듈이 이 파일을 거친다 — onCall은 import 시점에 평가되므로
// 전역 옵션이 함수 정의보다 반드시 먼저 서야 리전이 먹는다.
setGlobalOptions({maxInstances: 10, region: "asia-northeast3"});

/** Firestore 데이터베이스 ID. 클라 FirebaseRootPath.DatabaseId 와 같아야 한다. */
export const DATABASE_ID = "cardbattle";

const app = initializeApp();

/** 명명 DB 핸들. Admin SDK라 Security Rules를 우회한다. */
export const db = getFirestore(app, DATABASE_ID);
