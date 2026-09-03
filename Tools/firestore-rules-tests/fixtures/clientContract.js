// 클라이언트 원본에서 계약을 직접 읽어 온다.
//
// 픽스처는 손으로 베낀 사본이라, 원본이 개명돼도 픽스처와 룰이 나란히 낡으면
// 둘끼리는 맞아떨어져서 하네스가 초록불로 남는다. 실제로 그렇게 새어 나갔다 —
// 커밋 0e39a602e 가 슬롯 tournament 를 adventure 로 바꿨는데 룰만 남아,
// 배포 뒤 모든 클라 세이브 update 가 PermissionDenied 로 거부됐다.
// 그래서 키 목록과 스키마 버전만은 베끼지 않고 .cs 에서 뽑아 쓴다.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const REPO_ROOT = fileURLToPath(new URL('../../../', import.meta.url));

const PLAYER_SAVE_DOCUMENT_CS = `${REPO_ROOT}Assets/Scripts/OutGame/Save/4.Cloud/PlayerSaveDocument.cs`;
const USER_SAVE_DATA_CS = `${REPO_ROOT}Assets/Scripts/OutGame/Save/2.Domain/UserSaveData.cs`;

function readSource(_path) {
  try {
    return readFileSync(_path, 'utf8');
  } catch (t_error) {
    throw new Error(
      `클라 원본을 못 읽었다: ${_path}\n` +
      '파일이 옮겨졌다면 이 경로를 고쳐라 — 여기가 못 읽으면 계약 검증이 통째로 사라진다.',
      { cause: t_error },
    );
  }
}

/**
 * PlayerSaveDocument.ToSlotFieldMap 이 싣는 최상위 키(메타 5 + 슬롯 9)를 선언 순서대로 돌려준다.
 * 그 표는 FIELD_* 상수가 전부이므로 상수 선언만 훑으면 충분하다.
 */
export function clientTopLevelKeys() {
  const t_source = readSource(PLAYER_SAVE_DOCUMENT_CS);
  const t_keys = [...t_source.matchAll(/internal const string FIELD_[A-Z0-9_]+\s*=\s*"([^"]+)"\s*;/g)]
    .map((_match) => _match[1]);

  if (t_keys.length === 0) {
    throw new Error(
      `PlayerSaveDocument.cs 에서 FIELD_* 상수를 하나도 못 찾았다: ${PLAYER_SAVE_DOCUMENT_CS}\n` +
      '선언 형태가 바뀌었다면 이 정규식을 같이 고쳐라.',
    );
  }

  return t_keys;
}

/** UserSaveData.VERSION. 픽스처의 SCHEMA_VERSION 이 이 값을 따라야 한다. */
export function clientSchemaVersion() {
  const t_source = readSource(USER_SAVE_DATA_CS);
  const t_match = t_source.match(/public const int VERSION\s*=\s*(\d+)\s*;/);

  if (t_match === null) {
    throw new Error(`UserSaveData.cs 에서 VERSION 상수를 못 찾았다: ${USER_SAVE_DATA_CS}`);
  }

  return Number(t_match[1]);
}

/**
 * 룰의 최상위 키 계약 두 벌(hasOnly · hasAll)을 파싱한다.
 * rank.keys().hasOnly(...) 같은 안쪽 검사는 request.resource.data.keys() 접두사로 걸러진다.
 */
export function rulesTopLevelKeyLists(_rulesText) {
  const t_lists = [...(_rulesText ?? '').matchAll(
    /request\.resource\.data\.keys\(\)\.(hasOnly|hasAll)\(\[([^\]]*)\]\)/g,
  )];

  const t_pick = (_kind) => {
    const t_found = t_lists.find((_match) => _match[1] === _kind);
    if (t_found === undefined) {
      throw new Error(`firestore.rules 에서 request.resource.data.keys().${_kind}([...]) 를 못 찾았다.`);
    }
    return [...t_found[2].matchAll(/'([^']+)'/g)].map((_match) => _match[1]);
  };

  return { hasOnly: t_pick('hasOnly'), hasAll: t_pick('hasAll') };
}

/**
 * 메타 5 — 도메인이 아니라 클라우드 부기다(PlayerSaveDocument.cs 의 같은 이름 주석).
 * 슬롯 목록을 뽑을 때 이만큼을 덜어 낸다.
 */
export const META_KEYS = ['schemaVersion', 'revision', 'updatedAt', 'deviceId', 'appVersion'];

/** 도메인 슬롯 키만. 슬롯이 늘거나 개명되면 여기가 자동으로 따라간다. */
export function clientSlotKeys() {
  const t_keys = clientTopLevelKeys();
  const t_missingMeta = META_KEYS.filter((_key) => !t_keys.includes(_key));

  if (t_missingMeta.length > 0) {
    throw new Error(
      `메타 키가 PlayerSaveDocument.cs 에 없다: ${t_missingMeta.join(', ')}\n` +
      '메타가 개명됐다면 META_KEYS 도 같이 고쳐라.',
    );
  }

  return t_keys.filter((_key) => !META_KEYS.includes(_key));
}
