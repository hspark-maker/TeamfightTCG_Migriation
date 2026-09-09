import csv
import io
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = ROOT / 'docs' / 'SpecData'
OUT = Path(__file__).parent


def read_table(name):
    path = SPEC / (name + '_sheet.csv')
    with path.open(encoding='utf-8-sig', newline='') as f:
        rows = list(csv.reader(f))
    types = {'int', 'long', 'float', 'double', 'bool', 'string'}
    ti = next(i for i in (1, 2) if all(not c or c in types for c in rows[i]))
    fields = rows[ti - 1]
    data = [dict(zip(fields, r)) for r in rows[ti + 1:] if any(r)]
    return rows[:ti + 1], fields, data


def write_table(name, header, fields, data):
    s = io.StringIO(newline='')
    w = csv.writer(s, lineterminator='\n')
    w.writerows(header)
    w.writerows([[r.get(k, '') for k in fields] for r in data])
    (SPEC / (name + '_sheet.csv')).write_text(s.getvalue(), encoding='utf-8', newline='')


header, fields, old = read_table('Reward')
snapshot = OUT / 'reward_before.json'
if not snapshot.exists():
    snapshot.write_text(json.dumps(old, ensure_ascii=False, indent=2), encoding='utf-8')

def key(r):
    return tuple(r[k] for k in ('ownerType', 'ownerId', 'rewardType', 'rewardId'))

old_keys = {key(r): r for r in old}
assert len(old_keys) == len(old)
next_id = max(int(r['id']) for r in old) + 1
desired = []


def reward(owner, owner_id, kind, item, amount, memo=''):
    global next_id
    order = 1 + sum(r['ownerType'] == owner and r['ownerId'] == owner_id for r in desired)
    row = dict(ownerType=owner, ownerId=owner_id, order=str(order), rewardType=kind,
               rewardId=item, amount=str(amount), **{'#memo': memo})
    previous = old_keys.get(key(row))
    row['id'] = previous['id'] if previous else str(next_id)
    if not previous:
        next_id += 1
    desired.append(row)


def currency(owner, owner_id, item, amount, memo=''):
    reward(owner, owner_id, 'Currency', item, amount, memo)


# Existing unrelated Album and Battle rewards are preserved byte-for-value.
for row in old:
    if row['ownerType'] not in {'Adventure', 'Rank', 'Mission', 'Pass', 'Guide', 'CardDuplicate'}:
        desired.append(row.copy())

gold = [[110, 110, 110], [140, 150, 150], [180, 180, 190], [220, 220, 220]]
shards = [[30, 30, 30, 60, 60, 30], [40, 40, 40, 70, 70, 40],
          [50, 50, 50, 80, 80, 50], [60, 60, 60, 90, 90, 60]]
gems = [10, 15, 20, 30]
energy = [20, 30, 40, 50]
packs = [('NormalPack_TEST', 1), ('SpecialPack', 1), ('GiantsGardenPack', 1), ('HuntingBrandPack', 2)]
for ci in range(4):
    for ni in range(6):
        node = f'node_{ci * 6 + ni + 1:02d}'
        if ni in (0, 3):
            currency('Adventure', node, 'Gold', gold[ci][0 if ni == 0 else 1])
        if ni == 2:
            currency('Adventure', node, 'Energy', energy[ci])
        currency('Adventure', node, 'Shard', shards[ci][ni])
    chapter = f'chapter_{ci + 1:02d}'
    currency('Adventure', chapter, 'Gold', gold[ci][2])
    currency('Adventure', chapter, 'Diamond', gems[ci])
    reward('Adventure', chapter, 'Pack', *packs[ci], '팩 지급 구현 대기')

rank_gold = [80, 120, 180, 250, 350]
rank_gems = [5, 10, 15, 20, 30]
rank_packs = [('NormalPack_TEST', 1), ('SpecialPack', 1), ('GiantsGardenPack', 1),
              ('HuntingBrandPack', 2), ('ImmortalLegacyPack', 3)]
rank_names = ['브론즈', '실버', '골드', '플래티넘', '다이아몬드']
for grade in range(5):
    for division in range(4):
        tier = str(grade * 4 + division)
        memo = f'{rank_names[grade]} {division + 1}'
        currency('Rank', tier, 'Gold', rank_gold[grade], memo)
        if division == 3:
            currency('Rank', tier, 'Diamond', rank_gems[grade], memo + ' 완주')
            reward('Rank', tier, 'Pack', *rank_packs[grade], memo + ' 팩 지급 구현 대기')

