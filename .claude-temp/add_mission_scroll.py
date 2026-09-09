# -*- coding: utf-8 -*-
import re

p = r"Assets/Assets/Prefabs/UI/PooledUI/MissionOverlay.prefab"
text = open(p, encoding='utf-8').read()


def sub1(pat, rep, t, label):
    new, n = re.subn(pat, rep, t, count=1, flags=re.S | re.M)
    assert n == 1, "FAILED: " + label
    return new


# 1) Panel children: content RTs -> scroll wrapper RTs
text = sub1(r'  - \{fileID: 9100000000000000002\}\n  - \{fileID: 9100000000000000012\}\n',
            '  - {fileID: 9200000000000000002}\n  - {fileID: 9200000000000000012}\n', text, 'Panel children')

# 2) Daily_Content RT: father -> Daily_Scroll, top-stretch anchors, pivot top
old_daily_rt = """--- !u!224 &9100000000000000002
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 9100000000000000001}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {fileID: 509113565192813588}
  m_Father: {fileID: 421670764052333424}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
  m_AnchorMin: {x: 0.5, y: 0.5}
  m_AnchorMax: {x: 0.5, y: 0.5}
  m_AnchoredPosition: {x: 0, y: -160}
  m_SizeDelta: {x: 880, y: 830}
  m_Pivot: {x: 0.5, y: 0.5}"""
new_daily_rt = """--- !u!224 &9100000000000000002
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 9100000000000000001}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {fileID: 509113565192813588}
  m_Father: {fileID: 9200000000000000002}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
  m_AnchorMin: {x: 0, y: 1}
  m_AnchorMax: {x: 1, y: 1}
  m_AnchoredPosition: {x: 0, y: 0}
  m_SizeDelta: {x: 0, y: 200}
  m_Pivot: {x: 0.5, y: 1}"""
assert old_daily_rt in text, 'daily rt'
text = text.replace(old_daily_rt, new_daily_rt, 1)

old_weekly_rt = old_daily_rt.replace('9100000000000000002', '9100000000000000012') \
                            .replace('9100000000000000001', '9100000000000000011') \
                            .replace('  m_Children:\n  - {fileID: 509113565192813588}', '  m_Children: []')
new_weekly_rt = new_daily_rt.replace('9100000000000000002', '9100000000000000012') \
                            .replace('9100000000000000001', '9100000000000000011') \
                            .replace('9200000000000000002', '9200000000000000012') \
                            .replace('  m_Children:\n  - {fileID: 509113565192813588}', '  m_Children: []')
assert old_weekly_rt in text, 'weekly rt'
text = text.replace(old_weekly_rt, new_weekly_rt, 1)

# 3) content GOs: add ContentSizeFitter comp; weekly content back to active (wrapper carries inactive)
text = sub1(r'(  - component: \{fileID: 9100000000000000003\}\n)(  m_Layer: 0\n  m_Name: Daily_Content\n)',
            '\\1  - component: {fileID: 9200000000000000005}\n\\2', text, 'daily CSF comp')
text = sub1(r'(  - component: \{fileID: 9100000000000000013\}\n)(  m_Layer: 0\n  m_Name: Weekly_Content\n)',
            '\\1  - component: {fileID: 9200000000000000015}\n\\2', text, 'weekly CSF comp')
text = sub1(r'(  m_Name: Weekly_Content\n.*?)  m_IsActive: 0',
            '\\1  m_IsActive: 1', text, 'weekly content active')

# 4) MissionPanel: add list root fields
text = sub1(r'(  weeklyContent: \{fileID: 9100000000000000012\}\n)',
            '\\1  dailyListRoot: {fileID: 9100000000000000001}\n  weeklyListRoot: {fileID: 9100000000000000011}\n',
            text, 'panel list roots')
# those must point at wrapper GOs, not content GOs:
text = text.replace('  dailyListRoot: {fileID: 9100000000000000001}', '  dailyListRoot: {fileID: 9200000000000000001}', 1)
text = text.replace('  weeklyListRoot: {fileID: 9100000000000000011}', '  weeklyListRoot: {fileID: 9200000000000000011}', 1)

SCROLL = """--- !u!1 &{GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {RT}}}
  - component: {{fileID: {SR}}}
  - component: {{fileID: {MASK}}}
  m_Layer: 0
  m_Name: {NAME}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: {ACTIVE}
--- !u!224 &{RT}
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {GO}}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {{fileID: {CONTENT_RT}}}
  m_Father: {{fileID: 421670764052333424}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
  m_AnchorMin: {{x: 0.5, y: 0.5}}
  m_AnchorMax: {{x: 0.5, y: 0.5}}
  m_AnchoredPosition: {{x: 0, y: -160}}
  m_SizeDelta: {{x: 880, y: 830}}
  m_Pivot: {{x: 0.5, y: 0.5}}
--- !u!114 &{SR}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {GO}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 1aa08ab6e0800fa44ae55d278d1423e3, type: 3}}
  m_Name:
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.ScrollRect
  m_Content: {{fileID: {CONTENT_RT}}}
  m_Horizontal: 0
  m_Vertical: 1
  m_MovementType: 2
  m_Elasticity: 0.1
  m_Inertia: 1
  m_DecelerationRate: 0.135
  m_ScrollSensitivity: 30
  m_Viewport: {{fileID: {RT}}}
  m_HorizontalScrollbar: {{fileID: 0}}
  m_VerticalScrollbar: {{fileID: 0}}
  m_HorizontalScrollbarVisibility: 0
  m_VerticalScrollbarVisibility: 0
  m_HorizontalScrollbarSpacing: 0
  m_VerticalScrollbarSpacing: 0
  m_OnValueChanged:
    m_PersistentCalls:
      m_Calls: []
--- !u!114 &{MASK}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {GO}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 3312d7739989d2b4e91e6319e9a96d76, type: 3}}
  m_Name:
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.RectMask2D
  m_Padding: {{x: 0, y: 0, z: 0, w: 0}}
  m_Softness: {{x: 0, y: 0}}
"""
CSF = """--- !u!114 &{FID}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {GO}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 3245ec927659c4140ac4f8d17403cc18, type: 3}}
  m_Name:
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.ContentSizeFitter
  m_HorizontalFit: 0
  m_VerticalFit: 2
"""
app = []
app.append(SCROLL.format(GO='9200000000000000001', RT='9200000000000000002', SR='9200000000000000003',
                         MASK='9200000000000000004', CONTENT_RT='9100000000000000002',
                         NAME='Daily_Scroll', ACTIVE='1'))
app.append(SCROLL.format(GO='9200000000000000011', RT='9200000000000000012', SR='9200000000000000013',
                         MASK='9200000000000000014', CONTENT_RT='9100000000000000012',
                         NAME='Weekly_Scroll', ACTIVE='0'))
app.append(CSF.format(FID='9200000000000000005', GO='9100000000000000001'))
app.append(CSF.format(FID='9200000000000000015', GO='9100000000000000011'))

if not text.endswith('\n'):
    text += '\n'
text += ''.join(app)
open(p, 'w', encoding='utf-8', newline='\r\n').write(text)
print("SCROLL EDITS OK")
