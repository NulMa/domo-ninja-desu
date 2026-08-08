// domo-ninja-desu 운영 측정 Worker (25_운영측정_계획.md §6-1)
//
// 엔드포인트 둘뿐이다.
//   POST /ingest — 런 하나를 적재. 게임이 fire-and-forget 으로 부른다
//   GET  /stats  — 집계 JSON. 이 응답을 그대로 기술 문서 대조표에 넣는다
//
// ★ 이 파일의 지표 정의는 sim/Metrics.cs 를 그대로 따라간다.
//   `M3a`·`M3b`·`M3c` 는 sim 에서 **클리어한 런만** 분모로 쓴다(Metrics.cs:148,172,198).
//   여기서 분모를 전체 런으로 잡으면 대조표의 두 열이 서로 다른 질문의 답이 된다.

const MAX_BODY_BYTES = 8 * 1024;
const MAX_ROWS = 20000;
const STAGES = new Set(['S1', 'S2']);

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === 'OPTIONS') return preflight();

    if (url.pathname === '/ingest' && request.method === 'POST') return ingest(request, env);
    if (url.pathname === '/stats' && request.method === 'GET') return stats(url, env);
    if (url.pathname === '/') return index();

    return json({ error: 'not_found' }, 404);
  },
};

// ── CORS ──────────────────────────────────────────────────────────────
//
// ★ 와일드카드를 쓴다. 쿠키도 인증도 없고 /stats 는 공개해도 되는 집계값이라
//   오리진을 좁혀서 얻는 게 없다. 반대로 좁히면 gh-pages URL 이 바뀌거나
//   로컬에서 열어볼 때 조용히 막히고, **막힌 것과 데이터가 없는 것이 화면상 같다** —
//   GA 를 접은 이유(§4 ①)와 정확히 같은 함정을 자기 손으로 만드는 꼴이 된다.
const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
  'Access-Control-Max-Age': '86400',
};

const preflight = () => new Response(null, { status: 204, headers: CORS });

const json = (body, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS, 'Content-Type': 'application/json; charset=utf-8' },
  });

const index = () =>
  new Response(
    'domo-ninja-desu telemetry\n\n' +
      '  POST /ingest\n' +
      '  GET  /stats[?stage=S1&app_ver=...&denominator=cleared|all]\n\n' +
      'https://github.com/NulMa/domo-ninja-desu\n',
    { status: 200, headers: { ...CORS, 'Content-Type': 'text/plain; charset=utf-8' } }
  );

// ── POST /ingest ──────────────────────────────────────────────────────

