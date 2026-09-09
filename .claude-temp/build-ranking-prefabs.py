from pathlib import Path
import re
import uuid
import json
import copy

ROOT = Path('C:/Users/cookapps/TeamfightTCG_Migriation')

def load(path):
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    parts = re.split(r'(?m)^--- !u!(\d+) &(\d+)([^\n]*)\n', text)
    return parts[0], {int(parts[i+1]): [parts[i], parts[i+2], parts[i+3]]
                      for i in range(1, len(parts), 4)}

def save(path, head, docs):
    text = head + ''.join(f'--- !u!{v[0]} &{k}{v[1]}\n{v[2]}' for k,v in docs.items())
    (ROOT / path).write_text(text, encoding='utf-8', newline='\n')

def field(docs, id, key, value):
    pattern = rf'^  {re.escape(key)}:.*?(?=^  [A-Za-z_]\w*:|\Z)'
    replacement = f'  {key}: {value}\n' if not value.startswith('\n') else f'  {key}:{value}\n'
    body, count = re.subn(pattern, lambda _: replacement, docs[id][2], count=1, flags=re.M|re.S)
    if count != 1: raise ValueError(f'Missing field {id}/{key}')
    docs[id][2] = body

def scalar(docs, id, key, value):
    body, count = re.subn(rf'^  {re.escape(key)}:.*$', lambda _: f'  {key}: {value}', docs[id][2], count=1, flags=re.M)
    if count != 1: raise ValueError(f'Missing scalar {id}/{key}')
    docs[id][2] = body

def children(docs, id, ids):
    field(docs, id, 'm_Children', '\n' + '\n'.join(f'  - {{fileID: {i}}}' for i in ids) if ids else '[]')

def rect(docs, id, x, y, w, h, ax=0, ay=.5, px=0, py=.5):
    for k,v in dict(m_AnchorMin=f'{{x: {ax}, y: {ay}}}', m_AnchorMax=f'{{x: {ax}, y: {ay}}}',
                    m_AnchoredPosition=f'{{x: {x}, y: {y}}}', m_SizeDelta=f'{{x: {w}, y: {h}}}',
                    m_Pivot=f'{{x: {px}, y: {py}}}').items(): scalar(docs,id,k,v)

def text_style(docs,id,text,size,color='{r: 1, g: 1, b: 1, a: 1}',align=1):
    for k,v in dict(m_text=json.dumps(text,ensure_ascii=True), m_fontSize=size,m_fontSizeBase=size,
                    m_fontColor=color, m_HorizontalAlignment=align,m_VerticalAlignment=512,
                    m_TextWrappingMode=0,m_overflowMode=1,m_RaycastTarget=0).items(): scalar(docs,id,k,v)
    field(docs,id,'m_fontColor32','\n    serializedVersion: 2\n    rgba: 4294967295')

def clone_text(docs, template, start, name, parent):
    old_go,old_rect,old_renderer,old_text = template
    mapping = {old_go:start,old_rect:start+1,old_renderer:start+2,old_text:start+3}
    for old,new in mapping.items():
        doc=copy.deepcopy(docs[old])
        doc[2]=re.sub(r'fileID: (\d+)',lambda m:f'fileID: {mapping.get(int(m[1]),int(m[1]))}',doc[2])
        docs[new]=doc
    scalar(docs,start,'m_Name',name)
    scalar(docs,start+1,'m_Father',f'{{fileID: {parent}}}')
    return start+1,start+3

def meta(path, importer):
    p=ROOT / (path+'.meta')
    if p.exists(): return re.search(r'^guid: (\w+)',p.read_text(),re.M)[1]
    guid=uuid.uuid4().hex
    p.write_text(f'fileFormatVersion: 2\nguid: {guid}\n{importer}:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',encoding='utf-8')
    return guid

def script_guid(name):
    return re.search(r'^guid: (\w+)',(ROOT/f'Assets/Scripts/UI/Rank/{name}.cs.meta').read_text(),re.M)[1]

