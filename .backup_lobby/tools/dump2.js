// Unity prefab YAML -> hierarchy dump v2 (resolves nested PrefabInstances, prints layout settings)
const fs = require('fs');
const file = process.argv[2];
const text = fs.readFileSync(file, 'utf8');
const lines = text.split(/\r?\n/);

const docs = [];
let cur = null;
for (const line of lines) {
  const m = line.match(/^--- !u!(\d+) &(\d+)(.*)$/);
  if (m) { cur = { classId: m[1], fileId: m[2], stripped: /stripped/.test(m[3]), body: [] }; docs.push(cur); continue; }
  if (cur) cur.body.push(line);
}
const byId = new Map(docs.map(d => [d.fileId, d]));
const ind = l => l.match(/^ */)[0].length;
function prop(doc, name) {
  for (const l of doc.body) { const m = l.match(/^  (\w+):\s*(.*)$/); if (m && m[1] === name) return m[2]; }
  return null;
}
function subBlock(doc, name) {
  const out = []; let on = false;
  for (const l of doc.body) {
    if (on) { if (ind(l) > 2 || /^  - /.test(l) || l.trim() === '') { out.push(l); continue; } break; }
    const m = l.match(/^  (\w+):/); if (m && m[1] === name) on = true;
  }
  return out;
}

// ---- guid -> asset path (project + ugui pkg)
const guidPath = new Map();
const scriptGuidName = new Map();
function scan(dir) {
  let ents = []; try { ents = fs.readdirSync(dir, { withFileTypes: true }); } catch (e) { return; }
  for (const e of ents) {
    const p = dir + '/' + e.name;
    if (e.isDirectory()) { scan(p); continue; }
    if (!e.name.endsWith('.meta')) continue;
    let t; try { t = fs.readFileSync(p, 'utf8'); } catch (err) { continue; }
    const m = t.match(/guid:\s*([0-9a-f]+)/); if (!m) continue;
    guidPath.set(m[1], p.replace(/\.meta$/, ''));
    if (e.name.endsWith('.cs.meta')) scriptGuidName.set(m[1], e.name.replace(/\.cs\.meta$/, ''));
  }
}
scan('C:/mgJeon/TeamfightTCG_Migriation/Assets');
scan('C:/mgJeon/TeamfightTCG_Migriation/Library/PackageCache/com.unity.ugui@bb329a87fcdc');

// ---- PrefabInstance: map stripped transform fileId -> {name, srcPath}
const instByRootTransform = new Map(); // stripped transform id -> info
for (const d of docs) {
  if (d.classId !== '1001') continue;
  const src = (prop(d, 'm_SourcePrefab') || (subBlock(d, 'm_Modification').find(l => /m_SourcePrefab/.test(l)) || '')).match?.(/guid: ([0-9a-f]+)/);
  let srcGuid = null;
  for (const l of d.body) { const m = l.match(/m_SourcePrefab:.*guid: ([0-9a-f]+)/); if (m) { srcGuid = m[1]; break; } }
  let name = null;
  const mods = subBlock(d, 'm_Modification');
  for (let i = 0; i < mods.length; i++) {
    if (/propertyPath: m_Name/.test(mods[i])) {
      for (let j = i; j < Math.min(i + 4, mods.length); j++) {
        const m = mods[j].match(/value:\s*(.*)$/); if (m) { name = m[1].trim(); break; }
      }
      break;
    }
  }
  // collect all modifications for reference
  instByRootTransform.set(d.fileId, {
    id: d.fileId, name, srcGuid,
    srcPath: srcGuid ? (guidPath.get(srcGuid) || srcGuid) : '?',
    mods: mods.join('\n'),
  });
}
// stripped transforms carry m_PrefabInstance / m_CorrespondingSourceObject
const strippedInfo = new Map();
for (const d of docs) {
  if (!d.stripped) continue;
  let pi = null;
  for (const l of d.body) { const m = l.match(/m_PrefabInstance:.*fileID: (\d+)/); if (m) { pi = m[1]; break; } }
  strippedInfo.set(d.fileId, { classId: d.classId, inst: pi });
}

// ---- GameObjects & transforms
const gos = new Map();
for (const d of docs) {
  if (d.classId !== '1' || d.stripped) continue;
  const comps = subBlock(d, 'm_Component').map(l => (l.match(/fileID: (\d+)/) || [])[1]).filter(Boolean);
  gos.set(d.fileId, { name: prop(d, 'm_Name'), active: prop(d, 'm_IsActive'), comps });
}
const rt = new Map();
for (const d of docs) {
  if ((d.classId !== '224' && d.classId !== '4') || d.stripped) continue;
  const go = (prop(d, 'm_GameObject') || '').match(/fileID: (\d+)/);
  const father = (prop(d, 'm_Father') || '').match(/fileID: (\d+)/);
  rt.set(d.fileId, {
    id: d.fileId, go: go ? go[1] : null, father: father ? father[1] : '0',
    children: subBlock(d, 'm_Children').map(l => (l.match(/fileID: (\d+)/) || [])[1]).filter(Boolean),
    anchorMin: prop(d, 'm_AnchorMin'), anchorMax: prop(d, 'm_AnchorMax'),
    anchoredPosition: prop(d, 'm_AnchoredPosition'), sizeDelta: prop(d, 'm_SizeDelta'),
    pivot: prop(d, 'm_Pivot'), scale: prop(d, 'm_LocalScale'),
  });
}
const CLASS = { '223': 'Canvas', '225': 'CanvasGroup', '222': null, '224': null, '4': null, '1': 'GameObject' };
function v2(v) { const m = v && v.match(/x:\s*([-\d.eE]+),\s*y:\s*([-\d.eE]+)/); return m ? `(${+(+m[1]).toFixed(2)},${+(+m[2]).toFixed(2)})` : '?'; }

