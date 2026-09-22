import {
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type ReactNode,
} from "react";
import {
  GoogleAuthProvider,
  onIdTokenChanged,
  signInWithEmailAndPassword,
  signInWithPopup,
  signOut,
  type User,
} from "firebase/auth";
import {
  api,
  configError,
  emulator,
  errorMessage,
  links,
  projectId,
  type Doc,
  type Env,
  type Json,
  type Overview,
  type Player,
  type ReplayDay,
} from "./api";
import { SpecManager } from "./SpecManager";
import { MatchStats } from "./MatchStats";

type Page = "overview" | "players" | "content" | "replay" | "specs" | "matches";
type IconName = Page | "arrow" | "refresh" | "shield" | "logout" | "search";
const number = new Intl.NumberFormat("ko-KR");
const nav: { id: Page; label: string; caption: string }[] = [
  {
    id: "matches",
    label: "매치 통계",
    caption: "최근 매치의 결과와 플레이 추이를 확인하세요.",
  },
  {
    id: "overview",
    label: "종합 현황",
    caption: "프로젝트의 현재 상태를 한눈에 확인하세요.",
  },
  {
    id: "players",
    label: "유저 조회",
    caption: "UID로 계정과 게임 데이터를 확인하세요.",
  },
  {
    id: "content",
    label: "콘텐츠 · 버전",
    caption: "현재 공개된 콘텐츠와 앱 버전 정책입니다.",
  },
  {
    id: "replay",
    label: "전투 검증",
    caption: "최근 7일의 서버 전투 검증 결과입니다.",
  },
  {
    id: "specs",
    label: "CSV · SpecData",
    caption: "CSV를 편집하고 저장한 데이터를 서버와 비교하세요.",
  },
];

function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, ReactNode> = {
    matches: (
      <>
        <path d="M4 3v18h17M8 16v-5M13 16V7M18 16V4" />
      </>
    ),
    specs: (
      <>
        <rect x="3" y="3" width="18" height="18" rx="2" />
        <path d="M3 9h18M9 9v12M15 9v12M3 15h18" />
      </>
    ),
    overview: (
      <>
        <rect x="3" y="3" width="7" height="7" rx="1" />
        <rect x="14" y="3" width="7" height="7" rx="1" />
        <rect x="3" y="14" width="7" height="7" rx="1" />
        <rect x="14" y="14" width="7" height="7" rx="1" />
      </>
    ),
    players: (
      <>
        <circle cx="9" cy="8" r="3" />
        <path d="M3 21v-3a6 6 0 0 1 12 0v3M16 5a3 3 0 0 1 0 6m2 3a5 5 0 0 1 3 4v3" />
      </>
    ),
    content: (
      <>
        <path d="m12 3 10 5-10 5L2 8l10-5ZM2 12l10 5 10-5M2 16l10 5 10-5" />
      </>
    ),
    replay: (
      <>
        <path d="m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6l8-3Z" />
        <path d="m8 12 3 3 5-6" />
      </>
    ),
    shield: (
      <>
        <path d="m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6l8-3Z" />
        <path d="M12 8v5m0 3h.01" />
      </>
    ),
    arrow: <path d="M7 17 17 7M7 7h10v10" />,
    refresh: (
      <>
        <path d="M20 7v5h-5M4 17v-5h5" />
        <path d="M6 7a7 7 0 0 1 12-2l2 3M4 16l2 3a7 7 0 0 0 12-2" />
      </>
    ),
    logout: (
      <>
        <path d="M9 3H4v18h5m6-14 5 5-5 5M9 12h11" />
      </>
    ),
    search: (
      <>
        <circle cx="10" cy="10" r="6" />
        <path d="m15 15 6 6" />
      </>
    ),
  };
  return (
    <svg
      width="20"
      height="20"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {paths[name]}
    </svg>
  );
}

const record = (value: Json | undefined): Doc =>
  value && typeof value === "object" && !Array.isArray(value) ? value : {};
const array = (value: Json | undefined): Json[] =>
  Array.isArray(value) ? value : [];
