import csv
import json
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = ROOT / 'docs/SpecData'


def read(name):
    with (SPEC / f'{name}_sheet.csv').open(encoding='utf-8-sig', newline='') as f:
        rows = list(csv.reader(f))
    types = {'int', 'long', 'float', 'double', 'bool', 'string'}
    ti = next(i for i in (1, 2) if all(not x or x in types for x in rows[i]))
    headers = rows[ti - 1]
    assert len(headers) == len(rows[ti]), name
    data = []
    for line, cells in enumerate(rows[ti + 1:], ti + 2):
        if not any(cells):
            continue
        assert len(cells) == len(headers), (name, line, len(cells), len(headers))
        for value, kind in zip(cells, rows[ti]):
            if kind in ('int', 'long'):
                int(value)
        data.append(dict(zip(headers, cells)))
    assert len(data) == len({r['id'] for r in data}), name
    return data


reward = read('Reward')
missions = read('Mission')
roulette = read('RouletteSlot')
pass_levels = read('PassLevel')
enhance = read('CardEnhance')
legacy = read('AdventureReward')
pack_ids = {r['packId'] for r in read('CardPack')}
card_ids = {r['id'] for r in read('Card')}
assert [(int(r['level']), int(r['cost']), int(r['successPermille'])) for r in enhance] == [(2,10,1000),(3,20,1000),(4,150,1000)]
assert all(int(r['requiredExp']) == int(r['level']) * 300 for r in pass_levels)
assert len(pass_levels) == 10
assert len({r['missionId'] for r in missions}) == len(missions)
for period, count, exp in [('daily',6,100),('weekly',5,160),('guide',13,0)]:
    active = [r for r in missions if r['period'] == period and r['enabled'] == '1']
    assert len(active) == count, (period, 'count', len(active))
    assert sum(int(r['passExp']) for r in active) == exp, (period, 'exp')

orders = [(r['ownerType'],r['ownerId'],r['order']) for r in reward]
assert len(orders) == len(set(orders)), 'duplicate reward order'
assert all(int(r['amount']) > 0 for r in reward)
mission_ids = {r['missionId'] for r in missions if r['enabled'] == '1'}
for r in reward:
    if r['ownerType'] in ('Mission','Guide'):
        assert r['ownerId'] in mission_ids, r
    if r['rewardType'] == 'Pack':
        assert r['rewardId'] in pack_ids, r
    if r['rewardType'] == 'Card':
        assert r['rewardId'] in card_ids, r

def totals(owner, prefix=''):
    result = Counter()
    for r in reward:
        if r['ownerType'] == owner and r['ownerId'].startswith(prefix):
            result[(r['rewardType'], r['rewardId'])] += int(r['amount'])
    return dict(result)

def check_currency(owner, expected, prefix=''):
    sums = totals(owner, prefix)
    actual = {k[1]: v for k,v in sums.items() if k[0] == 'Currency'}
    assert actual == expected, (owner, prefix, actual, expected)

check_currency('Adventure', {'Gold':1980,'Shard':1320,'Diamond':75,'Energy':140})
check_currency('Rank', {'Gold':3920,'Diamond':80})
check_currency('Guide', {'Gold':110,'Shard':300,'Diamond':10,'RouletteTicket':1})
check_currency('Mission', {'Gold':230,'Shard':60,'Energy':10,'RouletteTicket':1}, 'daily.')
check_currency('Mission', {'Gold':500,'Shard':180,'Diamond':20,'Energy':30,'RouletteTicket':3}, 'weekly.')
check_currency('Pass', {'Gold':500,'Shard':360,'Diamond':80,'Energy':50,'RouletteTicket':4})
assert {r['ownerId']:int(r['amount']) for r in reward if r['ownerType']=='CardDuplicate'} == {'Common':1,'Rare':2,'Arcane':5,'Mythic':10}
assert {(r['ownerKey'],r['currency']):int(r['amount']) for r in legacy} == {
    (r['ownerId'],r['rewardId']):int(r['amount']) for r in reward if r['ownerType']=='Adventure' and r['rewardType']=='Currency'}

old = json.loads((Path(__file__).parent/'reward_before.json').read_text(encoding='utf-8'))
for group in ('Album','Battle'):
    assert [r for r in old if r['ownerType']==group] == [r for r in reward if r['ownerType']==group], group
semantic_key = lambda r: tuple(r[k] for k in ('ownerType','ownerId','rewardType','rewardId'))
old_keys = {semantic_key(r):r['id'] for r in old}
assert all(r['id']==old_keys[semantic_key(r)] for r in reward if semantic_key(r) in old_keys)
old_by_id = {r['id']:semantic_key(r) for r in old}
assert all(semantic_key(r)==old_by_id[r['id']] for r in reward if r['id'] in old_by_id)

assert len(roulette)==8 and {int(r['slotIndex']) for r in roulette}==set(range(8))
assert sum(int(r['weight']) for r in roulette)==100
assert all(int(r['weight'])>0 and int(r['amount'])>0 and r['rewardId']!='RouletteTicket' for r in roulette)
ev = Counter()
for r in roulette:
    if r['rewardType']=='Pack':
        assert r['rewardId'] in pack_ids
    ev[(r['rewardType'],r['rewardId'])] += int(r['amount'])*int(r['weight'])/100
assert abs(ev[('Currency','Shard')]-7)<1e-10
assert abs(ev[('Currency','Gold')]-42.5)<1e-10

balance=0
guide_total=0
adventure_total=0
spends=[0,0,30,60,0,60,0,150,0,30,0,150,0]
node_at={2:1,5:2,7:3,9:4,11:5,13:6}
for step, spend in enumerate(spends,1):
    assert balance>=spend, ('guide underfunded', step, balance, spend)
    balance-=spend
    gs=totals('Guide',f'guide.{step:02d}').get(('Currency','Shard'),0)
    guide_total+=gs
    balance+=gs
    if step in node_at:
        ns=totals('Adventure',f'node_{node_at[step]:02d}').get(('Currency','Shard'),0)
        adventure_total+=ns
        balance+=ns
assert (guide_total,adventure_total,balance)==(300,240,60)

snapshot_path=Path(__file__).parent/'imported_tables.json'
assert snapshot_path.exists(), 'official importer verification snapshot missing'
imported=json.loads(snapshot_path.read_text(encoding='utf-8'))
verified_cells=0
for table in ('Reward','Mission','CardEnhance','PassLevel','RouletteSlot'):
    expected={r['id']:r for r in read(table)}
    actual={str(r['id']):r for r in imported[table]}
    assert set(actual)==set(expected), (table,'imported ids')
    for row_id, row in actual.items():
        fields={k for k in expected[row_id] if not k.startswith('#')}
        assert set(row)==fields, (table,row_id,'imported fields')
        for field, value in row.items():
            assert str(value)==expected[row_id][field], (table,row_id,field,value,expected[row_id][field])
            verified_cells+=1

summary={'result':'PASS','rows':{'Reward':len(reward),'Mission':len(missions),'RouletteSlot':len(roulette),
         'AdventureReward':len(legacy),'CardEnhance':len(enhance),'PassLevel':len(pass_levels)},
         'firstHour':{'guideShard':guide_total,'adventureShard':adventure_total,'remainingShard':balance},
         'importedTypedCellsMatched':verified_cells,
         'unsupportedRewardTypes':dict(Counter(r['rewardType'] for r in reward if r['rewardType']!='Currency')),
         'unsupportedRoulettePackSlots':sum(r['rewardType']=='Pack' for r in roulette)}
(Path(__file__).parent/'validation.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False))
