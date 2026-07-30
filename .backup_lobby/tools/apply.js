// LobbyCanvas.prefab structural restructure.
// Every geometric change below is derived so the resolved rect is unchanged;
// the one intentional visual change (Stage 1) is documented in the report.
const { Prefab, LE, HVLG, CSF } = require('./prefablib.js');

const SRC = process.argv[2];
const OUT = process.argv[3];
const STAGES = (process.argv[4] || '1,2,3').split(',');

const p = new Prefab(SRC);
const log = [];
const say = (s) => { log.push(s); console.log(s); };

const GUID = {
  Image: 'fe87c0e1cc204ed48ad3b37840f39efc',
  VerticalLayoutGroup: '59f8146938fff824cb5fd77236b75775',
  LayoutElement: '306cc8c2b49d7114eaa3623786fc2126',
};
function comp(goDoc, guid) {
  for (const id of p.components(goDoc)) {
    const d = p.byId.get(id);
    if (!d) continue;
    for (const l of d.lines) if (l.includes('m_Script:') && l.includes(guid)) return d;
  }
  return null;
}
function hasComp(goDoc, guid) { return comp(goDoc, guid) !== null; }

// =============================================================== STAGE 1
if (STAGES.includes('1')) {
  say('--- Stage 1: LobbyRoot 3-split -> LayoutElement driven');
  const root = p.find('LobbyRoot');
  const vlg = comp(root, GUID.VerticalLayoutGroup);
  if (!vlg) throw new Error('LobbyRoot VerticalLayoutGroup missing');

  // Sanity: the arithmetic below assumes these exact starting values.
  const expect = {
    m_ChildControlWidth: '1', m_ChildControlHeight: '0',
    m_ChildForceExpandWidth: '1', m_ChildForceExpandHeight: '0',
    m_Spacing: '0', m_ChildAlignment: '0',
  };
  for (const [k, v] of Object.entries(expect)) {
    const got = p.get(vlg, k);
    if (got !== v) throw new Error(`LobbyRoot VLG ${k}: expected ${v}, got ${got}`);
  }
  p.setScalar(vlg, 'm_ChildControlHeight', 1);
  say('  VLG.m_ChildControlHeight: 0 -> 1');

  // TopBar 150 / Content flexible / BottomBar 220  (was 150 + 1900 + 220 = 2270 fixed)
  const plan = [
    ['LobbyRoot/TopBar', { preferredHeight: 150 }, 150],
    ['LobbyRoot/Content', { preferredHeight: 0, flexibleHeight: 1 }, 1900],
    ['LobbyRoot/BottomBar', { preferredHeight: 220 }, 220],
  ];
  for (const [path, opts, wasHeight] of plan) {
    const go = p.find(path);
    if (hasComp(go, GUID.LayoutElement)) throw new Error(`${path} already has a LayoutElement`);
    const rt = p.rt(go);
    const h = p.vec2(p.get(rt, 'm_SizeDelta')).y;
    if (h !== wasHeight) throw new Error(`${path} sizeDelta.y: expected ${wasHeight}, got ${h}`);
    const id = p.addComponent(go, 'LayoutElement', LE(opts));
    say(`  + LayoutElement on ${path.split('/').pop()} (&${id}) ${JSON.stringify(opts)}`);
  }
  // Content's stale 1900 is now a driven property. Left in place deliberately as a
  // degrade-to-current fallback; Unity rewrites it on the next prefab save.
  say('  note: Content.m_SizeDelta.y left at 1900 (driven; fallback if rebuild fails)');
}

