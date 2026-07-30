// Map LobbyScene overrides of the LobbyCanvas prefab instance -> object names
const fs = require('fs');
const PREFAB = 'C:/mgJeon/TeamfightTCG_Migriation/Assets/Assets/Prefabs/UI/LobbyCanvas.prefab';
const SCENE = 'C:/mgJeon/TeamfightTCG_Migriation/Assets/Scenes/LobbyScene.unity';
const GUID = '1c1712ad39fe2fb49b161eae277c37f7';

function parse(path) {
  const docs = []; let cur = null;
  for (const line of fs.readFileSync(path, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^--- !u!(\d+) &(\d+)(.*)$/);
    if (m) { cur = { classId: m[1], fileId: m[2], stripped: /stripped/.test(m[3]), body: [] }; docs.push(cur); continue; }
    if (cur) cur.body.push(line);
  }
  return docs;
}
const p = parse(PREFAB);
const prop = (d, n) => { for (const l of d.body) { const m = l.match(/^  (\w+):\s*(.*)$/); if (m && m[1] === n) return m[2]; } return null; };
const sub = (d, n) => { const o = []; let on = false; for (const l of d.body) { if (on) { if (l.match(/^ */)[0].length > 2 || /^  - /.test(l) || !l.trim()) { o.push(l); continue; } break; } const m = l.match(/^  (\w+):/); if (m && m[1] === n) on = true; } return o; };

// fileId -> label
const label = new Map();
const goName = new Map();
for (const d of p) if (d.classId === '1' && !d.stripped) goName.set(d.fileId, prop(d, 'm_Name'));
for (const d of p) {
  if (d.stripped) { label.set(d.fileId, `(stripped ${d.classId})`); continue; }
  if (d.classId === '1') { label.set(d.fileId, `GO:${prop(d, 'm_Name')}`); continue; }
  const go = (prop(d, 'm_GameObject') || '').match(/fileID: (\d+)/);
  const owner = go ? goName.get(go[1]) : null;
  const kind = { '224': 'RectTransform', '114': 'MonoBehaviour', '222': 'CanvasRenderer', '223': 'Canvas', '225': 'CanvasGroup', '1001': 'PrefabInstance' }[d.classId] || d.classId;
  label.set(d.fileId, `${owner || '?'} / ${kind}`);
}
// also PrefabInstance docs inside the prefab: give them their overridden m_Name
for (const d of p) {
  if (d.classId !== '1001') continue;
  let nm = null;
  const body = d.body;
  for (let i = 0; i < body.length; i++) if (/propertyPath: m_Name/.test(body[i])) { for (let j = i; j < i + 4 && j < body.length; j++) { const m = body[j].match(/value:\s*(.*)$/); if (m) { nm = m[1].trim(); break; } } break; }
  label.set(d.fileId, `PrefabInstance:${nm || '?'}`);
}

const s = parse(SCENE);
const inst = s.find(d => d.classId === '1001' && d.body.some(l => l.includes('m_SourcePrefab') && l.includes(GUID)));
if (!inst) { console.log('instance not found'); process.exit(0); }
const mods = sub(inst, 'm_Modification');
const rows = [];
let curTarget = null;
for (let i = 0; i < mods.length; i++) {
  const t = mods[i].match(/target: \{fileID: (\d+), guid: ([0-9a-f]+)/);
  if (t) { curTarget = t[2] === GUID ? (label.get(t[1]) || `?${t[1]}`) : `OTHER(${t[2].slice(0, 6)}) ${t[1]}`; continue; }
  const pp = mods[i].match(/propertyPath: (.*)$/);
  if (pp) {
    let val = '';
    for (let j = i + 1; j < Math.min(i + 3, mods.length); j++) {
      const v = mods[j].match(/^\s+value:\s*(.*)$/); const o = mods[j].match(/^\s+objectReference:\s*(.*)$/);
      if (v) val = v[1];
      else if (o && !val) val = 'objRef ' + o[1];
    }
    rows.push([curTarget, pp[1].trim(), val]);
  }
}
const byT = new Map();
for (const [t, k, v] of rows) { if (!byT.has(t)) byT.set(t, []); byT.get(t).push(`${k} = ${v}`); }
for (const [t, list] of byT) console.log(`\n### ${t}\n   ${list.join('\n   ')}`);