const LAYOUT = new Set(['VerticalLayoutGroup', 'HorizontalLayoutGroup', 'GridLayoutGroup', 'ContentSizeFitter', 'LayoutElement', 'AspectRatioFitter', 'CanvasScaler']);
function layoutDetail(d, nm) {
  const keys = {
    VerticalLayoutGroup: ['m_Padding', 'm_Spacing', 'm_ChildAlignment', 'm_ChildForceExpandWidth', 'm_ChildForceExpandHeight', 'm_ChildControlWidth', 'm_ChildControlHeight', 'm_ChildScaleWidth', 'm_ChildScaleHeight', 'm_ReverseArrangement'],
    HorizontalLayoutGroup: ['m_Padding', 'm_Spacing', 'm_ChildAlignment', 'm_ChildForceExpandWidth', 'm_ChildForceExpandHeight', 'm_ChildControlWidth', 'm_ChildControlHeight'],
    GridLayoutGroup: ['m_Padding', 'm_CellSize', 'm_Spacing', 'm_StartCorner', 'm_StartAxis', 'm_ChildAlignment', 'm_Constraint', 'm_ConstraintCount'],
    ContentSizeFitter: ['m_HorizontalFit', 'm_VerticalFit'],
    LayoutElement: ['m_IgnoreLayout', 'm_MinWidth', 'm_MinHeight', 'm_PreferredWidth', 'm_PreferredHeight', 'm_FlexibleWidth', 'm_FlexibleHeight', 'm_LayoutPriority'],
    AspectRatioFitter: ['m_AspectMode', 'm_AspectRatio'],
    CanvasScaler: ['m_UiScaleMode', 'm_ReferenceResolution', 'm_ScreenMatchMode', 'm_MatchWidthOrHeight'],
  }[nm];
  if (!keys) return '';
  const parts = [];
  for (const k of keys) {
    let v = prop(d, k);
    if (v === null) {
      const b = subBlock(d, k);
      if (b.length) v = b.map(x => x.trim()).join(' ');
    }
    if (v !== null && v !== undefined && v !== '') parts.push(`${k.replace(/^m_/, '')}=${v.trim()}`);
  }
  return parts.length ? `{${parts.join(' ')}}` : '';
}

function compDesc(id) {
  const d = byId.get(id); if (!d) return `?${id}`;
  if (d.classId === '224' || d.classId === '4' || d.classId === '222') return null;
  if (d.classId === '114') {
    let g = null; for (const l of d.body) { const m = l.match(/m_Script:.*guid: ([0-9a-f]+)/); if (m) { g = m[1]; break; } }
    const nm = scriptGuidName.get(g) || g;
    const det = LAYOUT.has(nm) ? layoutDetail(d, nm) : '';
    return nm + det;
  }
  return CLASS[d.classId] === null ? null : (CLASS[d.classId] || `class${d.classId}`);
}

const out = [];
function walk(id, depth) {
  const pad = '  '.repeat(depth);
  if (strippedInfo.has(id)) {
    const s = strippedInfo.get(id);
    const inst = instByRootTransform.get(s.inst);
    const nm = inst ? (inst.name || '(prefab)') : '?';
    const src = inst ? inst.srcPath.replace(/^.*Assets\//, 'Assets/') : '?';
    out.push(`${pad}- ${nm}  <<PREFAB ${src}>>`);
    return;
  }
  const r = rt.get(id);
  if (!r) { out.push(`${pad}- <missing ${id}>`); return; }
  const g = gos.get(r.go);
  const comps = (g ? g.comps : []).map(compDesc).filter(Boolean);
  const sc = v2(r.scale);
  out.push(`${pad}- ${g ? g.name : '???'}${g && g.active === '0' ? ' [OFF]' : ''}  aMin${v2(r.anchorMin)} aMax${v2(r.anchorMax)} pos${v2(r.anchoredPosition)} size${v2(r.sizeDelta)} piv${v2(r.pivot)}${sc !== '(1,1)' ? ' scl' + sc : ''}  [${comps.join(' | ')}]`);
  for (const c of r.children) walk(c, depth + 1);
}
const roots = [...rt.values()].filter(r => r.father === '0' || !rt.has(r.father) && !strippedInfo.has(r.father));
for (const r of roots) walk(r.id, 0);
console.log(out.join('\n'));
