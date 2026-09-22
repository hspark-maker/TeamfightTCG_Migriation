import { useEffect, useMemo, useRef, useState } from "react";
import { api, errorMessage, type Env } from "./api";
import "./spec.css";

type TableInfo = { name: string; file: string; rows: number; columns: number; editable: boolean; published?: boolean; error?: string };
type Listing = { tables: TableInfo[]; generated: { bytes: { exists: boolean; hash: string | null }; csvOnly: true } };
type Sheet = {
  name: string; file: string; hash: string;
  columns: { name: string; type: string; description: string }[];
  rows: { id: string; cells: string[] }[];
  editable: boolean; reason?: string;
};
type Change = { rowId: string; column: string; value: string };
type CellDiff = { rowId: string; column: string; before: string | null; after: string | null; table?: string };
type Preview = { table: string; baseHash: string; nextHash: string; changes: CellDiff[]; changedCells: number };
type Comparison = {
  env: Env; contentVersion: string | null; localMajor: number;
  tables: { name: string; localRows: number; localHash: string; remoteHash: string | null; state: "same" | "different" | "unpublished" }[];
};
type Plan = {
  planId: string; expiresAtMs: number; env: "test"; from: string | null; to: string; writes: number;
  changes: { table: string; oldRows: number; rows: number; revision: number; payloadHash: string }[];
  diffs: CellDiff[]; diffsTruncated: boolean;
};

class SpecRequestError extends Error {
  constructor(message: string, readonly code = "") { super(message); }
}

