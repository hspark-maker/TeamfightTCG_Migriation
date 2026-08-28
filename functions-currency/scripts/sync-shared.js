// 원본(functions/src) 을 미러(functions-currency/src/generated) 로 복사한다.
// prebuild 에서 자동으로 돈다. 미러는 커밋하지 않으므로 이 복사가 빌드를 성립시키는 유일한 장치다.
"use strict";

const fs = require("fs");
const path = require("path");
const {SHARED_FILES, ORIGIN_ROOT, MIRROR_ROOT} = require("./shared-files");

let copied = 0;
for (const relative of SHARED_FILES) {
  const origin = path.join(ORIGIN_ROOT, relative);
  const mirror = path.join(MIRROR_ROOT, relative);

  if (!fs.existsSync(origin)) {
    console.error(`sync-shared: 원본이 없다 - ${origin}`);
    process.exit(1);
  }

  fs.mkdirSync(path.dirname(mirror), {recursive: true});
  fs.copyFileSync(origin, mirror);
  copied += 1;
}

console.log(`sync-shared: ${copied}개 파일 미러 갱신`);