mission_rewards = {
    'daily.completeBattle3': [('Currency', 'Shard', 40)],
    'daily.completeBattle5': [('Currency', 'Gold', 100), ('Currency', 'RouletteTicket', 1)],
    'daily.destroyCards3': [('Currency', 'Gold', 80)],
    'daily.triggerKeyword2': [('Currency', 'Energy', 10)],
    'daily.triggerSynergy2': [('Currency', 'Shard', 20)],
    'daily.openPack1': [('Currency', 'Gold', 50)],
    'weekly.completeBattle20': [('Currency', 'Shard', 120)],
    'weekly.completeBattle35': [('Currency', 'Shard', 60), ('Currency', 'Gold', 300)],
    'weekly.openPack10': [('Pack', 'NormalPack_TEST', 1)],
    'weekly.enhanceCard10': [('Currency', 'Gold', 200), ('Currency', 'Energy', 30)],
    'weekly.winBattle10': [('Currency', 'Diamond', 20), ('Currency', 'RouletteTicket', 3)],
}
for owner_id, gains in mission_rewards.items():
    for kind, item, amount in gains:
        note = '미션 catalog/모험 집계 동기화 대기'
        if kind == 'Pack':
            note += '; 팩 지급 구현 대기'
        reward('Mission', owner_id, kind, item, amount, note)

pass_rewards = [
    [('Currency', 'Gold', 200)],
    [('Currency', 'Shard', 60)],
    [('Pack', 'NormalPack_TEST', 1), ('Currency', 'RouletteTicket', 2)],
    [('Currency', 'Diamond', 10), ('Currency', 'Energy', 20)],
    [('Currency', 'Shard', 80), ('Pack', 'SpecialPack', 1)],
    [('Currency', 'Gold', 300), ('Currency', 'RouletteTicket', 2)],
    [('Currency', 'Shard', 100)],
    [('Currency', 'Diamond', 20), ('Currency', 'Energy', 30)],
    [('Currency', 'Shard', 120), ('Pack', 'NormalPack_TEST', 2)],
    [('Currency', 'Diamond', 50), ('PackChoice', 'UnlockedThemePack', 1)],
]
for level, gains in enumerate(pass_rewards, 1):
    for kind, item, amount in gains:
        note = f'시즌 1 무료 트랙 레벨 {level}'
        if kind != 'Currency':
            note += '; 팩 지급 구현 대기'
        reward('Pass', f'S1:{level}', kind, item, amount, note)

guide_shards = [10, 10, 40, 0, 30, 30, 90, 0, 10, 20, 60, 0, 0]
guide_extra = {
    4: [('Currency', 'Gold', 50)], 5: [('Card', '9', 1)],
    8: [('Currency', 'Gold', 60)], 12: [('Pack', 'NormalPack_TEST', 1)],
    13: [('Currency', 'Diamond', 10), ('Currency', 'RouletteTicket', 1)],
}
for i, amount in enumerate(guide_shards, 1):
    if amount:
        currency('Guide', f'guide.{i:02d}', 'Shard', amount, '계정당 1회 가이드 지급 구현 대기')
    for kind, item, n in guide_extra.get(i, []):
        reward('Guide', f'guide.{i:02d}', kind, item, n, '계정당 1회 가이드 지급 구현 대기')

for grade, amount in [('Common', 1), ('Rare', 2), ('Arcane', 5), ('Mythic', 10)]:
    currency('CardDuplicate', grade, 'Shard', amount,
             '중복 추가 조각 구현 대기; 기존 간식1 유지; 0원 튜토리얼팩 제외')

ids = [int(r['id']) for r in desired]
assert len(ids) == len(set(ids))
assert len({key(r) for r in desired}) == len(desired)
desired.sort(key=lambda r: int(r['id']))
write_table('Reward', header, fields, desired)

# Keep the legacy currency-only adventure sheet consistent, without adding a schema.
lh, lf, legacy = read_table('AdventureReward')
old_legacy = {(r['ownerKey'], r['currency']): r for r in legacy}
legacy_next = max(int(r['id']) for r in legacy) + 1
legacy_desired = []
for r in desired:
    if r['ownerType'] != 'Adventure' or r['rewardType'] != 'Currency':
        continue
    previous = old_legacy.get((r['ownerId'], r['rewardId']))
    new = {k: '' for k in lf}
    new.update(id=previous['id'] if previous else str(legacy_next), ownerKey=r['ownerId'],
               order=r['order'], currency=r['rewardId'], amount=r['amount'])
    if previous:
        for k in lf:
            if k not in {'id', 'ownerKey', 'order', 'currency', 'amount'}:
                new[k] = previous[k]
    else:
        legacy_next += 1
    legacy_desired.append(new)
legacy_desired.sort(key=lambda r: int(r['id']))
write_table('AdventureReward', lh, lf, legacy_desired)

print(json.dumps({'Reward': len(desired), 'AdventureReward': len(legacy_desired),
                  'retiredRewardIds': sorted(set(int(r['id']) for r in old) - set(ids)),
                  'newRewardIds': sorted(set(ids) - set(int(r['id']) for r in old))}, ensure_ascii=False))
