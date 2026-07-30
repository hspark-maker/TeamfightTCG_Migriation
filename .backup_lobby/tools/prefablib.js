// Minimal, surgical Unity prefab YAML editor.
// Only touches the exact lines asked for; everything else is preserved byte-for-byte.
const fs = require('fs');

const SCRIPT_GUID = {
  LayoutElement: '306cc8c2b49d7114eaa3623786fc2126',
  VerticalLayoutGroup: '59f8146938fff824cb5fd77236b75775',
  HorizontalLayoutGroup: '30649d3a9faa99c48a7b1166b86bf2a0',
  ContentSizeFitter: '3245ec927659c4140ac4f8d17403cc18',
};
const CLASS_NAME = {
  LayoutElement: 'UnityEngine.UI::UnityEngine.UI.LayoutElement',
  VerticalLayoutGroup: 'UnityEngine.UI::UnityEngine.UI.VerticalLayoutGroup',
  HorizontalLayoutGroup: 'UnityEngine.UI::UnityEngine.UI.HorizontalLayoutGroup',
  ContentSizeFitter: 'UnityEngine.UI::UnityEngine.UI.ContentSizeFitter',
};

class Prefab {
  constructor(path) {
    this.path = path;
    const raw = fs.readFileSync(path, 'utf8');
    if (raw.includes('\r\n')) throw new Error('unexpected CRLF');
    const lines = raw.split('\n');
    if (lines[lines.length - 1] === '') lines.pop(); // trailing newline
    this.preamble = [];
    this.docs = [];
    let cur = null;
    for (const line of lines) {
      const m = line.match(/^--- !u!(\d+) &(\d+)(.*)$/);
      if (m) {
        cur = { header: line, classId: m[1], fileId: m[2], stripped: /stripped/.test(m[3]), lines: [] };
        this.docs.push(cur);
        continue;
      }
      if (cur) cur.lines.push(line); else this.preamble.push(line);
    }
    this.reindex();
  }

  reindex() {
    this.byId = new Map(this.docs.map(d => [d.fileId, d]));
    // GameObject name + components
    this.goByName = new Map();
    for (const d of this.docs) {
      if (d.classId !== '1' || d.stripped) continue;
      const n = this.get(d, 'm_Name');
      if (!this.goByName.has(n)) this.goByName.set(n, []);
      this.goByName.get(n).push(d);
    }
    // RectTransform -> gameobject / father / children
    this.rtByGo = new Map();
    for (const d of this.docs) {
      if ((d.classId !== '224' && d.classId !== '4') || d.stripped) continue;
      const go = this.ref(this.get(d, 'm_GameObject'));
      if (go) this.rtByGo.set(go, d);
    }
  }

  // ---- reading -------------------------------------------------------------
  get(doc, key) {
    for (const l of doc.lines) {
      const m = l.match(/^  ([A-Za-z_]\w*):\s?(.*)$/);
      if (m && m[1] === key) return m[2];
    }
    return null;
  }
  lineIndex(doc, key) {
    for (let i = 0; i < doc.lines.length; i++) {
      const m = doc.lines[i].match(/^  ([A-Za-z_]\w*):/);
      if (m && m[1] === key) return i;
    }
    return -1;
  }
  ref(v) { const m = v && v.match(/fileID: (\d+)/); return m ? m[1] : null; }
  vec2(v) {
    const m = v && v.match(/x:\s*([-\d.eE+]+),\s*y:\s*([-\d.eE+]+)/);
    return m ? { x: parseFloat(m[1]), y: parseFloat(m[2]) } : null;
  }
  components(goDoc) {
    const out = []; let on = false;
    for (const l of goDoc.lines) {
      if (on) {
        const m = l.match(/^  - component: \{fileID: (\d+)\}/);
        if (m) { out.push(m[1]); continue; }
        break;
      }
      if (/^  m_Component:/.test(l)) on = true;
    }
    return out;
  }
  childrenOf(rtDoc) {
    const out = []; let on = false;
    for (const l of rtDoc.lines) {
      if (on) {
        const m = l.match(/^  - \{fileID: (\d+)\}/);
        if (m) { out.push(m[1]); continue; }
        if (/^  m_Children: \[\]/.test(l)) break;
        break;
      }
      if (/^  m_Children:/.test(l)) { if (/\[\]/.test(l)) break; on = true; }
    }
    return out;
  }

