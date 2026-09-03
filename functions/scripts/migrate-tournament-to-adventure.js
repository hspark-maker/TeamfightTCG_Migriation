/**
 * 세이브 문서의 `tournament` 슬롯을 `adventure` 로 옮긴다. 1회성 운영 스크립트다.
 *
 * 왜 필요한가 — 모험 도메인 개명(2026-09-03)으로 클라·서버·firestore.rules 가 모두
 * `adventure` 키를 쓰는데, 그 전에 만들어진 문서는 `tournament` 를 들고 있다.
 * 규칙의 최상위 필드 전수 검증(hasOnly + hasAll)은 병합된 최종 문서를 보므로,
 * 옛 키가 남아 있는 한 그 계정의 모든 업로드가 PermissionDenied 로 막힌다
 * (클라가 dirty 슬롯만 보내도 결과는 같다).
 *
 * 사전 준비 — 둘 중 하나:
 *   1) gcloud auth application-default login   (ADC)
 *   2) export GOOGLE_APPLICATION_CREDENTIALS=/path/to/key.json
 *
 * 사용:
 *   node scripts/migrate-tournament-to-adventure.js <env> --dry-run   # 대상만 센다(기본)
 *   node scripts/migrate-tournament-to-adventure.js <env> --apply     # 실제로 옮긴다
 *
 * revision 은 올리지 않는다 — 이 이사는 값의 변경이 아니라 키 이름의 정정이고,
 * revision 을 올리면 그 계정의 클라가 "서버가 내가 모르는 쓰기를 했다"로 읽어 세션을 막는다
 * (PlayerSaveCloud.AdoptServerResult 의 revision == Revision + 1 계약).
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore, FieldValue } = require("firebase-admin/firestore");

const PROJECT_ID = "bm-cardbattle";
const EMPTY_ADVENTURE = { clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: "" };

async function main() {
  const [env, flag] = process.argv.slice(2);
  if (!env) {
    console.error("사용법: node scripts/migrate-tournament-to-adventure.js <env> [--apply|--dry-run]");
    process.exit(1);
  }
  const apply = flag === "--apply";

  initializeApp({ credential: applicationDefault(), projectId: PROJECT_ID });
  const db = getFirestore();

  // 세이브 문서는 envs/{env}/users/{uid}/save/current 하나뿐이다.
  const snapshot = await db.collectionGroup("save").get();

  let scanned = 0;
  let moved = 0;
  let already = 0;
  let skipped = 0;

  for (const doc of snapshot.docs) {
    const path = doc.ref.path;
    if (!path.startsWith(`envs/${env}/users/`) || !path.endsWith("/save/current")) continue;
    scanned++;

    const data = doc.data() || {};
    const hasOld = Object.prototype.hasOwnProperty.call(data, "tournament");
    const hasNew = Object.prototype.hasOwnProperty.call(data, "adventure");

    if (!hasOld && hasNew) { already++; continue; }
    if (!hasOld && !hasNew) {
      // 두 키가 다 없다 — 규칙의 hasAll 을 통과하지 못하는 문서다. 빈 슬롯을 세워 준다.
      if (apply) await doc.ref.update({ adventure: EMPTY_ADVENTURE });
      moved++;
      console.log(`${apply ? "채움" : "채울 것"}: ${path} (두 키 모두 없음)`);
      continue;
    }

    // 옛 키가 있다. 새 키가 이미 있으면 덮어쓰지 않는다 — 어느 쪽이 최신인지 여기서 알 수 없다.
    if (hasNew) {
      skipped++;
      console.warn(`건너뜀(두 키 공존, 수동 확인 필요): ${path}`);
      continue;
    }

    if (apply) {
      await doc.ref.update({
        adventure: data.tournament,
        tournament: FieldValue.delete(),
      });
    }
    moved++;
    console.log(`${apply ? "이사" : "이사할 것"}: ${path}`);
  }

  console.log(
    `\n[${apply ? "적용" : "예행"}] env=${env} 대상 ${scanned}건 · 이사 ${moved} · 이미 완료 ${already} · 건너뜀 ${skipped}`);
  if (!apply) console.log("실제로 옮기려면 --apply 를 붙여 다시 실행할 것.");
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
