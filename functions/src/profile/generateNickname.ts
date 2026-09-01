import {randomInt} from "crypto";
import {NICKNAME_MAX_LENGTH, NICKNAME_MODIFIERS, NICKNAME_NOUNS} from "./nicknameWords";

/** 0 이상 max 미만의 정수를 내는 추첨기. 테스트가 고정값을 주입할 수 있게 뚫어 둔 축이다. */
export type NicknameRollFn = (max: number) => number;

/** 길이 상한에 걸렸을 때 다시 뽑는 횟수. 표에 긴 낱말이 늘어도 여기서 흡수된다. */
const MAX_ATTEMPTS = 12;

/**
 * 신규 계정 기본 닉네임을 "수식어 명사" 로 조합한다. 계정 문서를 만드는 그 자리에서 한 번만
 * 부르고 결과를 문서에 굳힌다 — 클라는 뽑지 않고 문서 값을 그대로 읽는다.
 *
 * 상한을 넘는 조합은 자르지 않고 버린다(자르면 "빛나는 대장장"처럼 낱말이 깨진다).
 * 재추첨이 다 실패하면 명사 하나로 떨어뜨리고, 그것도 길면 상한에서 자른다.
 * @param {NicknameRollFn} roll 추첨기(기본: crypto.randomInt)
 * @return {string} 1..NICKNAME_MAX_LENGTH 자 닉네임
 */
export function generateNickname(roll: NicknameRollFn = (max) => randomInt(max)): string {
  for (let i = 0; i < MAX_ATTEMPTS; i++) {
    const modifier = NICKNAME_MODIFIERS[roll(NICKNAME_MODIFIERS.length)];
    const noun = NICKNAME_NOUNS[roll(NICKNAME_NOUNS.length)];
    const name = `${modifier} ${noun}`;
    if (name.length <= NICKNAME_MAX_LENGTH) return name;
  }

  const noun = NICKNAME_NOUNS[roll(NICKNAME_NOUNS.length)];
  return noun.length <= NICKNAME_MAX_LENGTH ? noun : noun.slice(0, NICKNAME_MAX_LENGTH);
}
