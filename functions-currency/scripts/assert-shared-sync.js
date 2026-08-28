// 미러가 원본과 같은지 판정한다. functions 의 npm test 끝에도 물려 있다 -
// 원본만 고치고 sync 를 안 돌린 커밋을 거기서 잡는 것이 목적이다.
"use strict";

const fs = require("fs");
const path = require("path");
const {SHARED_FILES, ORIGIN_ROOT, MIRROR_ROOT, normalize} = require("./shared-files");

const drifted = [];
for (const relative of SHARED_FILES) {
  const origin = path.join(ORIGIN_ROOT, relative);
  const mirror = path.join(MIRROR_ROOT, relative);

  if (!fs.existsSync(mirror)) {
    drifted.push(`${relative} - 미러 없음`);
    continue;
  }
  if (normalize(fs.readFileSync(origin, "utf8")) !== normalize(fs.readFileSync(mirror, "utf8"))) {
    drifted.push(`${relative} - 내용 불일치`);
  }
}

if (drifted.length > 0) {
  console.error("assert-shared-sync: 재화 코덱 미러가 원본과 갈렸다.");
  for (const line of drifted) console.error(`  - ${line}`);
  console.error("고침: functions/src 의 원본을 고친 뒤 (cd functions-currency && npm run sync:shared)");
  process.exit(1);
}

console.log(`assert-shared-sync: ok (${SHARED_FILES.length}개 일치)`);
