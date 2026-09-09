# -*- coding: utf-8 -*-
import re

p = r"Assets/Assets/Prefabs/UI/PooledUI/MissionOverlay.prefab"
text = open(p, encoding='utf-8').read()


def sub1(pat, rep, t, label):
    new, n = re.subn(pat, rep, t, count=1, flags=re.S | re.M)
    assert n == 1, "FAILED: " + label
    return new


# Reward GO: add Button component
text = sub1(r'(  - component: \{fileID: 222494283176913527\}\n)(  m_Layer: 0\n  m_Name: Reward\n)',
            '\\1  - component: {fileID: 9300000000000000001}\n\\2', text, 'Reward GO comp')

# MissionPanel: bind claimAllButton (insert after closeButton line)
text = sub1(r'(  closeButton: \{fileID: 0\}\n)',
            '\\1  claimAllButton: {fileID: 9300000000000000001}\n', text, 'panel claimAll bind')

BTN = """--- !u!114 &9300000000000000001
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 8529837369470202622}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 4e29b1a8efbd4b44bb3f3716e73f07ff, type: 3}
  m_Name:
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.Button
  m_Navigation:
    m_Mode: 3
    m_WrapAround: 0
    m_SelectOnUp: {fileID: 0}
    m_SelectOnDown: {fileID: 0}
    m_SelectOnLeft: {fileID: 0}
    m_SelectOnRight: {fileID: 0}
  m_Transition: 1
  m_Colors:
    m_NormalColor: {r: 1, g: 1, b: 1, a: 1}
    m_HighlightedColor: {r: 0.9607843, g: 0.9607843, b: 0.9607843, a: 1}
    m_PressedColor: {r: 0.78431374, g: 0.78431374, b: 0.78431374, a: 1}
    m_SelectedColor: {r: 0.9607843, g: 0.9607843, b: 0.9607843, a: 1}
    m_DisabledColor: {r: 0.78431374, g: 0.78431374, b: 0.78431374, a: 0.5019608}
    m_ColorMultiplier: 1
    m_FadeDuration: 0.1
  m_SpriteState:
    m_HighlightedSprite: {fileID: 0}
    m_PressedSprite: {fileID: 0}
    m_SelectedSprite: {fileID: 0}
    m_DisabledSprite: {fileID: 0}
  m_AnimationTriggers:
    m_NormalTrigger: Normal
    m_HighlightedTrigger: Highlighted
    m_PressedTrigger: Pressed
    m_SelectedTrigger: Selected
    m_DisabledTrigger: Disabled
  m_Interactable: 1
  m_TargetGraphic: {fileID: 222494283176913527}
  m_OnClick:
    m_PersistentCalls:
      m_Calls: []
"""
if not text.endswith('\n'):
    text += '\n'
text += BTN
open(p, 'w', encoding='utf-8', newline='\r\n').write(text)
print("CLAIM ALL BTN OK")