const label = (value: Json | undefined): string =>
  value == null || value === ""
    ? "—"
    : typeof value === "number"
      ? number.format(value)
      : String(value);
function time(value: string | number | Json | undefined) {
  if (typeof value !== "string" && typeof value !== "number") return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "—"
    : date.toLocaleString("ko-KR", { hour12: false });
}

function Brand() {
  return (
    <div className="brand">
      <div className="brand-mark">
        C<span>▰</span>
      </div>
      <div>
        <strong>CARD BATTLE</strong>
        <small>운영 대시보드</small>
      </div>
    </div>
  );
}
function Badge({
  children,
  tone = "neutral",
}: {
  children: ReactNode;
  tone?: string;
}) {
  return (
    <span className={`badge ${tone}`}>
      <i />
      {children}
    </span>
  );
}
function Empty({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <Icon name="search" />
      <h3>{title}</h3>
      {children && <p>{children}</p>}
    </div>
  );
}
function ErrorBox({ message, retry }: { message: string; retry?: () => void }) {
  return (
    <div className="error" role="alert">
      <span>{message}</span>
      {retry && <button onClick={retry}>다시 시도</button>}
    </div>
  );
}
function Loading() {
  return (
    <div className="loading" role="status">
      <span className="spinner" />
      서버에서 조회하고 있습니다…
    </div>
  );
}
function Metric({
  title,
  value,
  note,
  tone = "",
}: {
  title: string;
  value: ReactNode;
  note: string;
  tone?: string;
}) {
  return (
    <article className={`metric ${tone}`}>
      <span>{title}</span>
      <strong>{value}</strong>
      <small>{note}</small>
    </article>
  );
}
function JsonDetails({ title, data }: { title: string; data: Json }) {
  return (
    <details className="json-details">
      <summary>
        {title}
        <span>원본 보기</span>
      </summary>
      <pre>{JSON.stringify(data, null, 2)}</pre>
    </details>
  );
}
function ConsoleLinks() {
  return (
    <section className="panel">
      <div className="panel-heading">
        <h2>콘솔 바로가기</h2>
        <span>별도 권한으로 열립니다</span>
      </div>
      <div className="console-grid">
        {links.map((link) => (
          <a
            key={link.label}
            href={link.href}
            target="_blank"
            rel="noopener noreferrer"
          >
            <div>
              <strong>{link.label}</strong>
              <small>{link.detail}</small>
            </div>
            <Icon name="arrow" />
          </a>
        ))}
      </div>
    </section>
  );
}

