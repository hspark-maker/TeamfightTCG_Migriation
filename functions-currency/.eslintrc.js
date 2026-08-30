module.exports = {
  root: true,
  env: {
    es6: true,
    node: true,
  },
  extends: [
    "eslint:recommended",
    "plugin:import/errors",
    "plugin:import/warnings",
    "plugin:import/typescript",
    "google",
    "plugin:@typescript-eslint/recommended",
  ],
  parser: "@typescript-eslint/parser",
  parserOptions: {
    project: ["tsconfig.json", "tsconfig.dev.json"],
    sourceType: "module",
  },
  ignorePatterns: [
    "/lib/**/*", // Ignore built files.
    "/generated/**/*", // Ignore generated files.
    "/scripts/**/*", // 배포되지 않는 1회성 운영 스크립트(plain Node, tsconfig 밖).
    "/src/generated/**/*", // functions/src 의 미러. 원본을 고쳐라 - sync:shared 가 덮어쓴다.
  ],
  plugins: [
    "@typescript-eslint",
    "import",
  ],
  rules: {
    // core.autocrlf=true 라 체크아웃마다 CRLF가 된다 - 줄끝은 판정하지 않는다.
    "linebreak-style": 0,
    "quotes": ["error", "double"],
    "import/no-unresolved": 0,
    "indent": ["error", 2],
    "max-len": "off",
    "require-jsdoc": "off",
  },
};
