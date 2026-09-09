# -*- coding: utf-8 -*-
import re

p = r"Assets/Assets/Prefabs/UI/PooledUI/MissionOverlay.prefab"
text = open(p, encoding='utf-8').read()  # universal newlines -> \n


def sub1(pat, rep, t, label):
    new, n = re.subn(pat, rep, t, count=1, flags=re.S | re.M)
    assert n == 1, "FAILED: " + label
    return new


text = sub1(r'(  - component: \{fileID: 1983020375170869869\}\n)(  m_Layer: 0\n  m_Name: Daily\n)',
            '\\1  - component: {fileID: 9100000000000000021}\n\\2', text, 'Daily GO')
text = sub1(r'(  - component: \{fileID: 9144479639886922138\}\n)(  m_Layer: 0\n  m_Name: Weekly\n)',
            '\\1  - component: {fileID: 9100000000000000022}\n\\2', text, 'Weekly GO')
text = sub1(r'(  - component: \{fileID: 981379699637536113\}\n)(  m_Layer: 0\n  m_Name: Button\n)',
            '\\1  - component: {fileID: 9100000000000000023}\n\\2', text, 'Claim GO')
text = sub1(r'(  - component: \{fileID: 894394265681300909\}\n)(  m_Layer: 0\n  m_Name: Mission\n)',
            '\\1  - component: {fileID: 9100000000000000024}\n  - component: {fileID: 9100000000000000025}\n\\2', text, 'Mission GO')
text = sub1(r'(  - \{fileID: 4718098940925985922\}\n)  - \{fileID: 509113565192813588\}\n(  m_Father: \{fileID: 9130667183468646482\})',
            '\\1  - {fileID: 9100000000000000002}\n  - {fileID: 9100000000000000012}\n  - {fileID: 9100000000000000032}\n  - {fileID: 9100000000000000042}\n\\2',
            text, 'Panel children')
text = sub1(r'(  - \{fileID: 392164926265848772\}\n)  m_Father: \{fileID: 421670764052333424\}',
            '\\1  m_Father: {fileID: 9100000000000000002}', text, 'Mission father')

m = re.search(r'--- !u!114 &7718547985260651558\n(.*?)(?=^--- )', text, re.S | re.M)
blk = m.group(1)
blk2 = blk.replace('m_Type: 1', 'm_Type: 3', 1).replace('m_FillMethod: 4', 'm_FillMethod: 0', 1)
assert blk2 != blk, 'gauge fill'
text = text.replace(blk, blk2, 1)

mp_old = """  dailyContent: {fileID: 0}
  weeklyContent: {fileID: 0}
  rowPrefab: {fileID: 576488338440483197, guid: cdeddb68132935748a3322e54841647f, type: 3}
  dailyEmptyNotice: {fileID: 0}
  weeklyEmptyNotice: {fileID: 0}
  dailyResetText: {fileID: 0}
  weeklyResetText: {fileID: 0}
  closeButton: {fileID: 0}
  dimButton: {fileID: 7547680357580154284}"""
mp_new = """  dailyContent: {fileID: 9100000000000000002}
  weeklyContent: {fileID: 9100000000000000012}
  rowPrefab: {fileID: 9100000000000000024}
  dailyEmptyNotice: {fileID: 9100000000000000031}
  weeklyEmptyNotice: {fileID: 9100000000000000041}
  dailyResetText: {fileID: 0}
  weeklyResetText: {fileID: 0}
  resetText: {fileID: 6896683498800134000}
  dailyTabButton: {fileID: 9100000000000000021}
  weeklyTabButton: {fileID: 9100000000000000022}
  tabDimColor: {r: 0.6, g: 0.6, b: 0.6, a: 1}
  closeButton: {fileID: 0}
  dimButton: {fileID: 7547680357580154284}"""
assert mp_old in text, 'MissionPanel block'
text = text.replace(mp_old, mp_new, 1)

BTN = """--- !u!114 &{FID}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {GO}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 4e29b1a8efbd4b44bb3f3716e73f07ff, type: 3}}
  m_Name:
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.Button
  m_Navigation:
    m_Mode: 3
    m_WrapAround: 0
    m_SelectOnUp: {{fileID: 0}}
    m_SelectOnDown: {{fileID: 0}}
    m_SelectOnLeft: {{fileID: 0}}
    m_SelectOnRight: {{fileID: 0}}
  m_Transition: 1
  m_Colors:
    m_NormalColor: {{r: 1, g: 1, b: 1, a: 1}}
    m_HighlightedColor: {{r: 0.9607843, g: 0.9607843, b: 0.9607843, a: 1}}
    m_PressedColor: {{r: 0.78431374, g: 0.78431374, b: 0.78431374, a: 1}}
    m_SelectedColor: {{r: 0.9607843, g: 0.9607843, b: 0.9607843, a: 1}}
    m_DisabledColor: {{r: 0.78431374, g: 0.78431374, b: 0.78431374, a: 0.5019608}}
    m_ColorMultiplier: 1
    m_FadeDuration: 0.1
  m_SpriteState:
    m_HighlightedSprite: {{fileID: 0}}
    m_PressedSprite: {{fileID: 0}}
    m_SelectedSprite: {{fileID: 0}}
    m_DisabledSprite: {{fileID: 0}}
  m_AnimationTriggers:
    m_NormalTrigger: Normal
    m_HighlightedTrigger: Highlighted
    m_PressedTrigger: Pressed
    m_SelectedTrigger: Selected
    m_DisabledTrigger: Disabled
  m_Interactable: 1
  m_TargetGraphic: {{fileID: {TG}}}
  m_OnClick:
    m_PersistentCalls:
      m_Calls: []
"""
app = []
app.append(BTN.format(FID='9100000000000000021', GO='7972538358503599197', TG='8691722106296409'))
app.append(BTN.format(FID='9100000000000000022', GO='4840769777466118676', TG='2460374652123522956'))
app.append(BTN.format(FID='9100000000000000023', GO='2028950075330865346', TG='981379699637536113'))

