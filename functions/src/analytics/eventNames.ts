export const EVENTS = {
  accountCreated: {name: "account.created"},
  adventureNodeCompleted: {name: "adventure.node_completed"},
  battleCompleted: {name: "battle.completed", missionKey: "CompleteBattle"},
  battleCardsDestroyed: {name: "battle.cards_destroyed", missionKey: "DestroyCards"},
  battleAttacksPerformed: {name: "battle.attacks_performed", missionKey: "AttackTimes"},
  battleKeywordsTriggered: {name: "battle.keywords_triggered", missionKey: "TriggerKeyword"},
  battleSynergiesTriggered: {name: "battle.synergies_triggered", missionKey: "TriggerSynergy"},
  rankedBattleWon: {name: "battle.ranked_won", missionKey: "WinRankedBattle"},
  battleRewardClaimed: {name: "battle.reward_claimed"},
  cardEnhanceResolved: {name: "card.enhance_resolved", missionKey: "EnhanceCard"},
  cardLimitBreakCompleted: {name: "card.limit_break_completed", missionKey: "LimitBreakCard"},
  keywordEnhanced: {name: "keyword.enhanced"},
  matchCreated: {name: "match.created"},
  matchDeckLocked: {name: "match.deck_locked"},
  missionClaimed: {name: "mission.claimed"},
  packOpened: {name: "pack.opened", missionKey: "OpenPack"},
  passRewardClaimed: {name: "pass.reward_claimed"},
  rewardClaimed: {name: "reward.claimed", missionKey: "ClaimReward"},
  rouletteSpun: {name: "roulette.spun"},
  tutorialCardsGranted: {name: "tutorial.cards_granted"},
} as const;

export type AnalyticsEventName = (typeof EVENTS)[keyof typeof EVENTS]["name"];
