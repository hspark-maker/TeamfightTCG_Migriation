/**
 * 스펙 배포자 계정에 admin 커스텀 클레임을 부여한다. 1회성 운영 스크립트다.
 *
 * firestore.rules 의 스펙 쓰기 조건이 request.auth.token.admin == true 이고,
 * Unity 에디터의 SpecAdminAuth 가 그 계정으로 로그인해 ID 토큰을 싣는다.
 *
 * 사전 준비 — 둘 중 하나:
 *   1) gcloud auth application-default login   (ADC)
 *   2) 서비스 계정 키를 내려받고
 *      export GOOGLE_APPLICATION_CREDENTIALS=/path/to/key.json
 *      ※ 키 파일은 절대 커밋하지 말 것.
 *
 * 사용:
 *   node scripts/grant-admin.js <email>            # 부여
 *   node scripts/grant-admin.js <email> --revoke   # 회수
 *   node scripts/grant-admin.js <email> --check    # 현재 클레임만 조회
 *
 * 부여 후 해당 계정은 반드시 Unity 에디터에서 다시 로그인해야 한다 —
 * 클레임은 새로 발급되는 ID 토큰에만 박힌다.
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getAuth } = require("firebase-admin/auth");

const PROJECT_ID = "bm-cardbattle";

async function main() {
  const [email, flag] = process.argv.slice(2);
  if (!email) {
    console.error("사용법: node scripts/grant-admin.js <email> [--revoke|--check]");
    process.exit(1);
  }

  initializeApp({ credential: applicationDefault(), projectId: PROJECT_ID });
  const auth = getAuth();

  let user;
  try {
    user = await auth.getUserByEmail(email);
  } catch (e) {
    console.error(`계정을 못 찾았다: ${email}`);
    console.error("Firebase 콘솔 > Authentication 에서 이메일·비밀번호 계정을 먼저 만들 것.");
    console.error(String(e.message || e));
    process.exit(1);
  }

  const current = user.customClaims || {};

  if (flag === "--check") {
    console.log(`${email} (uid=${user.uid}) claims:`, JSON.stringify(current));
    return;
  }

  const revoke = flag === "--revoke";
  const next = { ...current };
  if (revoke) delete next.admin;
  else next.admin = true;

  await auth.setCustomUserClaims(user.uid, next);
  // 기존에 발급된 토큰을 즉시 무효화한다. 회수할 때 특히 중요하다.
  await auth.revokeRefreshTokens(user.uid);

  console.log(`${revoke ? "회수" : "부여"} 완료: ${email} (uid=${user.uid})`);
  console.log("claims:", JSON.stringify(next));
  console.log("Unity 에디터에서 로그아웃 후 다시 로그인해야 새 클레임이 토큰에 실린다.");
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
