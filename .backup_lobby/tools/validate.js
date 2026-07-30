// Structural integrity checks on a prefab file: no dangling refs, no duplicate
// ids, parent/child symmetry, component ownership symmetry.
const { Prefab } = require('./prefablib.js');
const p = new Prefab(process.argv[2]);
const errs = [];
const warn = [];

// 1. duplicate fileIDs
const seen = new Set();
for (const d of p.docs) {
  if (seen.has(d.fileId)) errs.push(`duplicate fileID &${d.fileId}`);
  seen.add(d.fileId);
}

// 2. every {fileID: N} that is not 0 and has no guid must resolve to a doc
for (const d of p.docs) {
  for (let i = 0; i < d.lines.length; i++) {
    const l = d.lines[i];
    const re = /\{fileID: (\d+)(, guid: [0-9a-f]+)?/g;
    let m;
    while ((m = re.exec(l)) !== null) {
      if (m[1] === '0' || m[2]) continue;           // null ref or external asset
      if (m[1] === '11500000') continue;            // MonoScript
      if (!p.byId.has(m[1])) errs.push(`dangling ref &${m[1]} in &${d.fileId} (${d.classId}): ${l.trim()}`);
    }
  }
}

// 3. GameObject <-> component symmetry
for (const d of p.docs) {
  if (d.classId !== '1' || d.stripped) continue;
  for (const cid of p.components(d)) {
    const cd = p.byId.get(cid);
    if (!cd) { errs.push(`GO &${d.fileId} lists missing component &${cid}`); continue; }
    if (cd.stripped) continue;
    const owner = p.ref(p.get(cd, 'm_GameObject'));
    if (owner !== d.fileId) errs.push(`component &${cid} m_GameObject=&${owner} but listed on GO &${d.fileId}`);
  }
}
// components that claim a GameObject which does not list them
for (const d of p.docs) {
  if (d.stripped || d.classId === '1' || d.classId === '1001') continue;
  const ownerId = p.ref(p.get(d, 'm_GameObject'));
  if (!ownerId || ownerId === '0') continue;
  const owner = p.byId.get(ownerId);
  // GameObjects living inside a nested prefab instance are not documents here.
  if (!owner) continue;
  if (owner.stripped) continue;
  if (!p.components(owner).includes(d.fileId))
    errs.push(`&${d.fileId} (class ${d.classId}) claims GO &${ownerId} but is not in its m_Component`);
}

// 4. transform parent/child symmetry
for (const d of p.docs) {
  if ((d.classId !== '224' && d.classId !== '4')) continue;
  const kids = d.stripped ? [] : p.childrenOf(d);
  for (const k of kids) {
    const kd = p.byId.get(k);
    if (!kd) { errs.push(`&${d.fileId} lists missing child &${k}`); continue; }
    // A stripped transform carries no m_Father; its parent link is implicit.
    if (kd.stripped) continue;
    const f = p.ref(p.get(kd, 'm_Father'));
    if (f !== d.fileId) errs.push(`child &${k} m_Father=&${f} but listed under &${d.fileId}`);
  }
  const father = p.ref(p.get(d, 'm_Father'));
  if (father && father !== '0') {
    const fd = p.byId.get(father);
    if (!fd) { errs.push(`&${d.fileId} has missing father &${father}`); continue; }
    if (!fd.stripped && !p.childrenOf(fd).includes(d.fileId))
      errs.push(`&${d.fileId} claims father &${father} which does not list it`);
  }
}

// 5. every non-stripped GameObject must have exactly one transform
for (const d of p.docs) {
  if (d.classId !== '1' || d.stripped) continue;
  const ts = p.components(d).filter(c => { const cd = p.byId.get(c); return cd && (cd.classId === '224' || cd.classId === '4'); });
  if (ts.length !== 1) errs.push(`GO &${d.fileId} (${p.get(d, 'm_Name')}) has ${ts.length} transforms`);
}

// 6. MonoBehaviour docs must carry a script ref
for (const d of p.docs) {
  if (d.classId !== '114' || d.stripped) continue;
  if (!d.lines.some(l => /m_Script: \{fileID: 11500000, guid: [0-9a-f]+, type: 3\}/.test(l)))
    errs.push(`MonoBehaviour &${d.fileId} has no valid m_Script`);
}

// 7. indentation sanity: no tabs, keys at 2 spaces
for (const d of p.docs) {
  for (const l of d.lines) {
    if (l.includes('\t')) errs.push(`tab character in &${d.fileId}`);
  }
}

console.log(`docs=${p.docs.length}  errors=${errs.length}  warnings=${warn.length}`);
for (const e of errs.slice(0, 40)) console.log('  ERROR ' + e);
for (const w of warn.slice(0, 20)) console.log('  WARN  ' + w);
process.exit(errs.length ? 1 : 0);
