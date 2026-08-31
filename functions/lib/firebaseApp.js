"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.db = exports.DATABASE_ID = void 0;
const firebase_functions_1 = require("firebase-functions");
const app_1 = require("firebase-admin/app");
const firestore_1 = require("firebase-admin/firestore");
// 모든 명령 모듈이 이 파일을 거친다 — onCall은 import 시점에 평가되므로
// 전역 옵션이 함수 정의보다 반드시 먼저 서야 리전이 먹는다.
(0, firebase_functions_1.setGlobalOptions)({ maxInstances: 10, region: "asia-northeast3" });
/** Firestore 데이터베이스 ID. 클라 FirebaseRootPath.DatabaseId 와 같아야 한다. */
exports.DATABASE_ID = "cardbattle";
const app = (0, app_1.initializeApp)();
/** 명명 DB 핸들. Admin SDK라 Security Rules를 우회한다. */
exports.db = (0, firestore_1.getFirestore)(app, exports.DATABASE_ID);
//# sourceMappingURL=firebaseApp.js.map