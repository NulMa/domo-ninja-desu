// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System.Collections.Generic;
using DomoNinja.Core.Combat;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using DomoNinja.Core.Skills;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Core.Economy
{
    /// <summary>
    /// 구매한 아이템이 스탯에 거는 증감분.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>스킬·메타와 같은 합연산 통에 들어간다</b> (`_schema` §8 · `economy.items._stacking`).
    /// 아이템만 따로 곱하면 아이템을 겹칠수록 폭발해 `M4` 지배 빌드가 생긴다 —
    /// 곱연산을 버린 이유가 그것이고, 여기서 예외를 두면 그 결정이 반쪽이 된다.
    /// </para>
    /// <para>
    /// ★ <b>조건이 붙은 옵션은 여기서 접지 않는다.</b>
    /// <c>conditionalBoost</c> 의 세 조건(<c>hp_below</c>·<c>enemies_above</c>·<c>is_last_alive</c>)은
    /// 전투 중에 뒤집히므로 시작 스탯에 넣으면 <b>조건부인데 항상 켜진 아이템</b>이 된다.
    /// 그쪽은 <see cref="CollectConditional"/> 이 뽑아 전투 루프가 매 틱 평가한다.
    /// </para>
    /// </remarks>
    public static class ItemEffects
    {
        /// <summary>
        /// 아직 효과가 반영되지 않는 아이템. <b>조용히 무시하지 않기 위해 이름을 남긴다.</b>
        /// </summary>
        /// <remarks>
        /// 지금은 비어 있다 — <c>conditionalBoost</c> 가 D+4 에 구현되면서 마지막 항목이 빠졌다.
        /// <b>목록을 지우지는 않는다.</b> 최적화기가 <c>economy.json</c> 에 새 아이템을 넣을 수 있고,
        /// 그때 "이름은 있는데 아무 일도 안 한다" 를 드러낼 자리가 여기다.
        /// </remarks>
        public static readonly IReadOnlyList<string> Pending = new string[0];

        /// <summary>
        /// 한 캐릭터에게 걸린 아이템 전부(개인 + 팀)를 <paramref name="stat"/> 기준 증감분으로 합친다.
        /// </summary>
        public static int DeltaPermilleFor(GameData data, RunState run, RosterEntry entry, StatKey stat)
        {
            var sum = new ModifierSum();

            Accumulate(data, entry.Items, stat, ref sum);
            Accumulate(data, run.TeamItems, stat, ref sum);

            return sum.DeltaPermille;
        }

        private static void Accumulate(GameData data, List<OwnedItem> items, StatKey stat, ref ModifierSum sum)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (IsPending(items[i].Key)) continue;

                var option = OptionOf(data, items[i]);
                if (option == null) continue;

                // ★ 조건이 붙은 옵션은 여기가 아니라 CollectConditional 이 가져간다.
                //   conditionalBoost 의 `value` 는 조건 임계값("체력 50% 이하일 때")이고
                //   실제 강화 수치는 `mult` 에 있다. 거르지 않으면 임계값을 강화값으로 읽어
                //   조건부 아이템이 상시 +50% 로 들어간다 — 안 켜지는 것보다 나쁘다.
                //   이름이 아니라 `condition` 의 유무로 가른다 — 최적화기가 다른 아이템에
                //   조건을 붙여도 같은 규칙이 걸린다.
                if (option["condition"] != null) continue;

                if (StatKeys.Parse((string?)option["stat"]) != stat) continue;

                // isMultiplier 인 값은 배율(0.15 = +15%)이고, 그렇지 않으면 그대로 증감분이다.
                // economy.items 의 현재 값은 전부 배율이지만, 최적화기가 형식을 바꿀 수 있어 둘 다 읽는다.
                double value = (double?)option["value"] ?? 0d;
                bool isMultiplier = (bool?)option["isMultiplier"] ?? true;

                // attackInterval 은 0.9 처럼 "곱해서 줄이는" 형태로 적혀 있다.
                // 1 을 빼면 -10% 가 되어 다른 스탯과 같은 증감분 규약에 들어온다.
                int delta = isMultiplier && value > 0.5 && stat == StatKey.AttackInterval
                    ? Permille.FromMultiplier(value) - Permille.One
                    : Permille.FromMultiplier(value);

                sum.AddDeltaPermille(delta);
            }
        }

        /// <summary>
        /// 한 캐릭터에게 걸린 <b>조건부</b> 강화를 전투에 넘길 형태로 뽑는다.
        /// </summary>
        /// <remarks>
        /// 팀 아이템까지 같이 본다 — 지금 데이터에서 <c>conditionalBoost</c> 는 캐릭터 지정형이지만,
        /// 여기서 개인분만 보면 최적화기가 팀 아이템에 조건을 붙이는 순간
        /// <b>구매는 되는데 아무 일도 안 하는</b> 아이템이 생긴다.
        /// </remarks>
        public static List<ConditionalBoost> CollectConditional(GameData data, RunState run, RosterEntry entry)
        {
            var list = new List<ConditionalBoost>();

            CollectConditionalFrom(data, entry.Items, list);
            CollectConditionalFrom(data, run.TeamItems, list);

            return list;
        }

        private static void CollectConditionalFrom(GameData data, List<OwnedItem> items,
                                                   List<ConditionalBoost> into)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (IsPending(items[i].Key)) continue;

                var option = OptionOf(data, items[i]);
                if (option == null) continue;

                var condition = ConditionalBoost.ParseCondition((string?)option["condition"]);
                if (condition == BoostCondition.None) continue;

                var stat = StatKeys.Parse((string?)option["stat"]);
                if (stat == StatKey.None) continue;

                // `value` 는 임계값이고 `mult` 가 강화 수치다. 둘을 바꿔 읽으면
                // "체력 50% 이하일 때 +40%" 가 "체력 40% 이하일 때 +50%" 가 된다 —
                // 컴파일도 되고 전투도 돌아서 지표가 조금 이상해질 뿐이다.
                double raw = (double?)option["value"] ?? 0d;
                int threshold = condition == BoostCondition.HpBelow
                    ? Permille.FromMultiplier(raw)      // 0.5 → 500‰
                    : (int)raw;                         // 개수는 그대로

                into.Add(new ConditionalBoost(
                    condition, threshold, stat,
                    Permille.FromMultiplier((double?)option["mult"] ?? 0d)));
            }
        }

        private static JObject? OptionOf(GameData data, OwnedItem item)
        {
            var def = (data.Economy.Raw["items"] as JObject)?[item.Key] as JObject;
            if (def == null) return null;

            if (!(def["options"] is JArray options)) return null;
            if (item.OptionIndex < 0 || item.OptionIndex >= options.Count) return null;

            return options[item.OptionIndex] as JObject;
        }

        /// <summary>그 아이템이 아직 구현되지 않았는가.</summary>
        public static bool IsPending(string itemKey)
        {
            for (int i = 0; i < Pending.Count; i++)
                if (Pending[i] == itemKey) return true;
            return false;
        }
    }
}
