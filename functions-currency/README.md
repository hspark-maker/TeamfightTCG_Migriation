# functions-currency (재화 codebase)

`envs/{envId}` 아래 재화 문서를 다루는 Firebase Functions codebase. 배포는 `firebase.json` 의
`functions-currency` 항목이 이 디렉터리만 업로드한다.

## `src/generated/` 는 생성물이다 — git 에 없다

체크아웃 직후 `src/generated/` 가 비어 있는 것이 정상이다. **`npm run sync:shared`**
(= `prebuild`, 즉 `npm run build` 가 매번 먼저 돌린다)가 `functions/src/` 에서 복사해 만든다.

- **진실원은 `functions/src/currency/` 와 `functions/src/save/saveValues.ts` 다.**
  재화 코덱을 고칠 일이 생기면 거기를 고쳐라. `src/generated/` 를 손으로 고치면 다음 빌드가 덮어쓴다.
- 복사하는 이유: codebase 가 갈리면 TS import 가 넘어가지 않고, Firebase 배포는 각 codebase 의
  `source` 디렉터리만 올린다. `file:../` npm 의존은 배포 서버에서 해석되지 않는다.
- 복사 목록은 `scripts/shared-files.js` 의 `SHARED_FILES` 가 갖는다.
- 커밋하지 않으므로 원본과 갈릴 여지 자체가 없다 — 미러 동일성을 감시하던 `assert-shared-sync` 는 걷었다.

## 스크립트

| 명령 | 하는 일 |
|---|---|
| `npm run sync:shared` | `functions/src/` 의 공유 파일을 `src/generated/` 로 복사 |
| `npm run build` | `prebuild` 로 sync 한 뒤 `tsc` |
| `npm run lint` | eslint (`src/generated/` 는 ignore) |
