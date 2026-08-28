// functions(default) 와 functions-currency 가 함께 쓰는 파일 목록.
//
// codebase 가 갈리면 TS import 가 안 넘어가고, Firebase 배포는 각 codebase 의 source 디렉터리만
// 올린다. 그래서 재화 코덱은 한 벌을 원본으로 두고 이쪽에 미러를 둔다.
// 원본은 functions/src/ 다 - 미러를 고치면 sync 가 덮어쓰고 assert 가 커밋을 막는다.
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

/** 줄끝을 정규화한다. core.autocrlf=true 라 체크아웃마다 CRLF/LF 가 갈려 바이트 비교가 못 선다. */
function normalize(text) {
  return text.replace(/\r\n/g, "\n");
}

module.exports = {SHARED_FILES, ORIGIN_ROOT, MIRROR_ROOT, normalize};
