// Faithful-enough UGUI layout resolver, used to prove that a structural change
// leaves resolved geometry untouched. Implements: anchors/pivot, LayoutElement,
// Horizontal/VerticalLayoutGroup, ContentSizeFitter. Grid groups fall back to
// anchors (fine for differential checks: identical on both sides).
const { Prefab } = require('./prefablib.js');

const G = {
  Image: 'fe87c0e1cc204ed48ad3b37840f39efc',
  VLG: '59f8146938fff824cb5fd77236b75775',
  HLG: '30649d3a9faa99c48a7b1166b86bf2a0',
  Grid: '8a8695521f0d02e499659fee002a26c2',
  LayoutElement: '306cc8c2b49d7114eaa3623786fc2126',
  CSF: '3245ec927659c4140ac4f8d17403cc18',
};

class Sim {
  constructor(path) {
    this.p = new Prefab(path);
    this.unsimulated = new Set();
  }
  _comp(goDoc, guid) {
    for (const id of this.p.components(goDoc)) {
      const d = this.p.byId.get(id);
      if (!d || d.stripped) continue;
      for (const l of d.lines) if (l.includes('m_Script:') && l.includes(guid)) return d;
    }
    return null;
  }
  num(doc, key) { const v = this.p.get(doc, key); return v === null ? null : parseFloat(v); }

  /** Build node objects for the whole tree under the canvas root. */
  build() {
    const p = this.p;
    const roots = [...p.docs].filter(d => (d.classId === '224' || d.classId === '4') && !d.stripped
      && p.ref(p.get(d, 'm_Father')) === '0');
    const canvas = roots.find(r => {
      const go = p.byId.get(p.ref(p.get(r, 'm_GameObject')));
      return go && p.get(go, 'm_Name') === 'LobbyCanvas';
    });
    if (!canvas) throw new Error('LobbyCanvas root not found');
    this.root = this.node(canvas, null);
    return this.root;
  }
  node(rtDoc, parent) {
    const p = this.p;
    const goId = rtDoc.stripped ? null : p.ref(p.get(rtDoc, 'm_GameObject'));
    const go = goId ? p.byId.get(goId) : null;
    const n = {
      rt: rtDoc, go, goId,
      name: go ? p.get(go, 'm_Name') : `(stripped &${rtDoc.fileId})`,
      active: go ? p.get(go, 'm_IsActive') !== '0' : true,
      parent, children: [],
    };
    n.path = parent ? `${parent.path}/${n.name}` : n.name;
    // rect inputs (stripped nodes read their nested-prefab overrides)
    const rv = (k, dflt) => {
      try { return p.rectVec2(rtDoc, k); } catch (e) { return dflt; }
    };
    n.aMin = rv('m_AnchorMin', { x: 0, y: 0 });
    n.aMax = rv('m_AnchorMax', { x: 0, y: 0 });
    n.pos = rv('m_AnchoredPosition', { x: 0, y: 0 });
    n.sizeDelta = rv('m_SizeDelta', { x: 0, y: 0 });
    n.pivot = rv('m_Pivot', { x: 0.5, y: 0.5 });
    if (rtDoc.stripped) this.unsimulated.add(n.path + ' (nested prefab rect from overrides)');
    // layout components
    if (go) {
      n.le = this._comp(go, G.LayoutElement);
      n.csf = this._comp(go, G.CSF);
      n.vlg = this._comp(go, G.VLG);
      n.hlg = this._comp(go, G.HLG);
      n.grid = this._comp(go, G.Grid);
      if (n.grid) this.unsimulated.add(n.path + ' (GridLayoutGroup: anchors fallback)');
      n.image = this._comp(go, G.Image);
    }
    for (const cid of this.p.childrenOf(rtDoc)) {
      const cd = this.p.byId.get(cid);
      if (!cd) continue;
      n.children.push(this.node(cd, n));
    }
    return n;
  }

  // ---- layout inputs -------------------------------------------------------
  group(n) {
    if (n.vlg) return { doc: n.vlg, vertical: true };
    if (n.hlg) return { doc: n.hlg, vertical: false };
    return null;
  }
  padding(doc) {
    const g = (k) => { const i = this.p.lineIndex(doc, 'm_Padding');
      for (let j = i + 1; j < doc.lines.length; j++) {
        const m = doc.lines[j].match(/^    (m_\w+): (-?[\d.]+)/);
        if (!m) break;
        if (m[1] === k) return parseFloat(m[2]);
      } return 0; };
    return { left: g('m_Left'), right: g('m_Right'), top: g('m_Top'), bottom: g('m_Bottom') };
  }
  groupOpts(doc) {
    const pad = this.padding(doc);
    return {
      pad,
      spacing: this.num(doc, 'm_Spacing') || 0,
      align: this.num(doc, 'm_ChildAlignment') || 0,
      controlW: this.num(doc, 'm_ChildControlWidth') === 1,
      controlH: this.num(doc, 'm_ChildControlHeight') === 1,
      expandW: this.num(doc, 'm_ChildForceExpandWidth') === 1,
      expandH: this.num(doc, 'm_ChildForceExpandHeight') === 1,
    };
  }
  alignOnAxis(align, axis) { return axis === 0 ? (align % 3) * 0.5 : Math.floor(align / 3) * 0.5; }