export function App() {
  const [session, setSession] = useState<{
    user: User;
    allowed: boolean;
  } | null>(null);
  const [checking, setChecking] = useState(!!api);
  const [authError, setAuthError] = useState("");
  const [env, setEnv] = useState<Env>("test");
  useEffect(() => {
    if (!api) return;
    let generation = 0;
    let active = true;
    let verifiedUid: string | null = null;
    const unsubscribe = onIdTokenChanged(api.auth, (user) => {
      const ticket = ++generation;
      // A same-account token refresh must not discard an unsaved CSV draft.
      if (verifiedUid !== user?.uid) setSession(null);
      if (!user) {
        verifiedUid = null;
        setSession(null);
        setChecking(false);
        return;
      }
      if (verifiedUid !== user.uid) setChecking(true);
      user
        .getIdTokenResult()
        .then((token) => {
          if (active && ticket === generation) {
            verifiedUid = user.uid;
            setSession({ user, allowed: token.claims.admin === true });
          }
        })
        .catch((error) => {
          if (active && ticket === generation) {
            verifiedUid = null;
            setSession(null);
            setAuthError(errorMessage(error));
          }
        })
        .finally(() => {
          if (active && ticket === generation) setChecking(false);
        });
    });
    api.ready.catch((error) => {
      if (active) setAuthError(errorMessage(error));
    });
    return () => {
      active = false;
      unsubscribe();
    };
  }, []);
  async function logout() {
    setSession(null);
    setEnv("test");
    setAuthError("");
    try {
      await signOut(api!.auth);
    } catch (error) {
      setAuthError(errorMessage(error));
    }
  }
  if (checking)
    return (
      <main className="auth-page">
        <Brand />
        <Loading />
      </main>
    );
  if (!session || !session.allowed || configError)
    return (
      <main className="auth-page">
        <div className="auth-shell">
          <Brand />
          <div className="auth-intro">
            <span className="eyebrow">OPERATIONS CONSOLE</span>
            <h1>
              게임 운영의
              <br />
              <em>한눈에 보이는 현재.</em>
            </h1>
            <p>
              유저 정보부터 콘텐츠 버전, 전투 검증까지.
              <br />
              관리자 계정으로 프로젝트 현황을 확인하세요.
            </p>
            <div className="auth-pills">
              <span>유저 조회</span>
              <span>콘텐츠 버전</span>
              <span>전투 검증</span>
            </div>
          </div>
        </div>
        <section className="auth-card">
          <Icon name="shield" />
          <h2>{session ? "관리자 권한이 필요합니다" : "관리자 로그인"}</h2>
          <p>
            {session
              ? `${session.user.email || session.user.uid} 계정에는 조회 권한이 없습니다.`
              : "기존 Firebase 관리자 계정으로 로그인하세요."}
          </p>
          {configError ? (
            <ErrorBox message={configError} />
          ) : session ? (
            <button className="primary" onClick={logout}>
              다른 계정으로 로그인
            </button>
          ) : (
            <Login error={authError} />
          )}
          <div className="auth-footer">
            <Badge>관리자 전용</Badge>
            <span>{projectId}</span>
          </div>
        </section>
      </main>
    );
  return (
    <Workspace
      key={`${session.user.uid}:${env}`}
      user={session.user}
      env={env}
      onEnv={setEnv}
      onLogout={logout}
    />
  );
}

function Login({ error }: { error: string }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [failure, setFailure] = useState("");
  const [busy, setBusy] = useState(false);
  async function login(google: boolean) {
    setBusy(true);
    setFailure("");
    try {
      await api!.ready;
      if (google) await signInWithPopup(api!.auth, new GoogleAuthProvider());
      else await signInWithEmailAndPassword(api!.auth, email.trim(), password);
    } catch (cause) {
      setFailure(errorMessage(cause));
    } finally {
      setBusy(false);
      setPassword("");
    }
  }
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        void login(false);
      }}
      className="login-form"
    >
      <label>
        이메일
        <input
          type="email"
          autoComplete="username"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          required
          placeholder="admin@example.com"
          disabled={busy}
        />
      </label>
      <label>
        비밀번호
        <input
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
          disabled={busy}
        />
      </label>
      {(failure || error) && <ErrorBox message={failure || error} />}
      <button className="primary" disabled={busy}>
        {busy ? "로그인 중…" : "로그인"}
      </button>
      <div className="divider">또는</div>
      <button type="button" disabled={busy} onClick={() => void login(true)}>
        Google로 로그인
      </button>
    </form>
  );
}