async function request<T>(action: string, body: object, signal: AbortSignal): Promise<T> {
  const user = api?.auth.currentUser;
  if (!user) throw new SpecRequestError("관리자 로그인이 필요합니다. 다시 로그인해 주세요.");
  const token = await user.getIdToken();
  if (signal.aborted) throw new DOMException("Aborted", "AbortError");
  let response: Response;
  try {
    response = await fetch(`/__spec/${action}`, {
      method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
      body: JSON.stringify(body), cache: "no-store", signal: AbortSignal.any([signal, AbortSignal.timeout(120_000)]),
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new SpecRequestError("로컬 관리 서버에 연결할 수 없습니다. dashboard/start.cmd 실행 상태를 확인해 주세요. 저장·발행 중이었다면 현재 상태를 조회한 뒤 다시 시도하세요.");
  }
  if (!response.headers.get("content-type")?.includes("application/json")) {
    throw new SpecRequestError("CSV 관리 기능은 로컬 관리 서버에서 사용할 수 있습니다. dashboard/start.cmd로 실행해 주세요.");
  }
  const result = await response.json();
  if (!response.ok) throw new SpecRequestError(result.error?.message || "요청을 처리하지 못했습니다.", result.error?.code || String(response.status));
  return result as T;
}

function DiffTable({ diffs, withTable = false }: { diffs: CellDiff[]; withTable?: boolean }) {
  return <div className="table-scroll spec-diffs"><table><thead><tr>
    {withTable && <th>테이블</th>}<th>행 ID</th><th>항목</th><th>변경 전</th><th>변경 후</th>
  </tr></thead><tbody>{diffs.map((diff, index) => <tr key={`${diff.table}:${diff.rowId}:${diff.column}:${index}`}>
    {withTable && <td>{diff.table}</td>}<td>{diff.rowId}</td><td>{diff.column}</td>
    <td className="spec-before">{diff.before === null ? <em>없음</em> : diff.before === "" ? <em>빈 값</em> : diff.before}</td>
    <td className="spec-after">{diff.after === null ? <em>없음</em> : diff.after === "" ? <em>빈 값</em> : diff.after}</td>
  </tr>)}</tbody></table></div>;
}

export function SpecManager({ env, onDirtyChange, onPublished, onBusyChange }: {
  env: Env; onDirtyChange: (dirty: boolean) => void; onPublished?: () => void; onBusyChange?: (busy: boolean) => void;
}) {
  const [mode, setMode] = useState<"edit" | "publish">("edit");
  const [listing, setListing] = useState<Listing | null>(null);
  const [sheet, setSheet] = useState<Sheet | null>(null);
  const [tableQuery, setTableQuery] = useState("");
  const [rowQuery, setRowQuery] = useState("");
  const [page, setPage] = useState(0);
  const [patches, setPatches] = useState<Record<string, Change>>({});
  const [preview, setPreview] = useState<Preview | null>(null);
  const [comparison, setComparison] = useState<Comparison | null>(null);
  const [plan, setPlan] = useState<Plan | null>(null);
  const [confirmation, setConfirmation] = useState("");
  const [now, setNow] = useState(Date.now());
  const [busy, setBusy] = useState("");
  const [error, setError] = useState<SpecRequestError | null>(null);
  const [notice, setNotice] = useState("");
  const requestRef = useRef<{ sequence: number; controller?: AbortController }>({ sequence: 0 });
  const mounted = useRef(false);
  const busyRef = useRef(false);
  const dirtyCallback = useRef(onDirtyChange);
  dirtyCallback.current = onDirtyChange;
  const busyCallback = useRef(onBusyChange);
  busyCallback.current = onBusyChange;
  const changes = useMemo(() => Object.values(patches), [patches]);
  const dirty = changes.length > 0;
  const mutating = busy === "save" || busy === "publish";

  async function run<T>(action: string, body: object, done: (result: T) => void) {
    if (busyRef.current) return;
    busyRef.current = true;
    const sequence = ++requestRef.current.sequence;
    const controller = new AbortController();
    requestRef.current.controller = controller;
    setBusy(action); setError(null); setNotice("");
    try {
      const result = await request<T>(action, body, controller.signal);
      if (mounted.current && sequence === requestRef.current.sequence) done(result);
    } catch (cause) {
      if (mounted.current && sequence === requestRef.current.sequence && !controller.signal.aborted) {
        setError(cause instanceof SpecRequestError ? cause : new SpecRequestError(errorMessage(cause)));
        if (action === "save") setPreview(null);
        if (action === "publish") { setPlan(null); setComparison(null); setConfirmation(""); }
      }
    } finally {
      if (mounted.current && sequence === requestRef.current.sequence) { setBusy(""); busyRef.current = false; }
    }
  }

  useEffect(() => {
    mounted.current = true;
    requestRef.current.controller?.abort();
    ++requestRef.current.sequence;
    busyRef.current = false;
    setBusy(""); setSheet(null); setPatches({}); setPreview(null); setPlan(null); setComparison(null);
    setConfirmation(""); setError(null); setNotice("");
    void run<Listing>("list", {}, setListing);
    return () => {
      mounted.current = false;
      requestRef.current.controller?.abort();
      ++requestRef.current.sequence;
      busyRef.current = false;
      dirtyCallback.current(false);
      busyCallback.current?.(false);
    };
    // Environment changes invalidate every in-flight response and review snapshot.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [env]);

  useEffect(() => { dirtyCallback.current(dirty || mutating); }, [dirty, mutating]);
  useEffect(() => { busyCallback.current?.(mutating); }, [mutating]);
  useEffect(() => {
    if (!dirty && !mutating) return;
    const protect = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", protect);
    return () => window.removeEventListener("beforeunload", protect);
  }, [dirty, mutating]);
  useEffect(() => {
    if (!plan) return;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [plan]);

  function loadTable(name: string) {
    if (busyRef.current) return;
    if (dirty && !window.confirm("저장하지 않은 변경을 버리고 CSV를 다시 읽을까요?")) return;
    setPreview(null); setPlan(null); setConfirmation("");
    void run<Sheet>("read", { table: name }, (result) => {
      setSheet(result); setPatches({}); setComparison(null); setPage(0); setRowQuery("");
    });
  }

  function edit(rowId: string, column: string, original: string, value: string) {
    const key = JSON.stringify([rowId, column]);
    setPatches((current) => {
      const next = { ...current };
      if (value === original) delete next[key]; else next[key] = { rowId, column, value };
      return next;
    });
    setPreview(null); setPlan(null); setConfirmation(""); setNotice(""); setError(null);
  }

  const filteredRows = useMemo(() => {
    const query = rowQuery.trim().toLocaleLowerCase();
    return (sheet?.rows || []).filter((row) => !query || row.id.toLocaleLowerCase().includes(query) || row.cells.some((cell) => cell.toLocaleLowerCase().includes(query)));
  }, [sheet, rowQuery]);
  const pages = Math.max(1, Math.ceil(filteredRows.length / 30));
  const visibleRows = filteredRows.slice(page * 30, (page + 1) * 30);
  const tableList = (listing?.tables || []).filter((table) => table.name.toLocaleLowerCase().includes(tableQuery.trim().toLocaleLowerCase()));
  const expired = !!plan && plan.expiresAtMs <= now;
  const changedTables = comparison?.tables.filter((table) => table.state !== "same").length || 0;

  function savePreview() {
    if (!sheet || !preview || busyRef.current) return;
    void run<Sheet & { changedCells: number }>("save", { table: sheet.name, baseHash: preview.baseHash, changes }, (result) => {
      setSheet(result); setPatches({}); setPreview(null); setPlan(null); setComparison(null);
      setNotice(`${result.name}의 ${result.changedCells}개 값을 로컬 CSV에 저장했습니다. 서버에는 아직 발행하지 않았습니다.`);
    });
  }

  function publishPlan() {
    if (!plan || expired || dirty || env !== "test" || confirmation !== plan.to || busyRef.current) return;
    if (!window.confirm(`TEST 서버에 콘텐츠 ${plan.to}을 발행합니다.\n변경 테이블 ${plan.changes.length}개 · 쓰기 ${plan.writes}건\n검토한 변경을 발행할까요?`)) return;
    void run<{ published: string | boolean; env: Env; verifiedTables: number }>("publish", { env: "test", planId: plan.planId, confirmation }, (result) => {
      setNotice(`TEST 콘텐츠 ${plan.to} 발행을 완료했습니다. 서버 검증: ${result.verifiedTables}개 테이블.`);
      setPlan(null); setComparison(null); setConfirmation(""); onPublished?.();
    });
  }

  return <div className="spec-manager">
    <div className="spec-topline">
      <div className="spec-tabs" aria-label="스펙 관리 작업">
        <button className={mode === "edit" ? "active" : ""} aria-pressed={mode === "edit"} onClick={() => setMode("edit")} disabled={!!busy}>CSV 편집{dirty && <span className="spec-count">{changes.length}</span>}</button>
        <button className={mode === "publish" ? "active" : ""} aria-pressed={mode === "publish"} onClick={() => setMode("publish")} disabled={!!busy}>서버 비교 · 발행</button>
      </div>
      <span className={`badge ${env === "test" ? "success" : "warning"}`}><i />{env.toUpperCase()} 서버</span>
    </div>
    <p className="spec-intro">로컬 CSV를 편집·저장하고, 발행 대상 스펙을 서버와 비교해 발행합니다.</p>
    {error && <div className="error" role="alert"><span>{error.message}{mutating && " 처리 결과를 다시 확인해 주세요."}</span><div className="spec-actions">
      {sheet && mode === "edit" && <button disabled={!!busy} onClick={() => loadTable(sheet.name)}>CSV 다시 읽기</button>}
      {!listing && <button disabled={!!busy} onClick={() => void run<Listing>("list", {}, setListing)}>다시 연결</button>}
    </div></div>}
    {notice && <div className="spec-success" role="status">✓ {notice}</div>}
    {busy && <div className="spec-working" role="status"><span className="spinner" />{{ list: "로컬 CSV 목록을 읽는 중", read: "CSV를 읽는 중", preview: "변경을 검증하는 중", save: "CSV를 저장하는 중", compare: "서버와 비교하는 중", plan: "발행 계획을 검증하는 중", publish: "TEST 서버에 발행하고 검증하는 중 · 완료까지 창을 유지하세요" }[busy]}</div>}

    {mode === "edit" ? <div className="spec-editor-layout">
      <aside className="panel spec-picker">
        <div className="panel-heading"><h2>스펙 테이블</h2><span>{listing?.tables.length ?? "—"}개</span></div>
        <input aria-label="테이블 검색" placeholder="테이블 이름 검색" value={tableQuery} onChange={(event) => setTableQuery(event.target.value)} />
        <div className="spec-table-list">{tableList.map((table) => <button key={table.name} className={sheet?.name === table.name ? "active" : ""} disabled={!!busy} onClick={() => loadTable(table.name)} title={table.error || table.file}>
          <span><strong>{table.name}</strong><small>{table.rows.toLocaleString("ko-KR")}행 · {table.columns}열</small>{table.published === false && <small>로컬 전용 · 서버 발행 제외</small>}</span><span className={`spec-table-dot ${table.error ? "invalid" : table.editable ? "editable" : ""}`} aria-label={table.error ? "오류" : table.editable ? "편집 가능" : "읽기 전용"} />
        </button>)}</div>
        {listing && !tableList.length && <p className="spec-muted">검색 결과가 없습니다.</p>}
      </aside>
      <section className="panel spec-editor">
        {!sheet ? <div className="empty"><h3>편집할 테이블을 선택하세요</h3><p>기존 행의 값을 수정할 수 있습니다. ID와 테이블 구조는 읽기 전용입니다.</p></div> : <>
          <div className="panel-heading"><div><h2>{sheet.name}</h2><span>{sheet.file}</span></div><span className={`badge ${dirty ? "warning" : "success"}`}><i />{dirty ? `${changes.length}개 값 변경 중` : "저장된 CSV"}</span></div>
          {!sheet.editable && <div className="notice">읽기 전용: {sheet.reason || "이 테이블은 현재 편집을 지원하지 않습니다."}</div>}
          {listing?.tables.find((table) => table.name === sheet.name)?.published === false && <div className="notice">로컬 전용 테이블입니다. CSV 편집·저장은 가능하며 서버 발행에는 포함되지 않습니다.</div>}
          <div className="spec-grid-toolbar"><input aria-label="행 검색" placeholder="ID 또는 값 검색" value={rowQuery} onChange={(event) => { setRowQuery(event.target.value); setPage(0); }} /><button onClick={() => loadTable(sheet.name)} disabled={!!busy}>다시 읽기</button></div>
          <div className="table-scroll spec-grid"><table><thead><tr>{sheet.columns.map((column) => <th key={column.name} title={column.description}><strong>{column.name}</strong><small>{column.type}{column.name.toLowerCase() === "id" ? " · 고정" : ""}</small>{column.description && <span>{column.description}</span>}</th>)}</tr></thead><tbody>{visibleRows.map((row) => <tr key={row.id}>{sheet.columns.map((column, index) => {
            const original = row.cells[index] ?? "";
            const patch = patches[JSON.stringify([row.id, column.name])];
            const value = patch?.value ?? original;
            const readOnly = !sheet.editable || column.name.toLowerCase() === "id";
            const label = `${sheet.name} ${row.id} ${column.name}`;
            return <td key={column.name} className={patch ? "spec-cell changed" : "spec-cell"}>{readOnly ? <span className="spec-readonly" title={value}>{value || "—"}</span> : original.length > 70 || original.includes("\n") ? <textarea aria-label={label} value={value} disabled={!!busy} onChange={(event) => edit(row.id, column.name, original, event.target.value)} rows={2} /> : <input aria-label={label} value={value} disabled={!!busy} onChange={(event) => edit(row.id, column.name, original, event.target.value)} />}</td>;
          })}</tr>)}</tbody></table></div>
          {!filteredRows.length && <p className="spec-muted">표시할 행이 없습니다.</p>}
          <div className="spec-pagination"><span>{filteredRows.length.toLocaleString("ko-KR")}행 · {page + 1} / {pages}페이지</span><div className="spec-actions"><button disabled={page === 0} onClick={() => setPage((value) => Math.max(0, value - 1))}>이전</button><button disabled={page + 1 >= pages} onClick={() => setPage((value) => Math.min(pages - 1, value + 1))}>다음</button></div></div>
          <div className="spec-savebar"><p>CSV 저장은 서버 발행과 별도입니다.</p><button className="primary" disabled={!dirty || !!busy} onClick={() => void run<Preview>("preview", { table: sheet.name, baseHash: sheet.hash, changes }, setPreview)}>변경 검토 · {changes.length}개</button></div>
          {preview && <section className="spec-review" aria-label="CSV 저장 전 검토"><div className="panel-heading"><h2>저장 전 변경 검토</h2><span>{preview.changedCells}개 값</span></div><DiffTable diffs={preview.changes} /><div className="spec-review-footer"><span>위 변경을 {sheet.name}_sheet.csv에 저장합니다.</span><button className="primary" disabled={!!busy || !dirty} onClick={savePreview}>검토한 변경 저장</button></div></section>}
        </>}
      </section>
    </div> : <section className="panel spec-publish">
      <div className="panel-heading"><div><h2>로컬 CSV ↔ {env.toUpperCase()} 서버</h2><span>저장된 발행 대상 CSV를 기준으로 비교합니다.</span></div><button disabled={!!busy || dirty} onClick={() => { setPlan(null); setConfirmation(""); void run<Comparison>("compare", { env }, setComparison); }}>서버와 비교</button></div>
      {dirty && <div className="notice">저장하지 않은 변경이 {changes.length}개 있습니다. CSV 편집에서 먼저 검토·저장해 주세요.</div>}
      {env === "live" && <div className="notice">LIVE는 비교만 지원합니다. 이번 버전의 발행 기능은 TEST 전용입니다.</div>}
      {comparison ? <>
        <div className="spec-compare-summary"><div><small>서버 콘텐츠 버전</small><strong>{comparison.contentVersion || "미발행"}</strong></div><div><small>비교한 테이블</small><strong>{comparison.tables.length}</strong></div><div><small>차이가 있는 테이블</small><strong className={changedTables ? "spec-amber" : ""}>{changedTables}</strong></div></div>
        <div className="table-scroll spec-compare-table"><table><thead><tr><th>테이블</th><th>로컬 행 수</th><th>비교 결과</th><th>로컬 해시</th><th>서버 해시</th></tr></thead><tbody>{comparison.tables.map((table) => <tr key={table.name}><td><strong>{table.name}</strong></td><td>{table.localRows.toLocaleString("ko-KR")}</td><td><span className={`badge ${table.state === "same" ? "success" : "warning"}`}><i />{{ same: "일치", different: "변경 있음", unpublished: "미발행" }[table.state]}</span></td><td className="hash" title={table.localHash}>{table.localHash.slice(0, 12)}</td><td className="hash" title={table.remoteHash || ""}>{table.remoteHash?.slice(0, 12) || "—"}</td></tr>)}</tbody></table></div>
      </> : <p className="spec-muted">서버와 비교하면 테이블별 일치 여부와 현재 콘텐츠 버전을 확인할 수 있습니다.</p>}
      <div className="spec-savebar"><p>TEST의 발행 대상 스펙을 하나의 콘텐츠 버전으로 발행합니다.</p><button className="primary" disabled={!!busy || dirty || env !== "test"} onClick={() => { setPlan(null); setConfirmation(""); void run<Plan>("plan", { env: "test" }, setPlan); }}>TEST 발행 계획 만들기</button></div>
      {plan && <section className="spec-review" aria-label="서버 발행 계획">
        <div className="panel-heading"><div><h2>TEST 발행 계획</h2><span>{plan.from || "미발행"} → {plan.to}</span></div><span className={`badge ${expired ? "danger" : "warning"}`}><i />{expired ? "계획 만료 · 다시 생성하세요" : `${Math.ceil((plan.expiresAtMs - now) / 60000)}분 이내 발행 가능`}</span></div>
        <p className="spec-muted">변경 테이블 {plan.changes.length}개 · 서버 쓰기 {plan.writes}건. 아래 내용은 계획 생성 시점의 스냅샷입니다.</p>
        <div className="table-scroll"><table><thead><tr><th>테이블</th><th>기존 행 수</th><th>발행 행 수</th><th>리비전</th></tr></thead><tbody>{plan.changes.map((change) => <tr key={change.table}><td>{change.table}</td><td>{change.oldRows}</td><td>{change.rows}</td><td>{change.revision}</td></tr>)}</tbody></table></div>
        {!!plan.diffs.length && <><h3 className="spec-diff-title">값 변경 내역</h3><DiffTable diffs={plan.diffs} withTable /></>}
        {plan.diffsTruncated && <div className="notice spec-truncated">변경량이 많아 값 변경 내역의 일부만 표시됩니다. 테이블 목록은 전체 변경 범위입니다.</div>}
        {!plan.changes.length && <p className="spec-muted">발행할 테이블 변경이 없습니다.</p>}
        <div className="spec-publish-confirm"><label htmlFor="spec-confirm-version">발행 버전 <strong>{plan.to}</strong>을 입력해 확인하세요.<input id="spec-confirm-version" value={confirmation} placeholder={plan.to} autoComplete="off" disabled={!!busy || expired} onChange={(event) => setConfirmation(event.target.value)} /></label><button className="primary" disabled={!!busy || dirty || expired || !plan.changes.length || confirmation !== plan.to} onClick={publishPlan}>TEST 서버에 발행</button></div>
      </section>}
    </section>}
    <div className="spec-footnote"><span>원본: <code>docs/SpecData/*_sheet.csv</code></span><span>SpecData.bytes 재생성 · Google 시트 동기화는 수행하지 않습니다.</span>{listing && <span>로컬 bytes: {listing.generated.bytes.exists ? "파일 있음" : "파일 없음"} · CSV와의 일치 여부는 별도 확인</span>}</div>
  </div>;
}

export default SpecManager;
