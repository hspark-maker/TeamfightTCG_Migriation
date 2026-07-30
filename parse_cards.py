import os
import glob
import re

# 카드 파일 찾기 (Effect/Passive 제외)
card_files = []
for f in glob.glob("Assets/SO/Cards/**/*.asset", recursive=True):
    if "Effect" not in f and "Passive" not in f:
        card_files.append(f)

card_files.sort()

# 키워드 매핑
keywords_map = {
    1: "Ranged",
    2: "Peerless",
    4: "Execution",
    8: "Taunt",
    16: "Cunning",
    32: "Mark",
    64: "Healer",
    128: "Invincible",
    256: "BonusHp"
}

cards_info = []

for card_file in card_files:
    try:
        with open(card_file, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # displayName 추출
        name_match = re.search(r'displayName: (.+?)(?:\n|$)', content)
        display_name = name_match.group(1).strip() if name_match else "Unknown"
        # quoted 문자열 처리
        display_name = display_name.replace('\u', '\U000')
        
        # keywords 추출
        kw_match = re.search(r'keywords: (\d+)', content)
        keywords_val = int(kw_match.group(1)) if kw_match else 0
        
        # maxHp 추출
        hp_match = re.search(r'maxHp: (\d+)', content)
        max_hp = int(hp_match.group(1)) if hp_match else 0
        
        # passive 확인 (passive: 줄 다음에 {fileID가 있는지)
        passive_match = re.search(r'passive: (\{fileID: \d+|{fileID: 0\})', content)
        has_passive = passive_match and 'fileID: 11400000' in passive_match.group(1)
        
        # synergies 항목 개수 세기
        synergies_match = re.search(r'synergies:\n((?:  - \{fileID.*\n)*)', content)
        synergies_count = 0
        if synergies_match:
            synergy_lines = synergies_match.group(1).strip()
            synergies_count = len([l for l in synergy_lines.split('\n') if l.strip().startswith('- {fileID')])
        
        # 키워드 이름 변환
        keyword_names = []
        for kbit, kname in keywords_map.items():
            if keywords_val & kbit:
                keyword_names.append(kname)
        keyword_str = ", ".join(keyword_names) if keyword_names else "None"
        
        cards_info.append({
            'file': card_file,
            'name': display_name,
            'keywords': keyword_str,
            'maxHp': max_hp,
            'has_passive': has_passive,
            'synergies_count': synergies_count
        })
    except Exception as e:
        print(f"Error reading {card_file}: {e}")

# 출력
print("=" * 120)
for idx, card in enumerate(cards_info):
    fname = os.path.basename(card['file']).replace('.asset', '')
    print(f"{idx+1:2}. {card['name']:20} | HP: {card['maxHp']:2} | Keywords: {card['keywords']:40} | Passive: {'Yes' if card['has_passive'] else 'No':3} | Syns: {card['synergies_count']}")

print("=" * 120)
print(f"Total cards: {len(cards_info)}")
no_kw = [c for c in cards_info if c['keywords'] == 'None']
no_passive = [c for c in cards_info if not c['has_passive']]
print(f"Cards without keywords: {len(no_kw)}")
print(f"Cards without passive: {len(no_passive)}")
print(f"Cards without keywords AND without passive AND no synergies: {len([c for c in cards_info if c['keywords'] == 'None' and not c['has_passive'] and c['synergies_count'] == 0])}")