row_src='Assets/Assets/Prefabs/UI/LobbyUI/RankUI/RankRewardRow.prefab'
row_path='Assets/Assets/Prefabs/UI/LobbyUI/RankUI/RankingRow.prefab'
panel_src='Assets/Assets/Prefabs/UI/PooledUI/RankRewardOverlay.prefab'
panel_path='Assets/Assets/Prefabs/UI/PooledUI/RankingBoardOverlay.prefab'
hud_path='Assets/Assets/Prefabs/UI/LobbyUI/RankInfo.prefab'
row_guid=meta(row_path,'PrefabImporter')
panel_guid=meta(panel_path,'PrefabImporter')

# Copy only the existing row card, badge and text; reward subtrees are excluded.
head,row=load(row_src)
keep_go={4470619398732920359,4385313478583332640,7475179357797161146,2239777404675944450}
row={id:doc for id,doc in row.items() if id in keep_go or
     (re.search(r'^  m_GameObject: \{fileID: (\d+)\}',doc[2],re.M) and
      int(re.search(r'^  m_GameObject: \{fileID: (\d+)\}',doc[2],re.M)[1]) in keep_go)}
scalar(row,4470619398732920359,'m_Name','RankingRow')
children(row,5554446052095675461,[369651437671738269])
scalar(row,5554446052095675461,'m_SizeDelta','{x: 848, y: 120}')
scalar(row,369651437671738269,'m_SizeDelta','{x: 0, y: 114}')
scalar(row,5667145076700154398,'m_MinHeight',120)
scalar(row,5667145076700154398,'m_PreferredHeight',120)
scalar(row,1916718639954387903,'m_Color','{r: 0.12, g: 0.16, b: 0.23, a: 1}')
rect(row,1977641465980789413,106,0,80,80)
rect(row,4295114394640639943,204,-24,380,32)
text_style(row,4327042663298593017,'티어',24,'{r: 0.7, g: 0.78, b: 0.84, a: 1}')
template=(2239777404675944450,4295114394640639943,2834316778535405343,4327042663298593017)
rank_rect,rank_text=clone_text(row,template,8100000000000001000,'Rank',369651437671738269)
name_rect,name_text=clone_text(row,template,8100000000000001100,'Nickname',369651437671738269)
points_rect,points_text=clone_text(row,template,8100000000000001200,'Points',369651437671738269)
rect(row,rank_rect,16,0,70,64)
text_style(row,rank_text,'1',36,align=2)
rect(row,name_rect,204,20,440,44)
text_style(row,name_text,'플레이어',34)
scalar(row,name_text,'m_isRichText',0)
rect(row,points_rect,-24,0,176,48,ax=1,px=1)
text_style(row,points_text,'0 P',32,align=4)
children(row,369651437671738269,[rank_rect,1977641465980789413,name_rect,4295114394640639943,points_rect])
component=row[2277039532059551791][2]
component=component[:component.index('  badgeImage:')]
component=component.replace('7a4a58b3550bd65418275839f5c9df4e',script_guid('RankingRowView')).replace('RankRewardRowView','RankingRowView')
component+=f'''  rankText: {{fileID: {rank_text}}}
  nicknameText: {{fileID: {name_text}}}
  tierNameText: {{fileID: 4327042663298593017}}
  pointsText: {{fileID: {points_text}}}
  badgeImage: {{fileID: 1147913892960466376}}
  background: {{fileID: 1916718639954387903}}
  normalColor: {{r: 0.12, g: 0.16, b: 0.23, a: 1}}
  selfColor: {{r: 0.18, g: 0.36, b: 0.43, a: 1}}
'''
row[2277039532059551791][2]=component
save(row_path,head,row)