  /** Resolve "LobbyRoot/Content/Tab_Shop" from a root GameObject name. */
  find(path) {
    const parts = path.split('/');
    const roots = this.goByName.get(parts[0]);
    if (!roots || roots.length === 0) throw new Error(`no GameObject named ${parts[0]}`);
    let candidates = roots;
    for (let i = 1; i < parts.length; i++) {
      const next = [];
      for (const go of candidates) {
        const rt = this.rtByGo.get(go.fileId);
        if (!rt) continue;
        for (const cid of this.childrenOf(rt)) {
          const cd = this.byId.get(cid);
          if (!cd || cd.stripped) continue;
          const cgo = this.byId.get(this.ref(this.get(cd, 'm_GameObject')));
          if (cgo && this.get(cgo, 'm_Name') === parts[i]) next.push(cgo);
        }
      }
      candidates = next;
      if (candidates.length === 0) throw new Error(`path not found: ${path} (at "${parts[i]}")`);
    }
    if (candidates.length > 1) throw new Error(`ambiguous path ${path}: ${candidates.length} matches`);
    return candidates[0];
  }
  rt(path) {
    const go = typeof path === 'string' ? this.find(path) : path;
    const r = this.rtByGo.get(go.fileId);
    if (!r) throw new Error(`no RectTransform on ${this.get(go, 'm_Name')}`);
    return r;
  }

  // ---- writing -------------------------------------------------------------
  setScalar(doc, key, value) {
    const i = this.lineIndex(doc, key);
    if (i < 0) throw new Error(`key ${key} not found in &${doc.fileId}`);
    doc.lines[i] = `  ${key}: ${value}`;
    return this;
  }
  setVec2(doc, key, x, y) {
    const i = this.lineIndex(doc, key);
    if (i < 0) throw new Error(`key ${key} not found in &${doc.fileId}`);
    const cur = doc.lines[i];
    if (/z:/.test(cur)) throw new Error(`${key} is not a Vector2 (${cur})`);
    doc.lines[i] = `  ${key}: {x: ${fmt(x)}, y: ${fmt(y)}}`;
    return this;
  }

  newFileId() {
    for (;;) {
      // Unity ids are positive int64; keep well inside the range used by Unity.
      let s = '';
      for (let i = 0; i < 19; i++) s += (i === 0 ? 1 + Math.floor(rnd() * 8) : Math.floor(rnd() * 10));
      if (!this.byId.has(s)) return s;
    }
  }

  /** Append a component doc and register it on the GameObject. */
  addComponent(goDoc, type, props) {
    const id = this.newFileId();
    const body = [
      'MonoBehaviour:',
      '  m_ObjectHideFlags: 0',
      '  m_CorrespondingSourceObject: {fileID: 0}',
      '  m_PrefabInstance: {fileID: 0}',
      '  m_PrefabAsset: {fileID: 0}',
      `  m_GameObject: {fileID: ${goDoc.fileId}}`,
      '  m_Enabled: 1',
      '  m_EditorHideFlags: 0',
      `  m_Script: {fileID: 11500000, guid: ${SCRIPT_GUID[type]}, type: 3}`,
      '  m_Name: ',
      `  m_EditorClassIdentifier: ${CLASS_NAME[type]}`,
      ...props,
    ];
    this.docs.push({ header: `--- !u!114 &${id}`, classId: '114', fileId: id, stripped: false, lines: body });
    // register on GameObject, after the last existing component entry
    const ci = this.lineIndex(goDoc, 'm_Component');
    if (ci < 0) throw new Error('no m_Component');
    let j = ci + 1;
    while (j < goDoc.lines.length && /^  - component: \{fileID: \d+\}/.test(goDoc.lines[j])) j++;
    goDoc.lines.splice(j, 0, `  - component: {fileID: ${id}}`);
    this.byId.set(id, this.docs[this.docs.length - 1]);
    return id;
  }

