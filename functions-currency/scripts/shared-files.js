// functions(default) 와 functions-currency 가 함께 쓰는 파일 목록.
//
// codebase 가 갈리면 TS import 가 안 넘어가고, Firebase 배포는 각 codebase 의 source 디렉터리만
// 올린다. 그래서 재화 코덱은 functions/src 한 벌을 원본으로 두고 빌드 때마다 이쪽으로 복사한다.
// 미러(src/generated)는 커밋하지 않는다 - 생성물이라 원본과 갈릴 여지가 없다.
"use strict";

const path = require("path");

/** 원본(functions/src) 기준 상대 경로. 미러는 functions-currency/src/generated 아래 같은 경로다. */
const SHARED_FILES = [
  "currency/currencyKeys.ts",
  "currency/wallet.ts",
  "currency/walletStore.ts",
  "save/saveValues.ts",
];

const ORIGIN_ROOT = path.resolve(__dirname, "..", "..", "functions", "src");
const MIRROR_ROOT = path.resolve(__dirname, "..", "src", "generated");

module.exports = {SHARED_FILES, ORIGIN_ROOT, MIRROR_ROOT};