  /** LayoutUtility.GetLayoutProperty: highest priority wins, negatives skipped. */
  layoutProp(n, axis, which) {
    let best = 0, maxPri = -Infinity, sawUnknown = false;
    // LayoutElement, priority = m_LayoutPriority (default 1)
    if (n.le) {
      const key = { min: axis === 0 ? 'm_MinWidth' : 'm_MinHeight',
        pref: axis === 0 ? 'm_PreferredWidth' : 'm_PreferredHeight',
        flex: axis === 0 ? 'm_FlexibleWidth' : 'm_FlexibleHeight' }[which];
      const v = this.num(n.le, key);
      const pri = this.num(n.le, 'm_LayoutPriority');
      if (v !== null && v >= 0) { if (pri > maxPri) { best = v; maxPri = pri; } else if (pri === maxPri && v > best) best = v; }
    }
    // layout group on this node, priority 0
    const g = this.group(n);
    if (g) {
      const v = this.groupInput(n, axis, which);
      if (v !== null && v >= 0) { if (0 > maxPri) { best = v; maxPri = 0; } else if (maxPri === 0 && v > best) best = v; }
    } else if (n.grid) {
      sawUnknown = true;
    } else if (n.image && which !== 'flex') {
      // Image.preferred* = sprite native size; we cannot read sprite dimensions here.
      // Only matters if nothing higher-priority supplied a value.
      if (maxPri < 0) sawUnknown = true;
    }
    if (sawUnknown && maxPri < 0) this.unsimulated.add(`${n.path} ${which}${axis} from Image/Grid (unknown)`);
    return best;
  }

  /** Total min/preferred/flexible produced by a Horizontal/VerticalLayoutGroup. */
  groupInput(n, axis, which) {
    const g = this.group(n);
    if (!g) return null;
    const o = this.groupOpts(g.doc);
    const alongOther = (g.vertical !== (axis === 1)); // isVertical ^ (axis==1)
    const combinedPadding = axis === 0 ? o.pad.left + o.pad.right : o.pad.top + o.pad.bottom;
    const controlSize = axis === 0 ? o.controlW : o.controlH;
    const forceExpand = axis === 0 ? o.expandW : o.expandH;
    const kids = n.children.filter(c => c.active);
    let totalMin = combinedPadding, totalPref = combinedPadding, totalFlex = 0;
    for (const c of kids) {
      let min, pref, flex;
      if (!controlSize) { min = axis === 0 ? c.sizeDelta.x : c.sizeDelta.y; pref = min; flex = 0; }
      else { min = this.layoutProp(c, axis, 'min'); pref = this.layoutProp(c, axis, 'pref'); flex = this.layoutProp(c, axis, 'flex'); }
      if (forceExpand) flex = Math.max(flex, 1);
      if (alongOther) {
        totalMin = Math.max(min + combinedPadding, totalMin);
        totalPref = Math.max(pref + combinedPadding, totalPref);
        totalFlex = Math.max(flex, totalFlex);
      } else {
        totalMin += min + o.spacing; totalPref += pref + o.spacing; totalFlex += flex;
      }
    }
    if (!alongOther && kids.length > 0) { totalMin -= o.spacing; totalPref -= o.spacing; }
    totalPref = Math.max(totalMin, totalPref);
    return which === 'min' ? totalMin : which === 'pref' ? totalPref : totalFlex;
  }

  // ---- resolve -------------------------------------------------------------
  /** Fill n.rect = {x,y,w,h} in absolute canvas space (origin bottom-left). */
  resolve(n, canvasW, canvasH) {
    n.rect = { x: 0, y: 0, w: canvasW, h: canvasH };
    for (const c of n.children) this.resolveChild(c, n);
  }
  sizeFromAnchors(c, axis, parentSize) {
    const aMin = axis === 0 ? c.aMin.x : c.aMin.y;
    const aMax = axis === 0 ? c.aMax.x : c.aMax.y;
    const sd = axis === 0 ? c.sizeDelta.x : c.sizeDelta.y;
    return (aMax - aMin) * parentSize + sd;
  }
  minFromAnchors(c, axis, parentSize, size) {
    const aMin = axis === 0 ? c.aMin.x : c.aMin.y;
    const aMax = axis === 0 ? c.aMax.x : c.aMax.y;
    const piv = axis === 0 ? c.pivot.x : c.pivot.y;
    const ap = axis === 0 ? c.pos.x : c.pos.y;
    const pivotPos = aMin * parentSize + (aMax - aMin) * parentSize * piv + ap;
    return pivotPos - size * piv;
  }