  /** Create a bare RectTransform container GameObject as a child of parentRt at childIndex. */
  createContainer(name, parentRtDoc, childIndex, rect) {
    const goId = this.newFileId();
    const rtId = this.newFileId();
    const goLines = [
      'GameObject:',
      '  m_ObjectHideFlags: 0',
      '  m_CorrespondingSourceObject: {fileID: 0}',
      '  m_PrefabInstance: {fileID: 0}',
      '  m_PrefabAsset: {fileID: 0}',
      '  serializedVersion: 6',
      '  m_Component:',
      `  - component: {fileID: ${rtId}}`,
      '  m_Layer: 5',
      `  m_Name: ${name}`,
      '  m_TagString: Untagged',
      '  m_Icon: {fileID: 0}',
      '  m_NavMeshLayer: 0',
      '  m_StaticEditorFlags: 0',
      '  m_IsActive: 1',
    ];
    const rtLines = [
      'RectTransform:',
      '  m_ObjectHideFlags: 0',
      '  m_CorrespondingSourceObject: {fileID: 0}',
      '  m_PrefabInstance: {fileID: 0}',
      '  m_PrefabAsset: {fileID: 0}',
      `  m_GameObject: {fileID: ${goId}}`,
      '  serializedVersion: 2',
      '  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}',
      '  m_LocalPosition: {x: 0, y: 0, z: 0}',
      '  m_LocalScale: {x: 1, y: 1, z: 1}',
      '  m_ConstrainProportionsScale: 0',
      '  m_Children: []',
      `  m_Father: {fileID: ${parentRtDoc.fileId}}`,
      '  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}',
      `  m_AnchorMin: {x: ${fmt(rect.anchorMin.x)}, y: ${fmt(rect.anchorMin.y)}}`,
      `  m_AnchorMax: {x: ${fmt(rect.anchorMax.x)}, y: ${fmt(rect.anchorMax.y)}}`,
      `  m_AnchoredPosition: {x: ${fmt(rect.anchoredPosition.x)}, y: ${fmt(rect.anchoredPosition.y)}}`,
      `  m_SizeDelta: {x: ${fmt(rect.sizeDelta.x)}, y: ${fmt(rect.sizeDelta.y)}}`,
      `  m_Pivot: {x: ${fmt(rect.pivot.x)}, y: ${fmt(rect.pivot.y)}}`,
    ];
    const goDoc = { header: `--- !u!1 &${goId}`, classId: '1', fileId: goId, stripped: false, lines: goLines };
    const rtDoc = { header: `--- !u!224 &${rtId}`, classId: '224', fileId: rtId, stripped: false, lines: rtLines };
    this.docs.push(goDoc, rtDoc);
    this.byId.set(goId, goDoc);
    this.byId.set(rtId, rtDoc);
    this.rtByGo.set(goId, rtDoc);
    if (!this.goByName.has(name)) this.goByName.set(name, []);
    this.goByName.get(name).push(goDoc);
    this.insertChild(parentRtDoc, rtId, childIndex);
    return { goDoc, rtDoc };
  }