# Clone the overlay shell and remove reward mock instances and coin effect.
head,panel=load(panel_src)
instances={id for id,doc in panel.items() if doc[0]=='1001'}
remove_go=4347059877444763963
panel={id:doc for id,doc in panel.items() if id not in instances and id!=remove_go and
       not any(f'm_PrefabInstance: {{fileID: {i}}}' in doc[2] for i in instances) and
       f'm_GameObject: {{fileID: {remove_go}}}' not in doc[2]}
for doc in panel.values():
    doc[2]=doc[2].replace('  - {fileID: 4111425866388583674}\n','')
    doc[2]=doc[2].replace('RankRewardPanel','RankingBoardPanel')
children(panel,5655762576460979311,[])
scalar(panel,6145127748300545670,'m_Name','RankingBoardOverlay')
scalar(panel,3475588569416217379,'m_Script',f'{{fileID: 11500000, guid: {script_guid("RankingBoardPanel")}, type: 3}}')
scalar(panel,3475588569416217379,'rowPrefab',f'{{fileID: 2277039532059551791, guid: {row_guid}, type: 3}}')
panel[3475588569416217379][2]+= '  sampleProfiles: {fileID: 11400000, guid: 7f6df2feb8d5076418b96358ee58377d, type: 2}\n'
scalar(panel,288225758036960409,'m_text',json.dumps('랭킹'))
field(panel,2300232790519440821,'m_OnClick','\n    m_PersistentCalls:\n      m_Calls: []')
scalar(panel,1556845238083296395,'m_AnchoredPosition','{x: 0, y: -94}')
scalar(panel,1556845238083296395,'m_SizeDelta','{x: -76, y: -264}')
title_template=(2974392808366636566,708699530486641231,1541633181659012910,288225758036960409)
notice_rect,notice_text=clone_text(panel,title_template,8200000000000001000,'SampleNotice',182366430554581995)
rect(panel,notice_rect,0,-132,840,76,ax=.5,ay=1,px=.5)
text_style(panel,notice_text,'미리보기 · 샘플 순위\n내 닉네임·티어·포인트만 실제 정보입니다.',27,'{r: 0.28, g: 0.25, b: 0.22, a: 1}',align=2)
children(panel,182366430554581995,[5131416005614839919,notice_rect,1556845238083296395])
save(panel_path,head,panel)

# The existing badge Button owns clicks, so bind its event directly to RankHud.
head,hud=load(hud_path)
field(hud,1871310651560559262,'m_OnClick','''
    m_PersistentCalls:
      m_Calls:
      - m_Target: {fileID: 2835394760567195396}
        m_TargetAssemblyTypeName: RankHud, Assembly-CSharp
        m_MethodName: OpenRanking
        m_Mode: 1
        m_Arguments:
          m_ObjectArgument: {fileID: 0}
          m_ObjectArgumentAssemblyTypeName: UnityEngine.Object, UnityEngine
          m_IntArgument: 0
          m_FloatArgument: 0
          m_StringArgument: 
          m_BoolArgument: 0
        m_CallState: 2''')
scalar(hud,2766345039964915729,'m_RaycastTarget',1)
save(hud_path,head,hud)

# Register only the panel; the row is its serialized dependency.
group=ROOT/'Assets/AddressableAssetsData/AssetGroups/Default Local Group.asset'
group_text=group.read_text(encoding='utf-8-sig')
entry=re.search(r'^  - m_GUID: 8a64cbb246ea50344ac445bc7d85e0c7\n.*?(?=^  - m_GUID:|^  [A-Za-z_]|\Z)',group_text,re.M|re.S)
if entry is None: raise ValueError('RankRewardPanel Addressables entry missing')
new_entry=entry[0].replace('8a64cbb246ea50344ac445bc7d85e0c7',panel_guid).replace('RankRewardPanel','RankingBoardPanel')
if f'm_GUID: {panel_guid}' not in group_text:
    group_text=group_text[:entry.end()]+new_entry+group_text[entry.end():]
    group.write_text(group_text,encoding='utf-8',newline='\n')
print('Created RankingBoardOverlay and RankingRow; wired RankInfo badge and UIPrefab address.')