// =============================================================== STAGE 2
if (STAGES.includes('2')) {
  say('--- Stage 2: Shop tab scroll list -> layout groups');
  const content = p.find('LobbyRoot/Content/Tab_Shop/ShopContent/ShopList/Viewport/Content');
  const crt = p.rt(content);
  const csize = p.vec2(p.get(crt, 'm_SizeDelta'));
  if (csize.y !== 1398) throw new Error(`Shop Content height expected 1398, got ${csize.y}`);

  const rows = p.childrenOf(crt).map(id => {
    const rtDoc = p.byId.get(id);
    const go = p.byId.get(p.ref(p.get(rtDoc, 'm_GameObject')));
    return { go, rt: rtDoc, name: p.get(go, 'm_Name') };
  });
  if (rows.length !== 4) throw new Error(`expected 4 rows, got ${rows.length}`);

  // Verify the hand-placed positions form an exact 330-high / 26-spacing stack.
  const SPACING = 26, ROW_H = 330;
  rows.forEach((r, i) => {
    const pos = p.vec2(p.get(r.rt, 'm_AnchoredPosition'));
    const sz = p.vec2(p.get(r.rt, 'm_SizeDelta'));
    const expectY = -(ROW_H + SPACING) * i;
    if (sz.y !== ROW_H) throw new Error(`${r.name} height ${sz.y} != ${ROW_H}`);
    if (pos.y !== expectY) throw new Error(`${r.name} y ${pos.y} != ${expectY}`);
  });
  if (ROW_H * rows.length + SPACING * (rows.length - 1) !== csize.y)
    throw new Error('row stack does not sum to Content height');
  say(`  verified: 4 rows x ${ROW_H} + 3 x ${SPACING} spacing = ${csize.y} (exact)`);

  p.addComponent(content, 'VerticalLayoutGroup', HVLG({
    spacing: SPACING, childAlignment: 0,
    controlWidth: 1, controlHeight: 1, forceExpandWidth: 1, forceExpandHeight: 0,
  }));
  p.addComponent(content, 'ContentSizeFitter', CSF({ horizontal: 0, vertical: 2 }));
  say('  + VerticalLayoutGroup(spacing=26) + ContentSizeFitter(Vertical=PreferredSize) on Content');

  for (const r of rows) {
    p.addComponent(r.go, 'LayoutElement', LE({ preferredHeight: ROW_H }));
  }
  say(`  + LayoutElement(preferredHeight=${ROW_H}) on ${rows.map(r => r.name).join(', ')}`);

  // --- per row: wrap Cell_0..2 into a Cells container driven by a HorizontalLayoutGroup
  const CELL_W = 205, CELL_H = 215, CELL_GAP = 25;
  for (const r of rows) {
    const kids = p.childrenOf(r.rt).map(id => {
      const rtDoc = p.byId.get(id);
      const go = p.byId.get(p.ref(p.get(rtDoc, 'm_GameObject')));
      return { id, go, rt: rtDoc, name: go ? p.get(go, 'm_Name') : '(stripped)' };
    });
    const cells = kids.filter(k => /^Cell_\d$/.test(k.name));
    if (cells.length !== 3) throw new Error(`${r.name}: expected 3 cells, got ${cells.length}`);

    // Confirm the hand-placed cell geometry, then derive the container rect from it.
    const geo = cells.map(c => ({
      c, pos: p.vec2(p.get(c.rt, 'm_AnchoredPosition')), sz: p.vec2(p.get(c.rt, 'm_SizeDelta')),
      aMin: p.vec2(p.get(c.rt, 'm_AnchorMin')), aMax: p.vec2(p.get(c.rt, 'm_AnchorMax')),
      piv: p.vec2(p.get(c.rt, 'm_Pivot')),
    }));
    geo.forEach((g, i) => {
      if (g.sz.x !== CELL_W || g.sz.y !== CELL_H) throw new Error(`${r.name}/${g.c.name} size ${g.sz.x}x${g.sz.y}`);
      if (g.aMin.x !== 0 || g.aMin.y !== 0.5 || g.aMax.x !== 0 || g.aMax.y !== 0.5)
        throw new Error(`${r.name}/${g.c.name} unexpected anchors`);
      if (g.piv.x !== 0.5 || g.piv.y !== 0.5) throw new Error(`${r.name}/${g.c.name} unexpected pivot`);
      const expectX = 130 + i * (CELL_W + CELL_GAP);
      if (g.pos.x !== expectX) throw new Error(`${r.name}/${g.c.name} x ${g.pos.x} != ${expectX}`);
      if (g.pos.y !== -8) throw new Error(`${r.name}/${g.c.name} y ${g.pos.y} != -8`);
    });

    // Container spans the cells exactly: left edge of Cell_0 .. right edge of Cell_2.
    const left = geo[0].pos.x - CELL_W / 2;                       // 27.5
    const width = CELL_W * 3 + CELL_GAP * 2;                        // 665
    const cx = left + width / 2;                                    // 360
    const { goDoc: cellsGo, rtDoc: cellsRt } = p.createContainer('Cells', r.rt, 1, {
      anchorMin: { x: 0, y: 0.5 }, anchorMax: { x: 0, y: 0.5 },
      anchoredPosition: { x: cx, y: -8 },
      sizeDelta: { x: width, y: CELL_H },
      pivot: { x: 0.5, y: 0.5 },
    });
    p.addComponent(cellsGo, 'HorizontalLayoutGroup', HVLG({
      spacing: CELL_GAP, childAlignment: 4, // MiddleCenter
      controlWidth: 1, controlHeight: 1, forceExpandWidth: 0, forceExpandHeight: 0,
    }));
    for (const c of cells) {
      p.reparent(c.rt, cellsRt, -1);
      // inside an HLG the anchors/position are driven; size comes from LayoutElement
      p.addComponent(c.go, 'LayoutElement', LE({ preferredWidth: CELL_W, preferredHeight: CELL_H }));
    }
    say(`  ${r.name}: + Cells container x=${cx} w=${width} h=${CELL_H} + HLG(spacing=${CELL_GAP}), 3 cells reparented`);
  }
}

