// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Economy;
using DomoNinja.Core.Events;
using DomoNinja.Core.Rng;
using UnityEngine;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 화면 전환 내내 살아있는 런 상태 보관소. <b>P5 UI 전체가 이걸 통해 core 를 만진다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <see cref="RunEngine.PlayRun"/> 은 <b>봇 전용</b>이다 — 상점 구매를 <c>ShopBot</c> 이
    /// 대신 결정해버려서 플레이어 입력이 끼어들 자리가 없다. 그래서 여기서는
    /// <see cref="RunEngine.PlayRound"/> 를 라운드 하나씩 직접 불러 플레이어 루프를 만든다.
    /// </para>
    /// <para>
    /// ⚠️ <b><see cref="_encounterRng"/>/<see cref="_shopRng"/> 는 런 시작 시 한 번만 fork 한다.</b>
    /// <c>RunEngine.PlayRun</c> 내부(봇 경로)도 같은 규칙을 지킨다 — 라운드마다, 또는 상점을
    /// 열 때마다 새로 fork/생성하면 두 스트림이 서로 다른 지점에서 갈라져 결정론이 깨진다.
    /// </para>
    /// </remarks>
    public sealed class RunManager : MonoBehaviour
    {
        public static RunManager? Instance { get; private set; }

        public GameData? Data { get; private set; }
        public MetaProgress? Meta { get; private set; }
        public RunState? CurrentRun { get; private set; }
        public Shop? CurrentShop { get; private set; }

        public bool IsDataLoaded { get; private set; }
        public bool IsRunActive => CurrentRun != null;

        /// <summary>직전 런의 결과. <see cref="EndRun"/> 이 <see cref="CurrentRun"/> 을 비우기 전에 남긴다 — 결과 화면이 읽는다.</summary>
        public bool LastRunCleared { get; private set; }
        public int LastRunRoundsWon { get; private set; }
        public int LastRunCurrencyEarned { get; private set; }

        /// <summary>직전 라운드에서 얻은 재화. <see cref="PlayRoundWithPlacement"/> 가 남긴다 — 전투 승리 화면이 읽는다.</summary>
        public int LastRoundCurrencyGained { get; private set; }

        /// <summary>스테이지 선택 화면에서 고른 스테이지. 용병 선택 화면이 <see cref="StartNewRun"/> 때 이걸 읽는다.</summary>
        public string SelectedStageId { get; set; } = "S1";

        /// <summary>데이터 로드가 끝나면(성공/실패 여부와 무관하게) 한 번 호출된다.</summary>
        public event Action? DataLoaded;

        private RunEngine? _engine;
        private DeterministicRandom? _encounterRng;
        private DeterministicRandom? _shopRng;
        private bool _shopRestockedThisRound;
        private int _roundsWon;

        /// <summary><see cref="PeekVariant"/> 가 뽑아둔 이번 라운드 변형. 라운드당 정확히 한 번만 뽑아야 한다(`D-75`).</summary>
        private VariantDef? _peekedVariant;
        private int _peekedForRound = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            StartCoroutine(StreamingGameData.LoadAsync(OnDataLoaded, OnDataError));
        }

        private void OnDataLoaded(GameData data)
        {
            Data = data;
            Meta = new MetaProgress(data.Meta);
            _engine = new RunEngine(data, CombatConfig.From(data.Economy, 20));
            IsDataLoaded = true;
            DataLoaded?.Invoke();
        }

        private void OnDataError(string message)
        {
            // 조용히 넘어가면 데이터 없이 뜬 UI가 원인 불명 상태로 멈춘다.
            Debug.LogError($"[RunManager] 데이터 로드 실패 — {message}");
            DataLoaded?.Invoke();
        }

        /// <summary>새 런을 시작한다. 로스터 3명은 용병 선택 화면에서 넘어온다.</summary>
        public void StartNewRun(string stageId, IReadOnlyList<string> characterIds)
        {
            if (_engine == null || Meta == null || Data == null)
            {
                Debug.LogError("[RunManager] 데이터가 아직 로드되지 않았다.");
                return;
            }

            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            var rootRng = new DeterministicRandom(seed);
            _encounterRng = rootRng.Fork(RngStream.Encounter);
            _shopRng = rootRng.Fork(RngStream.Shop);

            CurrentRun = _engine.StartRun(stageId, characterIds, Meta);
            CurrentShop = new Shop(Data);
            _shopRestockedThisRound = false;
            _roundsWon = 0;
            _peekedVariant = null;
            _peekedForRound = -1;
        }

        /// <summary>이번 라운드 상점 재고를 채운다 — 라운드당 정확히 한 번만.</summary>
        public void EnsureShopRestocked()
        {
            if (CurrentRun == null || CurrentShop == null || _shopRng == null) return;
            if (_shopRestockedThisRound) return;

            CurrentShop.Restock(CurrentRun, _shopRng, CurrentRun.Round);
            _shopRestockedThisRound = true;
        }

        public bool TryBuy(int offerIndex, string? targetCharacterId = null) =>
            CurrentRun != null && CurrentShop != null &&
            CurrentShop.TryBuy(CurrentRun, offerIndex, targetCharacterId);

        public bool TryReroll() =>
            CurrentRun != null && CurrentShop != null && _shopRng != null &&
            CurrentShop.TryReroll(CurrentRun, _shopRng, CurrentRun.Round);

        public bool TrySellItem(string characterId, string itemKey, int optionIndex = -1) =>
            CurrentRun != null && CurrentShop != null &&
            CurrentShop.TrySellItem(CurrentRun, characterId, itemKey, optionIndex);

        public bool TrySellTeamItem(string itemKey, int optionIndex = -1) =>
            CurrentRun != null && CurrentShop != null &&
            CurrentShop.TrySellTeamItem(CurrentRun, itemKey, optionIndex);

        /// <summary>
        /// 이번 라운드 적 구성을 전투 없이 미리 본다(`D-75`) — 배치 화면이 적을 공개하는 데 쓴다.
        /// </summary>
        /// <remarks>
        /// 같은 라운드에서 다시 부르면 캐시를 그대로 돌려준다. 두 번 뽑으면 관문 스트림이 한 칸
        /// 더 밀려 이후 라운드 구성이 통째로 달라지고, 그건 결과 숫자로는 드러나지 않는다.
        /// </remarks>
        public VariantDef? PeekVariant()
        {
            if (_engine == null || CurrentRun == null || _encounterRng == null) return null;

            if (_peekedForRound != CurrentRun.Round)
            {
                _peekedVariant = _engine.PeekVariant(CurrentRun, _encounterRng);
                _peekedForRound = CurrentRun.Round;
            }

            return _peekedVariant;
        }

        /// <summary>
        /// 플레이어가 고른 배치로 라운드를 치른다(`D-53`·`D-75`). 상점은 이 호출 전에 이미 끝나 있어야 한다(A-8).
        /// </summary>
        /// <param name="allyPlacement">캐릭터 id → 좌표. 살아 있는 아군 전원이 있어야 한다.</param>
        public RoundOutcome? PlayRoundWithPlacement(IReadOnlyDictionary<string, Coord> allyPlacement,
                                                    IEventSink sink, bool collectLog = true)
        {
            if (_engine == null || CurrentRun == null || Meta == null) return null;

            var variant = PeekVariant();
            if (variant == null) return null;

            var outcome = _engine.PlayRound(CurrentRun, Meta, variant, sink, allyPlacement, collectLog);
            if (outcome.Won) _roundsWon++;

            LastRoundCurrencyGained = outcome.CurrencyGained;
            _shopRestockedThisRound = false;
            _peekedVariant = null;
            _peekedForRound = -1;
            return outcome;
        }

        /// <summary>런을 마감한다 — 메타 재화 정산 + 스테이지 클리어 언락 후 상태를 비운다.</summary>
        public void EndRun()
        {
            if (CurrentRun == null || Meta == null || Data == null) return;

            bool cleared = _roundsWon == Data.Economy.TotalRounds;
            int earned = Meta.EarnedFrom(_roundsWon, cleared);
            Meta.Currency += earned;

            LastRunCleared = cleared;
            LastRunRoundsWon = _roundsWon;
            LastRunCurrencyEarned = earned;

            // ★ D-68 — 스테이지는 지금 S1/S2 둘뿐이라 다음 스테이지를 이렇게 정한다.
            //   스테이지가 늘어나면 economy.json 쪽에 순서 정보를 두고 거기서 읽어야 한다.
            if (cleared && CurrentRun.StageId == "S1") Meta.UnlockStage("S2");

            CurrentRun = null;
            CurrentShop = null;
        }
    }
}
