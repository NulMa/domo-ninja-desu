-- domo-ninja-desu 운영 측정 (25_운영측정_계획.md §6-1)
--
-- 런 하나 = 한 행. 라운드 단위로 쌓지 않는 이유는 요청 수를 줄이기 위해서다(§6-2).
-- 식별 정보는 넣지 않는다 — session_id 는 PlayerPrefs 의 익명 GUID 이고,
-- 같은 사람의 여러 런을 잇는 것 외의 용도가 없다(§5 개인정보).

CREATE TABLE IF NOT EXISTS runs (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,

  session_id TEXT    NOT NULL,           -- 익명 GUID. 사람 수 추정 · 중복 제보 판별용
  ts         INTEGER NOT NULL,           -- 서버 수신 시각(epoch ms). 클라이언트 시계를 믿지 않는다
  stage      TEXT    NOT NULL,           -- 'S1' | 'S2'
  cleared    INTEGER NOT NULL,           -- 0 | 1
  rounds_won INTEGER NOT NULL,

  -- RosterEntry 3명. [{ "c":"C1", "active":"...", "support":[...], "items":[...] }, ...]
  roster     TEXT    NOT NULL,
  team_items TEXT    NOT NULL DEFAULT '[]',

  -- ★ 이 칸이 없으면 재튜닝 전후 데이터가 한 통에 섞인다.
  --   밸런스가 바뀐 뒤의 클리어율과 바뀌기 전의 클리어율은 다른 게임의 숫자다.
  --   `M1` 을 스테이지 축 없이 봐서 틀렸던 것과 같은 종류의 실수다(CLAUDE.md D+8).
  app_ver    TEXT    NOT NULL DEFAULT ''
);

-- /stats 는 스테이지로 거른다. 표본이 수백 건이라 성능 때문은 아니고,
-- 스테이지별 조회가 기본 사용 방식이라 의도를 스키마에 남긴다.
CREATE INDEX IF NOT EXISTS idx_runs_stage    ON runs(stage);
CREATE INDEX IF NOT EXISTS idx_runs_app_ver  ON runs(app_ver);
