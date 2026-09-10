export interface RankPublicProfile {
  nickname: string;
  avatarId: string;
  frameId: string;
}

// 세이브의 다른 필드가 공개 색인이나 응답으로 새지 않도록 명시한 세 필드만 복사한다.
export function publicRankProfile(raw: unknown): RankPublicProfile {
  const profile = raw as Partial<RankPublicProfile> | null | undefined;
  return {
    nickname: typeof profile?.nickname === "string" ? profile.nickname.slice(0, 12) : "플레이어",
    avatarId: typeof profile?.avatarId === "string" ? profile.avatarId : "",
    frameId: typeof profile?.frameId === "string" ? profile.frameId : "",
  };
}

export function hasRankPublicProfile(raw: unknown): raw is RankPublicProfile {
  const profile = raw as Partial<RankPublicProfile> | null | undefined;
  return typeof profile?.nickname === "string" &&
    typeof profile.avatarId === "string" && typeof profile.frameId === "string";
}
