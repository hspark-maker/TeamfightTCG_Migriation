import { useEffect, useState } from "react";
import {
  api,
  errorMessage,
  type Env,
  type MatchStatsData,
  type MatchPeriod,
} from "./api";
import {
  groupDaily,
  presetRange,
  rangeError,
  utcDay,
  type ChartUnit,
  type DateRange,
} from "./match-periods";
import "./match-stats.css";

const count = (value: number) => value.toLocaleString("ko-KR");
const modeNames = {
  ai: "AI 대전",
  adventure: "모험",
  pvp: "PvP",
  unknown: "미확인",
};
const outcomeNames = {
  win: "승리",
  loss: "패배",
  draw: "무승부",
  pvp: "PvP 정산",
  unknown: "미확인",
};
const statusNames = {
  confirmed: "정산 확인",
  flagged: "확인 필요",
  other: "기타",
};

function winRate(wins: number, losses: number) {
  return wins + losses > 0
    ? `${((wins / (wins + losses)) * 100).toFixed(1)}%`
    : "—";
}

function kstTime(value: number) {
  return new Intl.DateTimeFormat("ko-KR", {
    timeZone: "Asia/Seoul",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hourCycle: "h23",
  }).format(value);
}

const unitNames = { day: "일별", week: "주별", month: "월별" };
const utcTime = (value: number) =>
  new Date(value).toISOString().slice(0, 16).replace("T", " ");

function DailyChart({
  daily,
  unit,
  setUnit,
}: {
  daily: MatchStatsData["daily"];
  unit: ChartUnit;
  setUnit: (unit: ChartUnit) => void;
}) {
  const groups = groupDaily(daily, unit);
  const maximum = Math.max(1, ...groups.map((group) => group.settled ?? 0));
  const shortDate = (day: string) => day.slice(5).replace("-", "/");
  return (
    <section
      className="panel match-daily-panel"
      aria-labelledby="match-daily-title"
    >
      <div className="panel-heading match-chart-heading">
        <div>
          <h2 id="match-daily-title">{unitNames[unit]} 정산 추이</h2>
          <span>일별 집계 문서 기준 · UTC 날짜 · 오늘은 조회 시점까지</span>
        </div>
        <div
          className="segmented match-unit"
          role="group"
          aria-label="추이 집계 단위"
        >
          {(["day", "week", "month"] as const).map((value) => (
            <button
              type="button"
              key={value}
              className={unit === value ? "selected" : ""}
              aria-pressed={unit === value}
              onClick={() => setUnit(value)}
            >
              {unitNames[value]}
            </button>
          ))}
        </div>
      </div>
      <div
        className="match-chart-scroll"
        role="region"
        aria-label="정산 추이 차트"
        tabIndex={0}
      >
        <div
          className={`match-chart${unit !== "day" ? " match-chart-grouped" : ""}`}
          role="list"
          aria-label="날짜별 정산 건수"
        >
          {groups.map((group) => {
            const known = group.settled !== null;
            const partial =
              group.knownDays > 0 && group.knownDays < group.expectedDays;
            const span =
              group.startDate === group.endDate
                ? group.startDate
                : `${group.startDate} ~ ${group.endDate}`;
            return (
              <div
                className={`match-chart-column${known ? "" : " unavailable"}${partial ? " partial" : ""}`}
                key={group.key}
                role="listitem"
                aria-label={`${span} UTC: ${known ? `${partial ? "소계 " : ""}${count(group.settled!)}건` : "집계 없음"}${partial ? `, 부분 집계 ${group.knownDays}/${group.expectedDays}일` : ""}`}
              >
                <strong aria-hidden="true">
                  {known
                    ? `${partial ? "소계 " : ""}${count(group.settled!)}`
                    : "—"}
                </strong>
                <div className="match-bar-track" aria-hidden="true">
                  {known ? (
                    <div
                      className="match-bar"
                      style={{ height: `${(group.settled! / maximum) * 100}%` }}
                    />
                  ) : (
                    <span className="match-bar-missing">—</span>
                  )}
                </div>
                <span aria-hidden="true">
                  {group.startDate === group.endDate
                    ? shortDate(group.startDate)
                    : `${shortDate(group.startDate)} ~ ${shortDate(group.endDate)}`}
                </span>
                {partial && (
                  <small className="match-partial" aria-hidden="true">
                    부분 집계 {group.knownDays}/{group.expectedDays}일
                  </small>
                )}
              </div>
            );
          })}
        </div>
      </div>
      <p className="match-note">
        주별은 월요일 시작, 월별은 달력 기준이며 선택 기간에 포함된 날짜만
        합산합니다. 집계가 없는 날은 —, 일부 날짜만 확인되면 소계와 확인 일수를
        표시합니다. 상세 분석은 보관 범위 내 최신 500건까지입니다.
      </p>
    </section>
  );
}