app.append("""--- !u!114 &9100000000000000024
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 8446096249568640013}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 273400c51ac4e824388b6a254c088ca3, type: 3}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::MissionRowView
  titleText: {fileID: 3623326276551236923}
  descriptionText: {fileID: 0}
  progressText: {fileID: 878727809630740993}
  progressFill: {fileID: 7718547985260651558}
  rewardText: {fileID: 161339362389999048}
  claimButton: {fileID: 9100000000000000023}
  claimLabel: {fileID: 6892983214456880389}
  claimedMark: {fileID: 0}
  disabledAlpha: 0.5
  claimGroup: {fileID: 0}
""")
app.append("""--- !u!114 &9100000000000000025
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 8446096249568640013}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 306cc8c2b49d7114eaa3623786fc2126, type: 3}
  m_Name:
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.LayoutElement
  m_IgnoreLayout: 0
  m_MinWidth: -1
  m_MinHeight: -1
  m_PreferredWidth: 857.4
  m_PreferredHeight: 158.7
  m_FlexibleWidth: -1
  m_FlexibleHeight: -1
  m_LayoutPriority: 1
""")

CONTENT = """--- !u!1 &{GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {RT}}}
  - component: {{fileID: {VLG}}}
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
  m_Children:{CHILDREN}
  m_Father: {{fileID: 421670764052333424}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
  m_AnchorMin: {{x: 0.5, y: 0.5}}
  m_AnchorMax: {{x: 0.5, y: 0.5}}
  m_AnchoredPosition: {{x: 0, y: -160}}
  m_SizeDelta: {{x: 880, y: 830}}
  m_Pivot: {{x: 0.5, y: 0.5}}
--- !u!114 &{VLG}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {GO}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 59f8146938fff824cb5fd77236b75775, type: 3}}
  m_Name:
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.VerticalLayoutGroup
  m_Padding:
    m_Left: 0
    m_Right: 0
    m_Top: 10
    m_Bottom: 0
  m_ChildAlignment: 1
  m_Spacing: 20
  m_ChildForceExpandWidth: 0
  m_ChildForceExpandHeight: 0
  m_ChildControlWidth: 0
  m_ChildControlHeight: 0
  m_ChildScaleWidth: 0
  m_ChildScaleHeight: 0
  m_ReverseArrangement: 0
"""
app.append(CONTENT.format(GO='9100000000000000001', RT='9100000000000000002', VLG='9100000000000000003',
                          NAME='Daily_Content', ACTIVE='1', CHILDREN='\n  - {fileID: 509113565192813588}'))
app.append(CONTENT.format(GO='9100000000000000011', RT='9100000000000000012', VLG='9100000000000000013',
                          NAME='Weekly_Content', ACTIVE='0', CHILDREN=' []'))

mt = re.search(r'--- !u!114 &3704280889250819080\nMonoBehaviour:\n(.*?)(?=^--- )', text, re.S | re.M)
tmp_body = mt.group(1)


def make_tmp(fid, go, msg):
    b = re.sub(r'm_GameObject: \{fileID: \d+\}', 'm_GameObject: {fileID: %s}' % go, tmp_body, count=1)
    b = re.sub(r'm_text: .*', lambda _m: 'm_text: "%s"' % msg, b, count=1)
    b = re.sub(r'm_fontSize: [\d.]+', 'm_fontSize: 40', b, count=1)
    b = re.sub(r'm_fontSizeBase: [\d.]+', 'm_fontSizeBase: 40', b, count=1)
    b = re.sub(r'm_HorizontalAlignment: \d+', 'm_HorizontalAlignment: 2', b, count=1)
    return "--- !u!114 &%s\nMonoBehaviour:\n%s" % (fid, b)


NOTICE = """--- !u!1 &{GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {RT}}}
  - component: {{fileID: {CR}}}
  - component: {{fileID: {TMP}}}
  m_Layer: 0
  m_Name: {NAME}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 0
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
  m_Children: []
  m_Father: {{fileID: 421670764052333424}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
  m_AnchorMin: {{x: 0.5, y: 0.5}}
  m_AnchorMax: {{x: 0.5, y: 0.5}}
  m_AnchoredPosition: {{x: 0, y: -160}}
  m_SizeDelta: {{x: 700, y: 80}}
  m_Pivot: {{x: 0.5, y: 0.5}}
--- !u!222 &{CR}
CanvasRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {GO}}}
  m_CullTransparentMesh: 0
"""
app.append(NOTICE.format(GO='9100000000000000031', RT='9100000000000000032', CR='9100000000000000033',
                         TMP='9100000000000000034', NAME='Daily_Empty'))
app.append(make_tmp('9100000000000000034', '9100000000000000031',
                    '\\uC9C4\\uD589 \\uC911\\uC778 \\uBBF8\\uC158\\uC774 \\uC5C6\\uC2B5\\uB2C8\\uB2E4'))
app.append(NOTICE.format(GO='9100000000000000041', RT='9100000000000000042', CR='9100000000000000043',
                         TMP='9100000000000000044', NAME='Weekly_Empty'))
app.append(make_tmp('9100000000000000044', '9100000000000000041',
                    '\\uC774\\uBC88 \\uC8FC \\uBBF8\\uC158\\uC774 \\uC5C6\\uC2B5\\uB2C8\\uB2E4'))

if not text.endswith('\n'):
    text += '\n'
text += ''.join(app)
open(p, 'w', encoding='utf-8', newline='\r\n').write(text)
print("ALL EDITS OK")