function Workspace({
  user,
  env,
  onEnv,
  onLogout,
}: {
  user: User;
  env: Env;
  onEnv: (env: Env) => void;
  onLogout: () => void;
}) {
  const [page, setPage] = useState<Page>("overview");
  const [overview, setOverview] = useState<Overview | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(true);
  const [refresh, setRefresh] = useState(0);
  const [hasDraft, setHasDraft] = useState(false);
  const [specBusy, setSpecBusy] = useState(false);
  function leave(action: () => void) {
    if (specBusy) return;
    if (
      hasDraft &&
      !window.confirm("저장하지 않은 CSV 변경을 버리고 이동할까요?")
    )
      return;
    setHasDraft(false);
    action();
  }
  useEffect(() => {
    let active = true;
    setOverview(null);
    setError("");
    setBusy(true);
    api!
      .overview(env)
      .then((data) => {
        if (active) setOverview(data);
      })
      .catch((cause) => {
        if (active) setError(errorMessage(cause));
      })
      .finally(() => {
        if (active) setBusy(false);
      });
    return () => {
      active = false;
    };
  }, [env, refresh]);
  const current = nav.find((item) => item.id === page)!;
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <Brand />
        <div className="nav-label">WORKSPACE</div>
        <nav aria-label="주 메뉴">
          {nav.map((item) => (
            <button
              key={item.id}
              disabled={specBusy}
              className={page === item.id ? "active" : ""}
              aria-current={page === item.id ? "page" : undefined}
              onClick={() => {
                if (page !== item.id) leave(() => setPage(item.id));
              }}
            >
              <Icon name={item.id} />
              {item.label}
              {page === item.id && <span className="nav-dot" />}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="scope-note">
            <Icon name="shield" />
            <div>
              <strong>운영 워크스페이스</strong>
              <p>유저 조회와 CSV 기반 콘텐츠 관리.</p>
            </div>
          </div>
          <div className="operator">
            <span className="avatar">
              {(user.email || "A")[0].toUpperCase()}
            </span>
            <div>
              <strong>관리자</strong>
              <small title={user.email || user.uid}>
                {user.email || user.uid}
              </small>
            </div>
            <button
              className="icon-button"
              onClick={() => leave(onLogout)}
              aria-label="로그아웃"
              disabled={specBusy}
            >
              <Icon name="logout" />
            </button>
          </div>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <div className="breadcrumb">
            Card Battle <span>/</span> 운영 <span>/</span>
            <strong>{current.label}</strong>
          </div>
          <div className="environment">
            <span>조회 환경</span>
            <div className="segmented" aria-label="조회 환경">
              {(["test", "live"] as const).map((value) => (
                <button
                  key={value}
                  disabled={specBusy}
                  className={env === value ? `selected ${value}` : ""}
                  aria-pressed={env === value}
                  onClick={() => {
                    if (env !== value) leave(() => onEnv(value));
                  }}
                >
                  {value === "test" ? "TEST" : "LIVE"}
                </button>
              ))}
            </div>
            {emulator && <Badge tone="warning">로컬 에뮬레이터</Badge>}
          </div>
        </header>
        <main className="main-content">
          <div className="page-heading">
            <div>
              <span className="eyebrow">
                {env === "test" ? "TEST ENVIRONMENT" : "LIVE ENVIRONMENT"}
              </span>
              <h1>{current.label}</h1>
              <p>{current.caption}</p>
            </div>
            {page !== "players" && page !== "specs" && page !== "matches" && (
              <button
                onClick={() => setRefresh((value) => value + 1)}
                disabled={busy}
              >
                <Icon name="refresh" />
                새로고침
              </button>
            )}
          </div>
          {page === "matches" ? (
            <MatchStats env={env} />
          ) : page === "specs" ? (
            <SpecManager
              env={env}
              onDirtyChange={setHasDraft}
              onBusyChange={setSpecBusy}
              onPublished={() => setRefresh((value) => value + 1)}
            />
          ) : page === "players" ? (
            <PlayerSearch env={env} />
          ) : (
            <>
              {error && (
                <ErrorBox
                  message={error}
                  retry={() => setRefresh((value) => value + 1)}
                />
              )}
              {busy && <Loading />}
              {overview && (
                <>
                  <div className="snapshot-line">
                    <Badge tone="success">조회 완료</Badge>
                    <span>
                      {time(overview.fetchedAtMs)} 기준 · 자동 갱신 꺼짐
                    </span>
                  </div>
                  {page === "overview" && (
                    <OverviewPage data={overview} onPage={setPage} />
                  )}
                  {page === "content" && <ContentPage data={overview} />}
                  {page === "replay" && (
                    <ReplayPanel
                      days={overview.replay.days}
                      enabled={overview.replay.enabled}
                      expanded
                    />
                  )}
                </>
              )}
              {page === "overview" && <ConsoleLinks />}
            </>
          )}
          <footer className="workspace-footer">
            <span>
              {projectId} <b>·</b> 서울 리전 <b>·</b> cardbattle
            </span>
            <span>Card Battle Operations / 03</span>
          </footer>
        </main>
      </div>
    </div>
  );
}

function OverviewPage({
  data,
  onPage,
}: {
  data: Overview;
  onPage: (page: Page) => void;
}) {
  const days = data.replay.days;
  const hasStats = days.some((day) => day.exists);
  const total = days.reduce((sum, day) => sum + day.settled, 0);
  return (
    <>
      <div className="metrics-grid">
        <Metric
          title="공개 콘텐츠"
          value={label(data.content?.contentVersion)}
          note={
            data.content
              ? `최소 콘텐츠 세대 ${label(data.content.minAppMajor)}`
              : "공개 인덱스 없음"
          }
          tone="accent"
        />
        <Metric
          title="최신 앱 버전"
          value={label(data.appPolicy?.latest)}
          note={`최소 지원 ${label(data.appPolicy?.minSupported)}`}
        />
        <Metric
          title="서버 전투 검증"
          value={
            data.replay.enabled == null
              ? "미설정"
              : data.replay.enabled
                ? "활성"
                : "비활성"
          }
          note="현재 환경 설정 · 서버 반영 최대 60초"
        />
        <Metric
          title="최근 7일 정산"
          value={hasStats ? number.format(total) : "—"}
          note={
            hasStats
              ? "UTC 일별 집계 · 오늘 포함"
              : "아직 집계 데이터가 없습니다"
          }
        />
      </div>
      <div className="overview-grid">
        <ReplayPanel days={days} enabled={data.replay.enabled} />
        <section className="panel quick-panel">
          <div className="panel-heading">
            <h2>빠른 조회</h2>
            <span>자주 찾는 정보</span>
          </div>
          <button className="quick-action" onClick={() => onPage("players")}>
            <div className="quick-icon">
              <Icon name="players" />
            </div>
            <div>
              <strong>유저 정보 확인</strong>
              <p>UID로 재화, 카드, 진행도 조회</p>
            </div>
            <Icon name="arrow" />
          </button>
          <button className="quick-action" onClick={() => onPage("content")}>
            <div className="quick-icon">
              <Icon name="content" />
            </div>
            <div>
              <strong>배포된 콘텐츠 확인</strong>
              <p>버전, 테이블, 앱 정책 확인</p>
            </div>
            <Icon name="arrow" />
          </button>
          <div className="subtle-note">
            환경을 전환하면 해당 환경의 데이터를 새로 조회합니다.
          </div>
        </section>
      </div>
    </>
  );
}

function ReplayPanel({
  days,
  enabled,
  expanded = false,
}: {
  days: ReplayDay[];
  enabled: boolean | null;
  expanded?: boolean;
}) {
  const ordered = [...days].reverse();
  const max = Math.max(1, ...ordered.map((day) => day.settled));
  const sum = (
    key: "replayOk" | "divergent" | "replayFailed" | "unavailable",
  ) => days.reduce((total, day) => total + day[key], 0);
  const hasStats = days.some((day) => day.exists);
  return (
    <section className="panel replay-panel">
      <div className="panel-heading">
        <div>
          <h2>전투 검증 추이</h2>
          <span>최근 7일 · UTC · 오늘 포함</span>
        </div>
        <Badge tone={enabled ? "success" : "neutral"}>
          {enabled == null
            ? "설정 없음"
            : enabled
              ? "검증 활성"
              : "검증 비활성"}
        </Badge>
      </div>
      {hasStats ? (
        <>
          <div
            className="chart"
            role="img"
            aria-label="최근 7일 일별 정산 건수"
          >
            {ordered.map((day) => (
              <div className="chart-column" key={day.day}>
                <span>{day.exists ? number.format(day.settled) : "—"}</span>
                <div className="bar-track">
                  <div
                    className={day.exists ? "bar" : "bar missing"}
                    style={{
                      height: `${day.exists ? Math.max(day.settled > 0 ? 3 : 0, (day.settled / max) * 100) : 0}%`,
                    }}
                  />
                </div>
                <small>{day.day.slice(5).replace("-", ".")}</small>
              </div>
            ))}
          </div>
          <div className="replay-summary">
            <span>
              <i className="dot success" />
              재생 성공 <b>{number.format(sum("replayOk"))}</b>
            </span>
            <span>
              <i className="dot warning" />
              불일치 <b>{number.format(sum("divergent"))}</b>
            </span>
            <span>
              <i className="dot danger" />
              재생 실패 <b>{number.format(sum("replayFailed"))}</b>
            </span>
            <span>
              <i className="dot" />
              서비스 불가 <b>{number.format(sum("unavailable"))}</b>
            </span>
          </div>
        </>
      ) : (
        <Empty title="아직 집계 데이터가 없습니다">
          전투 정산이 집계되면 이곳에 표시됩니다.
        </Empty>
      )}
      {expanded && (
        <>
          <p className="table-note">
            재생 성공에는 판정 불일치가 포함될 수 있습니다. 각 카운터는 서로
            배타적인 비율이 아닙니다.
          </p>
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>날짜 (UTC)</th>
                  <th>정산</th>
                  <th>재생 성공</th>
                  <th>불일치</th>
                  <th>재생 실패</th>
                  <th>서비스 불가</th>
                  <th>결과 불일치</th>
                  <th>해시 불일치</th>
                </tr>
              </thead>
              <tbody>
                {days.map((day) => (
                  <tr key={day.day}>
                    <td>{day.day}</td>
                    {(
                      [
                        "settled",
                        "replayOk",
                        "divergent",
                        "replayFailed",
                        "unavailable",
                        "outcomeMismatch",
                        "hashMismatch",
                      ] as const
                    ).map((key) => (
                      <td key={key}>
                        {day.exists ? number.format(day[key]) : "—"}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="table-note">
            — 는 집계 문서 없음입니다. Cloud Run 실행 상태와 배포 버전은
            콘솔에서 확인할 수 있습니다.
          </p>
          <a
            className="text-link"
            href={links[4].href}
            target="_blank"
            rel="noopener noreferrer"
          >
            Cloud Run 콘솔 열기 <Icon name="arrow" />
          </a>
        </>
      )}
    </section>
  );
}

function ContentPage({ data }: { data: Overview }) {
  const tables = record(data.content?.tables);
  return (
    <>
      <div className="metrics-grid three">
        <Metric
          title="공개 콘텐츠"
          value={label(data.content?.contentVersion)}
          note={`최소 콘텐츠 세대 ${label(data.content?.minAppMajor)}`}
          tone="accent"
        />
        <Metric
          title="최신 앱"
          value={label(data.appPolicy?.latest)}
          note="스토어 권장 버전"
        />
        <Metric
          title="최소 지원 앱"
          value={label(data.appPolicy?.minSupported)}
          note="앱 진입에 필요한 버전"
        />
      </div>
      <section className="panel">
        <div className="panel-heading">
          <h2>공개 테이블</h2>
          <span>{Object.keys(tables).length}개 · 현재 인덱스 기준</span>
        </div>
        {Object.keys(tables).length ? (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>테이블</th>
                  <th>리비전</th>
                  <th>페이로드 해시</th>
                </tr>
              </thead>
              <tbody>
                {Object.entries(tables).map(([name, value]) => {
                  const pin = record(value);
                  return (
                    <tr key={name}>
                      <td>
                        <strong>{name}</strong>
                      </td>
                      <td>{label(pin.revision)}</td>
                      <td className="hash" title={label(pin.payloadHash)}>
                        {label(pin.payloadHash)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <Empty title="공개 테이블이 없습니다">
            공개 인덱스의 등록 상태를 확인해 주세요.
          </Empty>
        )}
      </section>
      <section className="panel">
        <div className="panel-heading">
          <h2>앱 버전 정책</h2>
          <span>현재 환경</span>
        </div>
        {data.appPolicy ? (
          <>
            <dl className="info-grid">
              <div>
                <dt>공지 제목</dt>
                <dd>{label(data.appPolicy.noticeTitle)}</dd>
              </div>
              <div>
                <dt>공지 ID</dt>
                <dd>{label(data.appPolicy.noticeId)}</dd>
              </div>
            </dl>
            {typeof data.appPolicy.noticeBody === "string" && (
              <p className="notice-body">{data.appPolicy.noticeBody}</p>
            )}
            <JsonDetails title="앱 정책 상세" data={data.appPolicy} />
          </>
        ) : (
          <Empty title="앱 버전 정책이 없습니다" />
        )}
      </section>
      {data.content && (
        <JsonDetails title="콘텐츠 인덱스 상세" data={data.content} />
      )}
    </>
  );
}

function PlayerSearch({ env }: { env: Env }) {
  const [uid, setUid] = useState("");
  const [player, setPlayer] = useState<Player | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const generation = useRef(0);
  useEffect(
    () => () => {
      generation.current++;
    },
    [],
  );
  async function search(event: FormEvent) {
    event.preventDefault();
    const target = uid.trim();
    const ticket = ++generation.current;
    setPlayer(null);
    setError("");
    if (
      !target ||
      target.length > 128 ||
      /[\/\s]/.test(target) ||
      target === "." ||
      target === ".."
    ) {
      setBusy(false);
      setError("공백이나 / 없이 정확한 UID를 입력해 주세요.");
      return;
    }
    setBusy(true);
    try {
      const data = await api!.player(env, target);
      if (ticket === generation.current) setPlayer(data);
    } catch (cause) {
      if (ticket === generation.current) setError(errorMessage(cause));
    } finally {
      if (ticket === generation.current) setBusy(false);
    }
  }
  return (
    <>
      <section className="panel search-panel">
        <form onSubmit={search}>
          <label htmlFor="uid">Firebase UID</label>
          <div className="search-row">
            <div className="search-input">
              <Icon name="search" />
              <input
                id="uid"
                value={uid}
                onChange={(event) => setUid(event.target.value)}
                placeholder="조회할 유저의 UID를 입력하세요"
                autoComplete="off"
                spellCheck={false}
                maxLength={128}
                required
              />
            </div>
            <button className="primary" type="submit">
              <Icon name="search" />
              {busy ? "다시 조회" : "유저 조회"}
            </button>
          </div>
          <p>
            정확한 UID로 조회합니다. 계정 인증 정보는 공통이며 게임 데이터는{" "}
            {env.toUpperCase()} 환경 기준입니다.
          </p>
        </form>
      </section>
      {error && <ErrorBox message={error} />}
      {busy && <Loading />}
      {!player && !busy && !error && (
        <section className="panel">
          <Empty title="확인할 유저를 찾아보세요">
            재화, 보유 카드, 덱, 성장과 모험 진행도를 확인할 수 있습니다.
          </Empty>
        </section>
      )}
      {player && <PlayerDetails player={player} />}
    </>
  );
}

function PlayerDetails({ player }: { player: Player }) {
  const save = player.save;
  const profile = record(save?.profile);
  const ownership = array(record(save?.ownership).cardIds);
  const balances = record(player.wallet?.balances);
  const slots = array(record(save?.deck).slots);
  const sections: [string, string][] = [
    ["profile", "프로필"],
    ["ownership", "카드 소유"],
    ["cardGrowth", "카드 성장"],
    ["deck", "덱 편성"],
    ["rank", "랭크"],
    ["adventure", "모험"],
    ["albumReward", "도감 보상"],
    ["tutorial", "튜토리얼"],
  ];
  return (
    <>
      <div className="snapshot-line">
        <Badge tone="success">조회 완료</Badge>
        <span>{time(player.fetchedAtMs)} 기준</span>
      </div>
      <section className="panel player-heading">
        <div className="player-avatar">
          <Icon name="players" />
        </div>
        <div>
          <h2>
            {label(
              profile.nickname || player.account?.displayName || "닉네임 없음",
            )}
          </h2>
          <code>{player.uid}</code>
        </div>
        <Badge
          tone={
            !player.account
              ? "warning"
              : player.account.disabled
                ? "danger"
                : "success"
          }
        >
          {!player.account
            ? "인증 계정 없음"
            : player.account.disabled
              ? "계정 사용 중지"
              : "계정 활성"}
        </Badge>
      </section>
      {!save && (
        <div className="notice" role="status">
          이 환경에는 세이브가 없습니다. 인증 계정과 지갑의 존재 여부는 별도로
          표시됩니다.
        </div>
      )}
      <div className="metrics-grid">
        <Metric
          title="골드"
          value={label(balances.Gold)}
          note={
            player.wallet
              ? `지갑 리비전 ${label(player.wallet.rev)}`
              : "지갑 문서 없음"
          }
          tone="accent"
        />
        <Metric
          title="샤드"
          value={label(balances.Shard)}
          note="서버 지갑 잔액"
        />
        <Metric
          title="카드 가루"
          value={label(balances.CardDust)}
          note="서버 지갑 잔액"
        />
        <Metric
          title="보유 카드"
          value={save ? number.format(ownership.length) : "—"}
          note="카드 종류 수"
        />
      </div>
      <section className="panel">
        <div className="panel-heading">
          <h2>계정 정보</h2>
          <span>Firebase Authentication</span>
        </div>
        <dl className="info-grid">
          <div>
            <dt>이메일</dt>
            <dd>{player.account?.email || "—"}</dd>
          </div>
          <div>
            <dt>로그인 방식</dt>
            <dd>
              {player.account
                ? player.account.providers.join(", ") || "익명 계정"
                : "—"}
            </dd>
          </div>
          <div>
            <dt>계정 생성</dt>
            <dd>{time(player.account?.createdAt)}</dd>
          </div>
          <div>
            <dt>최근 로그인</dt>
            <dd>{time(player.account?.lastSignInAt)}</dd>
          </div>
          <div>
            <dt>세이브 갱신</dt>
            <dd>{time(save?.updatedAt)}</dd>
          </div>
          <div>
            <dt>세이브 리비전</dt>
            <dd>{label(save?.revision)}</dd>
          </div>
          <div>
            <dt>계정 경험치</dt>
            <dd>{label(profile.accountExp)}</dd>
          </div>
          <div>
            <dt>랭크 점수</dt>
            <dd>{label(record(save?.rank).points)}</dd>
          </div>
        </dl>
      </section>
      {slots.length > 0 && (
        <section className="panel">
          <div className="panel-heading">
            <h2>편성 덱</h2>
            <span>{slots.length}개 슬롯</span>
          </div>
          <div className="deck-grid">
            {slots.map((slot, index) => {
              const deck = record(slot);
              return (
                <article key={index} className="deck">
                  <div>
                    <strong>{label(deck.name || `덱 ${index + 1}`)}</strong>
                    {record(save?.deck).selectedSlot === index && (
                      <Badge tone="success">선택됨</Badge>
                    )}
                  </div>
                  <div className="card-ids">
                    {array(deck.cardIds).map((id, position) => (
                      <span key={position}>#{label(id)}</span>
                    ))}
                  </div>
                </article>
              );
            })}
          </div>
        </section>
      )}
      {save && (
        <section className="panel">
          <div className="panel-heading">
            <h2>게임 데이터 상세</h2>
            <span>저장된 값 기준</span>
          </div>
          {sections
            .filter(([key]) => save[key] !== undefined)
            .map(([key, title]) => (
              <JsonDetails key={key} title={title} data={save[key]} />
            ))}
          <p className="table-note">
            미션·패스·출석 등 별도 문서의 진행도는 이 조회에 포함되지 않습니다.
          </p>
        </section>
      )}
      {player.wallet && <JsonDetails title="지갑 상세" data={player.wallet} />}
    </>
  );
}