  insertChild(parentRtDoc, childRtId, index) {
    const ci = this.lineIndex(parentRtDoc, 'm_Children');
    if (ci < 0) throw new Error('no m_Children');
    if (/m_Children: \[\]/.test(parentRtDoc.lines[ci])) {
      parentRtDoc.lines[ci] = '  m_Children:';
      parentRtDoc.lines.splice(ci + 1, 0, `  - {fileID: ${childRtId}}`);
      return;
    }
    let start = ci + 1, end = start;
    while (end < parentRtDoc.lines.length && /^  - \{fileID: \d+\}/.test(parentRtDoc.lines[end])) end++;
    const at = index === -1 ? end : start + index;
    if (at < start || at > end) throw new Error(`child index ${index} out of range`);
    parentRtDoc.lines.splice(at, 0, `  - {fileID: ${childRtId}}`);
  }

  removeChild(parentRtDoc, childRtId) {
    const ci = this.lineIndex(parentRtDoc, 'm_Children');
    let start = ci + 1, end = start;
    while (end < parentRtDoc.lines.length && /^  - \{fileID: \d+\}/.test(parentRtDoc.lines[end])) end++;
    for (let i = start; i < end; i++) {
      if (parentRtDoc.lines[i] === `  - {fileID: ${childRtId}}`) {
        parentRtDoc.lines.splice(i, 1);
        if (end - start === 1) parentRtDoc.lines[ci] = '  m_Children: []';
        return;
      }
    }
    throw new Error(`child ${childRtId} not found under &${parentRtDoc.fileId}`);
  }

  reparent(childRtDoc, newParentRtDoc, index) {
    const oldParent = this.byId.get(this.ref(this.get(childRtDoc, 'm_Father')));
    this.removeChild(oldParent, childRtDoc.fileId);
    this.setScalar(childRtDoc, 'm_Father', `{fileID: ${newParentRtDoc.fileId}}`);
    this.insertChild(newParentRtDoc, childRtDoc.fileId, index);
  }