function Statistics({
  data,
  unit,
  setUnit,
}: {
  data: MatchStatsData;
  unit: ChartUnit;
  setUnit: (unit: ChartUnit) => void;
}) {
  const { summary } = data;
  const detailAvailable = data.detailAvailable !== false;
  return (
    <>
      <div className="snapshot-line match-snapshot">
        <span
          className={`badge ${data.env === "test" ? "success" : "warning"}`}
        >
          <i />
          {data.env.toUpperCase()}
        </span>
        <span>
          조회 기간 UTC {data.startDate ?? utcDay(data.fromMs)} ~{" "}
          {data.endDate ?? utcDay(data.toMs)}
        </span>
        <span>조회 {kstTime(data.fetchedAtMs)} KST</span>
        {detailAvailable && (
          <span>
            상세 분석 {count(data.sampleSize)}건 / 최대 {count(data.limit)}건
          </span>
        )}
      </div>
      {data.detailLimited && (
        <div className="notice" role="status">
          선택 기간 중 상세 기록은 최근 7일 보관 범위에서만 분석합니다. 과거
          날짜는 일별 정산 집계만 조회할 수 있습니다.
        </div>
      )}
      {detailAvailable &&
        data.detailFromMs != null &&
        data.detailToMs != null && (
          <p className="match-detail-range">
            승률·평균 턴·모드별·최근 매치 분석 범위: UTC{" "}
            {utcTime(data.detailFromMs)} ~ {utcTime(data.detailToMs)} · 최대{" "}
            {count(data.limit)}건
          </p>
        )}
      {detailAvailable && data.hasMore && (
        <div className="notice" role="status">
          이 기간의 상세 매치가 {count(data.limit)}건을 넘습니다. 승률·평균
          턴·모드별 수치는 최신 {count(data.sampleSize)}건만 분석한 결과입니다.
        </div>
      )}
      {detailAvailable && (
        <div className="metrics-grid match-metrics" aria-label="매치 상세 분석">
          <article className="metric accent">
            <span>분석한 매치</span>
            <strong>
              {count(data.sampleSize)}
              <em>건</em>
            </strong>
            <small>상세 보관 범위 내 최신 {count(data.limit)}건까지</small>
          </article>
          <article className="metric">
            <span>AI전 승률 · 모험 포함</span>
            <strong>{winRate(summary.aiWins, summary.aiLosses)}</strong>
            <small>
              {count(summary.aiWins)}승 / 승패 확인{" "}
              {count(summary.aiWins + summary.aiLosses)}건
              <br />
              무승부 {count(summary.aiDraws)} · 미확인{" "}
              {count(summary.aiUnknown)} 제외
            </small>
          </article>
          <article className="metric">
            <span>평균 전투 턴</span>
            <strong>
              {summary.averageTurns === null
                ? "—"
                : summary.averageTurns.toFixed(1)}
              {summary.averageTurns !== null && <em>턴</em>}
            </strong>
            <small>서버 판정 확인 {count(summary.turnSamples)}건 기준</small>
          </article>
          <article
            className={`metric${summary.flagged > 0 ? " match-flagged" : ""}`}
          >
            <span>확인 필요한 매치</span>
            <strong>
              {count(summary.flagged)}
              <em>건</em>
            </strong>
            <small>
              정산 확인 {count(summary.confirmed)} · 기타 {count(summary.other)}
            </small>
          </article>
        </div>
      )}
      <DailyChart daily={data.daily} unit={unit} setUnit={setUnit} />
      {!detailAvailable ? (
        <section className="panel empty" aria-label="상세 보관 범위 밖">
          <h3>선택 기간이 상세 보관 범위 밖입니다</h3>
          <p>
            일별 정산 집계는 위에서 확인할 수 있습니다. 7일이 지난 매치의
            승률·평균 턴·모드별 상세는 제공하지 않습니다.
          </p>
        </section>
      ) : data.sampleSize === 0 ? (
        <section className="panel empty" aria-label="매치 상세 없음">
          <h3>이 기간에 조회된 매치 상세가 없습니다</h3>
          <p>
            조회 환경과 기간을 확인하세요. 상세 기록은 7일간 보관되며, 일별
            집계와 별도로 조회됩니다.
          </p>
        </section>
      ) : (
        <>
          <section className="panel" aria-labelledby="match-mode-title">
            <div className="panel-heading">
              <div>
                <h2 id="match-mode-title">모드별 매치</h2>
                <span>
                  분석한 {count(data.sampleSize)}건 기준 · 서버 재현 결과{" "}
                  {count(summary.replayed)}건
                </span>
              </div>
            </div>
            <div
              className="table-scroll"
              tabIndex={0}
              role="region"
              aria-label="모드별 매치 통계 표"
            >
              <table>
                <thead>
                  <tr>
                    <th scope="col">모드</th>
                    <th scope="col">매치</th>
                    <th scope="col">정산 확인</th>
                    <th scope="col">확인 필요</th>
                    <th scope="col">승률</th>
                    <th scope="col">승 / 패 / 무승부</th>
                  </tr>
                </thead>
                <tbody>
                  {data.modes.map((mode) => (
                    <tr key={mode.mode}>
                      <th scope="row">{modeNames[mode.mode]}</th>
                      <td>{count(mode.total)}</td>
                      <td>{count(mode.confirmed)}</td>
                      <td>
                        <span className={mode.flagged ? "match-attention" : ""}>
                          {count(mode.flagged)}
                        </span>
                      </td>
                      <td>
                        {mode.mode === "ai" || mode.mode === "adventure"
                          ? winRate(mode.wins, mode.losses)
                          : "—"}
                      </td>
                      <td title={`미확인 ${count(mode.unknown)}건`}>
                        {mode.mode === "ai" || mode.mode === "adventure"
                          ? `${count(mode.wins)} / ${count(mode.losses)} / ${count(mode.draws)}`
                          : "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p className="match-note">
              승률은 AI 대전과 모험의 승리 ÷ (승리 + 패배)입니다. 무승부·미확인
              결과는 제외하며, PvP의 전체 승률은 표시하지 않습니다.
            </p>
          </section>
          <section className="panel" aria-labelledby="match-recent-title">
            <div className="panel-heading">
              <div>
                <h2 id="match-recent-title">최근 매치</h2>
                <span>
                  정산 시각순 최신 {count(data.recent.length)}건 · 최대 50건 ·
                  시각은 KST
                </span>
              </div>
            </div>
            <div
              className="table-scroll"
              tabIndex={0}
              role="region"
              aria-label="최근 매치 목록 표"
            >
              <table className="match-recent-table">
                <thead>
                  <tr>
                    <th scope="col">매치 ID</th>
                    <th scope="col">정산 시각 · KST</th>
                    <th scope="col">모드</th>
                    <th scope="col">상태</th>
                    <th scope="col">결과</th>
                    <th scope="col">턴</th>
                    <th scope="col">문제 사유</th>
                  </tr>
                </thead>
                <tbody>
                  {data.recent.map((match) => (
                    <tr key={match.id}>
                      <td>
                        <code className="match-id">{match.id}</code>
                      </td>
                      <td>{kstTime(match.settledAtMs)}</td>
                      <td>{modeNames[match.mode]}</td>
                      <td>
                        <span
                          className={`badge ${match.status === "flagged" ? "danger" : match.status === "confirmed" ? "success" : ""}`}
                        >
                          <i />
                          {statusNames[match.status]}
                        </span>
                      </td>
                      <td>{outcomeNames[match.outcome]}</td>
                      <td>{match.turns === null ? "—" : count(match.turns)}</td>
                      <td className="match-reason">{match.reason || "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
      <p className="match-retention">
        기간은 UTC 날짜 기준이며 양 끝 날짜를 포함합니다. 오늘은 조회 시점까지만
        반영합니다. 상세 기록은 최근 7일, 일별 집계는 최대 90일 범위를
        조회합니다. 과거 승률과 전투 턴은 일별 정산 건수에서 복원하지 않습니다.
      </p>
    </>
  );
}

const presets = [
  { key: "1", label: "오늘" },
  { key: "yesterday", label: "어제" },
  { key: "3", label: "3일" },
  { key: "7", label: "7일" },
  { key: "14", label: "14일" },
  { key: "30", label: "30일" },
  { key: "90", label: "90일" },
  { key: "week", label: "이번 주" },
  { key: "month", label: "이번 달" },
  { key: "custom", label: "직접 지정" },
];

export function MatchStats({ env }: { env: Env }) {
  const [unit, setUnit] = useState<ChartUnit>("day");
  const [preset, setPreset] = useState("7");
  const [period, setPeriod] = useState<MatchPeriod>(7);
  const [draft, setDraft] = useState<DateRange>(() => ({
    startDate: utcDay(Date.now() - 6 * 86_400_000),
    endDate: utcDay(Date.now()),
  }));
  const [draftError, setDraftError] = useState<string | null>(null);
  const [refresh, setRefresh] = useState(0);
  const requestKey = JSON.stringify([env, period, refresh]);
  const [result, setResult] = useState<{
    key: string;
    data: MatchStatsData | null;
    error: string | null;
  } | null>(null);
  const loading = result?.key !== requestKey;
  const current = result?.key === requestKey ? result.data : null;
  const error = result?.key === requestKey ? result.error : null;

  useEffect(() => {
    let active = true;
    if (!api) {
      setResult({
        key: requestKey,
        data: null,
        error: "Firebase 연결 설정을 확인해 주세요.",
      });
      return;
    }
    api.matches(env, period).then(
      (data) => {
        if (active) setResult({ key: requestKey, data, error: null });
      },
      (failure: unknown) => {
        if (active)
          setResult({
            key: requestKey,
            data: null,
            error: errorMessage(failure),
          });
      },
    );
    return () => {
      active = false;
    };
  }, [env, period, requestKey]);

  function choosePreset(key: string) {
    setPreset(key);
    setDraftError(null);
    if (key === "custom") return;
    if (key === "yesterday" || key === "week" || key === "month")
      setPeriod(presetRange(key));
    else setPeriod(Number(key));
  }

  function refreshData() {
    if (preset === "yesterday" || preset === "week" || preset === "month")
      setPeriod(presetRange(preset));
    setRefresh((value) => value + 1);
  }

  return (
    <div className="match-stats">
      <div className="match-toolbar">
        <div
          className="segmented match-period"
          role="group"
          aria-label="매치 조회 기간"
        >
          {presets.map(({ key, label }) => (
            <button
              type="button"
              key={key}
              className={preset === key ? "selected" : ""}
              aria-pressed={preset === key}
              onClick={() => choosePreset(key)}
            >
              {label}
            </button>
          ))}
        </div>
        <span className="match-period-note">
          UTC 기준 · 최근 N일은 오늘 포함
        </span>
        <button
          type="button"
          className="match-refresh"
          disabled={loading}
          onClick={refreshData}
        >
          매치 새로고침
        </button>
      </div>
      {preset === "custom" && (
        <form
          className="match-custom-range"
          onSubmit={(event) => {
            event.preventDefault();
            const message = rangeError(draft);
            setDraftError(message);
            if (!message) {
              setPeriod({ ...draft });
              setRefresh((value) => value + 1);
            }
          }}
          noValidate
        >
          <label>
            시작일
            <input
              type="date"
              aria-label="시작일"
              value={draft.startDate}
              max={utcDay(Date.now())}
              onChange={(event) => {
                setDraft({ ...draft, startDate: event.target.value });
                setDraftError(null);
              }}
            />
          </label>
          <label>
            종료일
            <input
              type="date"
              aria-label="종료일"
              value={draft.endDate}
              max={utcDay(Date.now())}
              onChange={(event) => {
                setDraft({ ...draft, endDate: event.target.value });
                setDraftError(null);
              }}
            />
          </label>
          <button type="submit">기간 적용</button>
          <span className="match-period-note">
            양 끝 날짜 포함 · 최대 90일 · 적용 전까지 기존 결과 유지
          </span>
          {draftError && (
            <div className="error match-range-error" role="alert">
              {draftError}
            </div>
          )}
        </form>
      )}
      {error && (
        <div className="error" role="alert">
          <span>{error}</span>
          <button type="button" onClick={refreshData}>
            다시 시도
          </button>
        </div>
      )}
      {loading && (
        <div className="panel loading" role="status">
          <span className="spinner" aria-hidden="true" />
          매치 통계를 불러오는 중입니다.
        </div>
      )}
      {!loading && current && (
        <Statistics data={current} unit={unit} setUnit={setUnit} />
      )}
    </div>
  );
}
