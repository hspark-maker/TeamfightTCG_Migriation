// Compare resolved geometry of two prefab revisions, keyed on fileID (stable
// across reparenting), at several canvas sizes.
const { Sim } = require('./sim.js');

const A = process.argv[2], B = process.argv[3];
const EPS = 0.01;
const SIZES = [[1080, 1920], [1080, 2340], [1080, 2400]];

let anyUnexpected = false;
for (const [w, h] of SIZES) {
  const sa = new Sim(A), sb = new Sim(B);
  const a = sa.snapshot(w, h), b = sb.snapshot(w, h);
  const changed = [], added = [], removed = [];
  for (const [id, va] of a) {
    if (!b.has(id)) { removed.push(va.path); continue; }
    const vb = b.get(id);
    const d = ['x', 'y', 'w', 'h'].filter(k => Math.abs(va.rect[k] - vb.rect[k]) > EPS);
    if (d.length) changed.push({ id, pa: va.path, pb: vb.path, a: va.rect, b: vb.rect, d });
  }
  for (const [id, vb] of b) if (!a.has(id)) added.push(vb.path);

  console.log(`\n================ canvas ${w}x${h} ================`);
  console.log(`nodes: before=${a.size} after=${b.size} | changed=${changed.length} added=${added.length} removed=${removed.length}`);
  if (added.length) console.log('  ADDED:   ' + added.join(', '));
  if (removed.length) { console.log('  REMOVED: ' + removed.join(', ')); anyUnexpected = true; }
  const fmtR = (r) => `x=${r.x.toFixed(1)} y=${r.y.toFixed(1)} w=${r.w.toFixed(1)} h=${r.h.toFixed(1)}`;
  for (const c of changed) {
    const moved = c.pa !== c.pb ? `  (path ${c.pa} -> ${c.pb})` : '';
    console.log(`  CHANGED [${c.d.join(',')}] ${c.pa}${moved}`);
    console.log(`      before ${fmtR(c.a)}`);
    console.log(`      after  ${fmtR(c.b)}`);
  }
  if (process.env.SHOW_UNSIM) {
    const u = [...sb.unsimulated].filter(x => !/nested prefab rect/.test(x));
    if (u.length) console.log('  [not fully simulated] ' + u.slice(0, 12).join(' | '));
  }
}
process.exit(anyUnexpected ? 1 : 0);