  // ---- nested prefab instance overrides -----------------------------------
  /** For a `stripped` doc, the PrefabInstance doc that owns it. */
  instanceOf(strippedDoc) {
    const pi = this.ref(this.get(strippedDoc, 'm_PrefabInstance'));
    const d = this.byId.get(pi);
    if (!d || d.classId !== '1001') throw new Error(`no PrefabInstance for &${strippedDoc.fileId}`);
    return d;
  }
  /** {fileID, guid} identifying the object inside the source prefab. */
  sourceKey(strippedDoc) {
    const raw = this.get(strippedDoc, 'm_CorrespondingSourceObject');
    const m = raw && raw.match(/fileID: (\d+), guid: ([0-9a-f]+)/);
    if (!m) throw new Error(`no m_CorrespondingSourceObject on &${strippedDoc.fileId}`);
    return { fileId: m[1], guid: m[2] };
  }
  /** Index of the `value:` line for a modification entry, or -1. */
  _modValueLine(instDoc, key, propertyPath) {
    let curMatch = false;
    for (let i = 0; i < instDoc.lines.length; i++) {
      const t = instDoc.lines[i].match(/^    - target: \{fileID: (\d+), guid: ([0-9a-f]+)/);
      if (t) { curMatch = (t[1] === key.fileId && t[2] === key.guid); continue; }
      if (!curMatch) continue;
      const pp = instDoc.lines[i].match(/^      propertyPath: (.*)$/);
      if (pp && pp[1].trim() === propertyPath) {
        const v = instDoc.lines[i + 1] && instDoc.lines[i + 1].match(/^      value: /);
        if (!v) throw new Error(`malformed modification for ${propertyPath}`);
        return i + 1;
      }
    }
    return -1;
  }
  getInstanceProp(strippedDoc, propertyPath) {
    const inst = this.instanceOf(strippedDoc);
    const i = this._modValueLine(inst, this.sourceKey(strippedDoc), propertyPath);
    if (i < 0) return null;
    return inst.lines[i].replace(/^      value: /, '');
  }
  setInstanceProp(strippedDoc, propertyPath, value) {
    const inst = this.instanceOf(strippedDoc);
    const i = this._modValueLine(inst, this.sourceKey(strippedDoc), propertyPath);
    if (i < 0) throw new Error(`no existing override for ${propertyPath} on &${strippedDoc.fileId}`);
    inst.lines[i] = `      value: ${fmt(value)}`;
    return this;
  }
  /** Read a rect property of a normal OR stripped transform as {x,y}. */
  rectVec2(rtDoc, key) {
    if (!rtDoc.stripped) {
      const v = this.vec2(this.get(rtDoc, key));
      if (!v) throw new Error(`${key} missing on &${rtDoc.fileId}`);
      return v;
    }
    const x = this.getInstanceProp(rtDoc, `${key}.x`);
    const y = this.getInstanceProp(rtDoc, `${key}.y`);
    if (x === null || y === null) throw new Error(`${key} not overridden on stripped &${rtDoc.fileId}`);
    return { x: parseFloat(x), y: parseFloat(y) };
  }
  setRectVec2(rtDoc, key, x, y) {
    if (!rtDoc.stripped) return this.setVec2(rtDoc, key, x, y);
    this.setInstanceProp(rtDoc, `${key}.x`, x);
    this.setInstanceProp(rtDoc, `${key}.y`, y);
    return this;
  }

  save(outPath) {
    const parts = [...this.preamble];
    for (const d of this.docs) { parts.push(d.header); parts.push(...d.lines); }
    const text = parts.join('\n') + '\n';
    const tmp = (outPath || this.path) + '.tmp';
    fs.writeFileSync(tmp, text, 'utf8');
    fs.renameSync(tmp, outPath || this.path);
  }
}

function fmt(n) {
  if (typeof n !== 'number') return String(n);
  if (Number.isInteger(n)) return String(n);
  return String(parseFloat(n.toFixed(5)));
}

// Deterministic RNG so reruns produce identical fileIDs.
let _seed = 0x5eed1234;
function rnd() { _seed = (_seed * 1103515245 + 12345) & 0x7fffffff; return _seed / 0x7fffffff; }

const LE = (o = {}) => [
  `  m_IgnoreLayout: ${o.ignoreLayout ?? 0}`,
  `  m_MinWidth: ${o.minWidth ?? -1}`,
  `  m_MinHeight: ${o.minHeight ?? -1}`,
  `  m_PreferredWidth: ${o.preferredWidth ?? -1}`,
  `  m_PreferredHeight: ${o.preferredHeight ?? -1}`,
  `  m_FlexibleWidth: ${o.flexibleWidth ?? -1}`,
  `  m_FlexibleHeight: ${o.flexibleHeight ?? -1}`,
  `  m_LayoutPriority: ${o.layoutPriority ?? 1}`,
];
const HVLG = (o = {}) => [
  '  m_Padding:',
  `    m_Left: ${o.left ?? 0}`,
  `    m_Right: ${o.right ?? 0}`,
  `    m_Top: ${o.top ?? 0}`,
  `    m_Bottom: ${o.bottom ?? 0}`,
  `  m_ChildAlignment: ${o.childAlignment ?? 0}`,
  `  m_Spacing: ${o.spacing ?? 0}`,
  `  m_ChildForceExpandWidth: ${o.forceExpandWidth ?? 0}`,
  `  m_ChildForceExpandHeight: ${o.forceExpandHeight ?? 0}`,
  `  m_ChildControlWidth: ${o.controlWidth ?? 1}`,
  `  m_ChildControlHeight: ${o.controlHeight ?? 1}`,
  `  m_ChildScaleWidth: ${o.scaleWidth ?? 0}`,
  `  m_ChildScaleHeight: ${o.scaleHeight ?? 0}`,
  `  m_ReverseArrangement: ${o.reverse ?? 0}`,
];
const CSF = (o = {}) => [
  `  m_HorizontalFit: ${o.horizontal ?? 0}`,
  `  m_VerticalFit: ${o.vertical ?? 0}`,
];

module.exports = { Prefab, LE, HVLG, CSF, fmt };
