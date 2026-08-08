# telemetry — 운영 측정 엔드포인트

`docs/25_운영측정_계획.md` §6-1 의 구현. **게임 코드가 아니다** — 빌드에 들어가지 않는다.

## 무엇을 위한 것인가

★ **밸런스를 잡기 위한 게 아니다.** 심사자 몇 명이 두는 판은 표본이 안 되고,
`sim` 은 21,600런을 1.5초에 돈다. 여기서 얻는 건 **"예측 분포와 실측 분포를 나란히 놓는 자리"** 다
(`25` §1). 기술 문서에 *"사람 데이터로 밸런스를 잡았다"* 고 쓰면 표본 수 한 번에 무너진다.

## 엔드포인트

| | |
|---|---|
| `POST /ingest` | 런 하나 적재. 게임이 런 마감에 **한 번만** 부른다 |
| `GET /stats` | 집계 JSON. `?stage=S1` · `?app_ver=...` · `?denominator=all` |

`/stats` 의 지표 이름과 분모는 `sim/Metrics.cs` 를 따라간다 — `M3a`·`M3b`·`M3c` 는
**클리어한 런만** 분모로 쓴다. 이름이나 분모가 갈리면 대조표를 손으로 맞추게 된다(`25` §6 검증).

> ⚠️ 한 번도 등장하지 않은 캐릭터·스킬은 **키 자체가 없다.** `sim` 은 카탈로그를 알고 있어 0 을 채우지만
> 여기는 관측된 것만 안다. 카탈로그를 여기 복사하지 않는 쪽을 택했다 —
> `/data` 정본을 두 벌로 만들지 않는 게 이 저장소의 규칙이기 때문이다.

## 배포

```bash
cd telemetry
npm install

npx wrangler login                      # 브라우저 OAuth
npx wrangler d1 create domo-runs        # 출력된 database_id 를 wrangler.jsonc 에 붙인다

npm run db:local                        # 로컬 DB 에 스키마
npm run db:remote                       # ★ 실서버 DB 에 스키마 — 빼먹으면 배포는 되는데 INSERT 가 죽는다
npm run deploy
```

배포 후 확인:

```bash
curl https://domo-telemetry.jungwt524.workers.dev/stats
```

## 설계상 못 바꾸는 것

- **게임을 막지 않는다.** 클라이언트는 응답을 안 기다리고 실패를 삼킨다(`25` §5).
  네트워크가 느린 심사자의 화면이 멈추면 그건 "게임이 구리다"로 읽힌다
- **식별 정보를 안 받는다.** `session_id` 는 `PlayerPrefs` 의 익명 GUID 뿐이다
- **`app_ver` 을 반드시 채운다.** 재튜닝 전후 데이터가 섞이면 클리어율은 두 게임의 평균이 된다
- **무료 티어 안에서만** — Workers 10만 req/일, D1 쓰기 10만행/일. 표본 수백 건은 근처도 안 간다