  resolveChild(c, parent) {
    const P = parent.rect;
    const g = this.group(parent);
    const size = [null, null];
    const localMin = [null, null];

    if (g) {
      const o = this.groupOpts(g.doc);
      const kids = parent.children.filter(k => k.active);
      const idx = kids.indexOf(c);
      for (const axis of [0, 1]) {
        const alongOther = (g.vertical !== (axis === 1));
        const controlSize = axis === 0 ? o.controlW : o.controlH;
        const forceExpand = axis === 0 ? o.expandW : o.expandH;
        const parentSize = axis === 0 ? P.w : P.h;
        const padStart = axis === 0 ? o.pad.left : o.pad.top;
        const combinedPadding = axis === 0 ? o.pad.left + o.pad.right : o.pad.top + o.pad.bottom;
        const alignment = this.alignOnAxis(o.align, axis);
        const childSizes = (k) => {
          let min, pref, flex;
          if (!controlSize) { min = axis === 0 ? k.sizeDelta.x : k.sizeDelta.y; pref = min; flex = 0; }
          else { min = this.layoutProp(k, axis, 'min'); pref = this.layoutProp(k, axis, 'pref'); flex = this.layoutProp(k, axis, 'flex'); }
          if (forceExpand) flex = Math.max(flex, 1);
          return { min, pref, flex };
        };
        if (idx < 0) { // inactive child: keep anchors
          size[axis] = this.sizeFromAnchors(c, axis, parentSize);
          localMin[axis] = this.minFromAnchors(c, axis, parentSize, size[axis]);
          continue;
        }
        if (alongOther) {
          const innerSize = parentSize - combinedPadding;
          const s = childSizes(c);
          const requiredSpace = Math.min(Math.max(innerSize, s.min), s.flex > 0 ? parentSize : s.pref);
          const totalRequired = requiredSpace + combinedPadding;
          const startOffset = padStart + (parentSize - totalRequired) * alignment;
          if (controlSize) {
            size[axis] = requiredSpace;
            localMin[axis] = axis === 0 ? startOffset : parentSize - startOffset - requiredSpace;
          } else {
            size[axis] = this.sizeFromAnchors(c, axis, parentSize);
            const offsetInCell = (requiredSpace - size[axis]) * alignment;
            const at = startOffset + offsetInCell;
            localMin[axis] = axis === 0 ? at : parentSize - at - size[axis];
          }
        } else {
          const totalMin = this.groupInput(parent, axis, 'min');
          const totalPref = this.groupInput(parent, axis, 'pref');
          const totalFlex = this.groupInput(parent, axis, 'flex');
          const surplus = parentSize - totalPref;
          let pos = padStart, mult = 0;
          if (surplus > 0) {
            if (totalFlex === 0) pos = padStart + (parentSize - (totalPref - combinedPadding) - combinedPadding) * alignment;
            else mult = surplus / totalFlex;
          }
          let minMaxLerp = 0;
          if (totalMin !== totalPref) minMaxLerp = Math.max(0, Math.min(1, (parentSize - totalMin) / (totalPref - totalMin)));
          for (let i = 0; i <= idx; i++) {
            const s = childSizes(kids[i]);
            const cs = s.min + (s.pref - s.min) * minMaxLerp + s.flex * mult;
            if (i === idx) {
              if (controlSize) { size[axis] = cs; localMin[axis] = axis === 0 ? pos : parentSize - pos - cs; }
              else {
                size[axis] = this.sizeFromAnchors(c, axis, parentSize);
                const offsetInCell = (cs - size[axis]) * alignment;
                const at = pos + offsetInCell;
                localMin[axis] = axis === 0 ? at : parentSize - at - size[axis];
              }
            }
            pos += cs + o.spacing;
          }
        }
      }
    } else {
      for (const axis of [0, 1]) {
        const parentSize = axis === 0 ? P.w : P.h;
        size[axis] = this.sizeFromAnchors(c, axis, parentSize);
        localMin[axis] = this.minFromAnchors(c, axis, parentSize, size[axis]);
      }
    }

    // ContentSizeFitter overrides size on fitted axes (0=Unconstrained,1=MinSize,2=PreferredSize)
    if (c.csf) {
      const hf = this.num(c.csf, 'm_HorizontalFit'), vf = this.num(c.csf, 'm_VerticalFit');
      for (const [axis, fit] of [[0, hf], [1, vf]]) {
        if (fit !== 1 && fit !== 2) continue;
        const v = this.layoutProp(c, axis, fit === 1 ? 'min' : 'pref');
        const piv = axis === 0 ? c.pivot.x : c.pivot.y;
        // CSF keeps the pivot point fixed
        const pivotPos = localMin[axis] + size[axis] * piv;
        size[axis] = v;
        localMin[axis] = pivotPos - v * piv;
      }
    }

    c.rect = { x: P.x + localMin[0], y: P.y + localMin[1], w: size[0], h: size[1] };
    for (const k of c.children) this.resolveChild(k, c);
  }

  /** fileId -> {path, rect} for every node with a stable id. */
  snapshot(canvasW, canvasH) {
    this.build();
    this.resolve(this.root, canvasW, canvasH);
    const out = new Map();
    const walk = (n) => {
      out.set(n.rt.fileId, { path: n.path, name: n.name, rect: n.rect, active: n.active });
      for (const c of n.children) walk(c);
    };
    walk(this.root);
    return out;
  }
}

module.exports = { Sim };