// =============================================================== STAGE 3
if (STAGES.includes('3')) {
  say('--- Stage 3: RankInfo container now encloses its children');
  const info = p.find('LobbyRoot/Content/Tab_Match/MatchContent/RankInfo');
  const irt = p.rt(info);
  const isz = p.vec2(p.get(irt, 'm_SizeDelta'));
  const ipos = p.vec2(p.get(irt, 'm_AnchoredPosition'));
  const ipiv = p.vec2(p.get(irt, 'm_Pivot'));
  const iaMin = p.vec2(p.get(irt, 'm_AnchorMin'));
  if (isz.x !== 100 || isz.y !== 100) throw new Error(`RankInfo size expected 100x100, got ${isz.x}x${isz.y}`);
  if (ipos.x !== 0 || ipos.y !== 100) throw new Error(`RankInfo pos expected (0,100)`);
  if (ipiv.x !== 0.5 || ipiv.y !== 0) throw new Error('RankInfo pivot expected (0.5,0)');
  if (iaMin.x !== 0.5 || iaMin.y !== 0) throw new Error('RankInfo anchorMin expected (0.5,0)');

  // Parent-space (MatchContent bottom-centre) frame of the old RankInfo rect.
  const oldBottom = ipos.y;                  // 100
  const oldCentreY = oldBottom + isz.y * 0.5; // 150  (children anchor at RankInfo centre)

  // Children are all anchored to RankInfo's centre (0.5,0.5).
  const kids = p.childrenOf(irt).map(id => {
    const rtDoc = p.byId.get(id);
    const go = rtDoc.stripped ? null : p.byId.get(p.ref(p.get(rtDoc, 'm_GameObject')));
    return { id, rt: rtDoc, name: go ? p.get(go, 'm_Name') : '(nested prefab)' };
  });
  const measured = [];
  for (const k of kids) {
    const aMin = p.rectVec2(k.rt, 'm_AnchorMin');
    const aMax = p.rectVec2(k.rt, 'm_AnchorMax');
    const pos = p.rectVec2(k.rt, 'm_AnchoredPosition');
    const sz = p.rectVec2(k.rt, 'm_SizeDelta');
    const piv = p.rectVec2(k.rt, 'm_Pivot');
    if (aMin.x !== 0.5 || aMin.y !== 0.5 || aMax.x !== 0.5 || aMax.y !== 0.5)
      throw new Error(`RankInfo/${k.name}: expected centre anchors, got ${JSON.stringify(aMin)}/${JSON.stringify(aMax)}`);
    // centre of the child in parent space
    const cx = pos.x + (0.5 - piv.x) * sz.x;
    const cy = oldCentreY + pos.y + (0.5 - piv.y) * sz.y;
    measured.push({ ...k, pos, sz, piv, cx, cy, top: cy + sz.y / 2, bottom: cy - sz.y / 2, l: cx - sz.x / 2, r: cx + sz.x / 2 });
  }
  // RankText lives under RankBadge and overhangs it; include it in the bounds.
  const badge = measured.find(m => m.name === 'RankBadge');
  const extra = [];
  if (badge) {
    for (const cid of p.childrenOf(badge.rt)) {
      const d = p.byId.get(cid);
      if (!d || d.stripped) continue;
      const go = p.byId.get(p.ref(p.get(d, 'm_GameObject')));
      const nm = p.get(go, 'm_Name');
      const pos = p.vec2(p.get(d, 'm_AnchoredPosition'));
      const sz = p.vec2(p.get(d, 'm_SizeDelta'));
      const piv = p.vec2(p.get(d, 'm_Pivot'));
      const cx = badge.cx + pos.x + (0.5 - piv.x) * sz.x;
      const cy = badge.cy + pos.y + (0.5 - piv.y) * sz.y;
      extra.push({ name: `RankBadge/${nm}`, l: cx - sz.x / 2, r: cx + sz.x / 2, bottom: cy - sz.y / 2, top: cy + sz.y / 2 });
    }
  }
  const all = [...measured, ...extra];
  const bounds = {
    l: Math.min(...all.map(m => m.l)), r: Math.max(...all.map(m => m.r)),
    b: Math.min(...all.map(m => m.bottom)), t: Math.max(...all.map(m => m.top)),
  };
  for (const m of all) say(`  content: ${m.name} x[${m.l}..${m.r}] y[${m.bottom}..${m.top}]`);
  say(`  bounds: x[${bounds.l}..${bounds.r}] y[${bounds.b}..${bounds.t}]  (old rect was x[-50..50] y[100..200])`);

  // New rect: exactly the content bounds, still bottom-centre anchored.
  const newW = bounds.r - bounds.l;
  const newH = bounds.t - bounds.b;
  const newCentreY = bounds.b + newH / 2;
  const newCentreX = (bounds.l + bounds.r) / 2;
  p.setVec2(irt, 'm_SizeDelta', newW, newH);
  p.setVec2(irt, 'm_AnchoredPosition', newCentreX, bounds.b);
  say(`  RankInfo: size 100x100 -> ${newW}x${newH}, pos (0,100) -> (${newCentreX},${bounds.b})`);

  // Re-derive each child's anchoredPosition against the new centre so it does not move.
  for (const m of measured) {
    const newPosX = m.cx - newCentreX - (0.5 - m.piv.x) * m.sz.x;
    const newPosY = m.cy - newCentreY - (0.5 - m.piv.y) * m.sz.y;
    p.setRectVec2(m.rt, 'm_AnchoredPosition', newPosX, newPosY);
    say(`  ${m.name}: pos (${m.pos.x},${m.pos.y}) -> (${newPosX},${newPosY})  [centre stays (${m.cx},${m.cy})]`);
  }
}

p.save(OUT);
say(`\nwritten: ${OUT}`);
require('fs').writeFileSync(OUT + '.changelog.txt', log.join('\n') + '\n');
