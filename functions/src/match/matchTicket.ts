import {createHmac, timingSafeEqual} from "crypto";

/**
 * 매칭 티어 티켓. 서버가 서명하고 서버만 검증한다 —
 * 클라는 티켓을 나르기만 하므로 티어를 지어낼 수 없다.
 *
 * 한계: 이 서명은 "서버가 이 uid 에게 이 티어를 발급했다"만 증명한다.
 * 티켓을 어디서 얻었는지는 증명하지 못하므로, 남의 티켓을 주워 자기 것처럼 내미는 재사용은
 * 만료(TTL) 안에서 가능하다. 그걸 막으려면 검증 시 1회 소비 기록이 필요하다(별건).
 */
export type MatchTicketPayload = {
  uid: string;
  tier: number;
  env: string;
  exp: number; // epoch seconds
};

export const TICKET_TTL_SECONDS = 120;

export function signMatchTicket(payload: MatchTicketPayload, secret: string): string {
  const body = encodeBase64Url(Buffer.from(JSON.stringify(payload), "utf8"));
  return `${body}.${sign(body, secret)}`;
}

export type MatchTicketVerdict =
  | {ok: true; payload: MatchTicketPayload}
  | {ok: false; reason: string};

export function verifyMatchTicket(
  ticket: string, secret: string, nowSeconds: number): MatchTicketVerdict {
  const dot = ticket.indexOf(".");
  if (dot <= 0 || dot === ticket.length - 1) return {ok: false, reason: "malformed"};

  const body = ticket.slice(0, dot);
  const signature = ticket.slice(dot + 1);
  if (!equalsConstantTime(signature, sign(body, secret))) return {ok: false, reason: "signature"};

  let payload: MatchTicketPayload;
  try {
    payload = JSON.parse(decodeBase64Url(body).toString("utf8")) as MatchTicketPayload;
  } catch {
    return {ok: false, reason: "payload"};
  }

  if (typeof payload.uid !== "string" || payload.uid === "") return {ok: false, reason: "uid"};
  if (!Number.isSafeInteger(payload.tier) || payload.tier < 0) return {ok: false, reason: "tier"};
  if (!Number.isSafeInteger(payload.exp) || payload.exp <= nowSeconds) return {ok: false, reason: "expired"};

  return {ok: true, payload};
}

function sign(body: string, secret: string): string {
  return encodeBase64Url(createHmac("sha256", secret).update(body).digest());
}

function equalsConstantTime(a: string, b: string): boolean {
  const left = Buffer.from(a, "utf8");
  const right = Buffer.from(b, "utf8");
  if (left.length !== right.length) return false;
  return timingSafeEqual(left, right);
}

function encodeBase64Url(buffer: Buffer): string {
  return buffer.toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function decodeBase64Url(value: string): Buffer {
  return Buffer.from(value.replace(/-/g, "+").replace(/_/g, "/"), "base64");
}
