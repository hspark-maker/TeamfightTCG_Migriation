# 자동 생성 — 손으로 고치지 마라

이 폴더는 `functions/src/` 의 **미러**다. 원본이 진실원이고, 여기 파일은
`npm run sync:shared`(= `prebuild`)가 매번 덮어쓴다.

왜 복사인가: codebase 가 갈리면 TS import 가 넘어가지 않고, Firebase 배포는
각 codebase 의 `source` 디렉터리만 업로드한다. `file:../` npm 의존은 배포 서버에서
해석되지 않는다.

- 재화 코덱을 고칠 일이 생기면 **`functions/src/currency/` 를 고쳐라.**
- 그 다음 `cd functions-currency && npm run sync:shared` 로 미러를 갱신하고 **같이 커밋한다.**
- 갱신을 잊으면 `functions` 의 `npm test` 끝에 물린 `assert-shared-sync` 가 잡는다.
- 미러 파일에 배너 주석을 넣지 않는 것은 의도다 — 바이트가 갈리면 동일성 판정이 무의미해진다.

목록은 `scripts/shared-files.js` 의 `SHARED_FILES` 가 갖는다.
