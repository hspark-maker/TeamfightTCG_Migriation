// 매치 검증 콜러블들이 공유하는 페이로드 가드. 명령별 파서는 각 commands/ 파일이 갖고,
// 여기에는 여러 명령이 같이 쓰는 것만 둔다.

export const HEX_16 = /^[0-9a-f]{16}$/;
export const HEX_32 = /^[0-9a-f]{32}$/;
export const HEX_64 = /^[0-9a-f]{64}$/;

export function objectRecord(value: unknown): Record<string, unknown> | null {
  if (value == null || typeof value !== "object" || Array.isArray(value)) return null;
  return value as Record<string, unknown>;
}

export function safeInteger(value: unknown): number | null {
  return typeof value === "number" && Number.isSafeInteger(value) ? value : null;
}