async function ingest(request, env) {
  const raw = await request.text();
  if (raw.length > MAX_BODY_BYTES) return json({ error: 'too_large' }, 413);

  let payload;
  try {
    payload = JSON.parse(raw);
  } catch {
    return json({ error: 'bad_json' }, 400);
  }

  const rec = validate(payload);
  if (rec.error) return json(rec, 400);

  // D1 이 일시적으로 실패할 수 있다(콜드 스타트·리전 전환). 그때 예외가 그대로 새면
  // 500 이 나가는데, 클라이언트는 응답을 안 보므로 **화면상 성공과 구분되지 않는다.**
  // 레코드는 어차피 잃지만, 최소한 어느 쪽으로 잃었는지는 로그에 남겨야 한다.
  try {
    await env.DB.prepare(
      `INSERT INTO runs (session_id, ts, stage, cleared, rounds_won, roster, team_items, app_ver)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
    )
      .bind(
        rec.sessionId,
        Date.now(),
        rec.stage,
        rec.cleared ? 1 : 0,
        rec.roundsWon,
        JSON.stringify(rec.roster),
        JSON.stringify(rec.teamItems),
        rec.appVer
      )
      .run();
  } catch (e) {
    console.error('ingest insert failed', { stage: rec.stage, message: String(e) });
    return json({ error: 'storage_unavailable' }, 503);
  }

  return json({ ok: true });
}

/**
 * 들어온 것을 그대로 믿지 않는다. 공개 엔드포인트라 누구나 아무거나 넣을 수 있고,
 * 쓰레기가 한 번 섞이면 대조표에서 그것만 골라내기가 어렵다.
 * 대신 **버리는 것보다 자르는 쪽**을 택한다 — 형식이 조금 어긋났다고 런 하나를 통째로
 * 날리면 이미 적은 표본이 더 줄어든다.
 */
function validate(p) {
  if (!p || typeof p !== 'object') return { error: 'bad_payload' };
  if (p.v !== 1) return { error: 'bad_version' };

  const sessionId = str(p.sid, 64);
  if (!sessionId) return { error: 'bad_sid' };

  const stage = str(p.stage, 8);
  if (!stage || !STAGES.has(stage)) return { error: 'bad_stage' };

  const roundsWon = int(p.roundsWon, 0, 100);
  if (roundsWon === null) return { error: 'bad_roundsWon' };

  if (!Array.isArray(p.roster) || p.roster.length === 0 || p.roster.length > 3)
    return { error: 'bad_roster' };

  const roster = [];
  for (const e of p.roster) {
    if (!e || typeof e !== 'object') return { error: 'bad_roster_entry' };
    const c = str(e.c, 32);
    if (!c) return { error: 'bad_character' };

    roster.push({
      c,
      active: str(e.active, 64), // 아직 안 골랐으면 null 이다 — 정상 상태다
      support: ids(e.support, 4),
      items: ids(e.items, 24),
    });
  }

  return {
    sessionId,
    stage,
    cleared: p.cleared === true,
    roundsWon,
    roster,
    teamItems: ids(p.teamItems, 24),
    appVer: str(p.appVer, 32) ?? '',
  };
}

const str = (v, max) => (typeof v === 'string' && v.length > 0 && v.length <= max ? v : null);

const int = (v, lo, hi) =>
  typeof v === 'number' && Number.isInteger(v) && v >= lo && v <= hi ? v : null;

const ids = (v, max) =>
  Array.isArray(v) ? v.map((x) => str(x, 64)).filter(Boolean).slice(0, max) : [];

// ── GET /stats ────────────────────────────────────────────────────────

async function stats(url, env) {
  const stage = url.searchParams.get('stage');
  const appVer = url.searchParams.get('app_ver');
  const denominator = url.searchParams.get('denominator') === 'all' ? 'all' : 'cleared';

  const where = [];
  const args = [];
  if (stage) {
    where.push('stage = ?');
    args.push(stage);
  }
  if (appVer) {
    where.push('app_ver = ?');
    args.push(appVer);
  }

  const sql =
    `SELECT session_id, stage, cleared, rounds_won, roster FROM runs` +
    (where.length ? ` WHERE ${where.join(' AND ')}` : '') +
    ` ORDER BY id DESC LIMIT ${MAX_ROWS}`;

  const { results } = await env.DB.prepare(sql).bind(...args).all();

  // ★ SQL 로 집계하지 않고 JS 로 접는다. 표본이 수백 건 규모라(§7) 전 행을 읽어도
  //   무료 티어 근처도 안 가고, json_each 로 짜맞춘 SQL 보다 sim/Metrics.cs 와
  //   나란히 놓고 읽기가 쉽다. 수만 건을 넘기면 그때 SQL 집계로 옮긴다.
  const rows = results.map((r) => ({
    sessionId: r.session_id,
    stage: r.stage,
    cleared: r.cleared === 1,
    roundsWon: r.rounds_won,
    roster: safeParse(r.roster),
  }));

  const basis = denominator === 'all' ? rows : rows.filter((r) => r.cleared);

  return json({
    generatedAt: new Date().toISOString(),
    filter: { stage: stage ?? null, appVer: appVer ?? null, denominator },
    sample: {
      runs: rows.length,
      cleared: rows.filter((r) => r.cleared).length,
      sessions: new Set(rows.map((r) => r.sessionId)).size,
      basis: basis.length,
    },
    M1: {
      clearRate: rows.length ? round(rows.filter((r) => r.cleared).length / rows.length) : 0,
      runs: rows.length,
    },
    M3a: m3a(basis),
    M3b: m3b(basis),
    M3c: m3c(basis),
    note:
      'M3a/M3b/M3c 의 분모는 ' +
      (denominator === 'all' ? '전체 런 (sim 과 다름 — 표본이 적을 때만 참고)' : '클리어한 런 (sim/Metrics.cs 와 동일)') +
      '. 등장하지 않은 id 는 키 자체가 없다 — sim 은 0 으로 채운다.',
  });
}

/** 캐릭터 출전률. sim: Metrics.M3a */
function m3a(runs) {
  const out = {};
  if (!runs.length) return out;

  for (const id of charIds(runs)) {
    out[id] = round(runs.filter((r) => has(r, id)).length / runs.length);
  }
  return out;
}

/**
 * 액티브 2택 선택 비율. sim: Metrics.M3b
 * 분모는 **그 캐릭터가 나온 런**이다 — 전체로 잡으면 출전률이 섞여 들어온다.
 */
function m3b(runs) {
  const out = {};

  for (const id of charIds(runs)) {
    const used = runs.filter((r) => has(r, id));
    if (!used.length) continue;

    const per = {};
    for (const r of used) {
      const skill = r.roster.find((e) => e.c === id)?.active;
      if (!skill) continue;
      per[skill] = (per[skill] ?? 0) + 1;
    }
    for (const k of Object.keys(per)) per[k] = round(per[k] / used.length);

    out[id] = per;
  }
  return out;
}

/** 보조 스킬 채택률. sim: Metrics.M3c — 분모는 그 스킬 주인 캐릭터가 나온 런. */
function m3c(runs) {
  const owner = new Map(); // supportId → characterId
  for (const r of runs) {
    for (const e of r.roster) for (const s of e.support) owner.set(s, e.c);
  }

  const out = {};
  for (const [supportId, charId] of owner) {
    const used = runs.filter((r) => has(r, charId));
    if (!used.length) continue;

    const took = used.filter((r) =>
      r.roster.some((e) => e.c === charId && e.support.includes(supportId))
    ).length;

    out[supportId] = round(took / used.length);
  }
  return out;
}

const charIds = (runs) => {
  const s = new Set();
  for (const r of runs) for (const e of r.roster) s.add(e.c);
  return [...s].sort();
};

const has = (run, charId) => run.roster.some((e) => e.c === charId);

const round = (x) => Math.round(x * 10000) / 10000;

function safeParse(s) {
  try {
    const v = JSON.parse(s);
    return Array.isArray(v) ? v : [];
  } catch {
    return [];
  }
}
