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
    /// ⚠️ <c>conditionalBoost</c> 는 <b>아직 반영되지 않는다.</b>
    /// 조건(<c>hp_below</c>·<c>enemies_above</c>·<c>is_last_alive</c>)이 전투 중에 바뀌므로
    /// 시작 시점 스탯으로 접을 수 없고, 매 틱 평가할 자리가 필요하다.
    /// 지금 대충 상시 적용으로 넣으면 <b>조건부인데 항상 켜진 아이템</b>이 되어
    /// 밸런스 지표가 실제보다 세게 나온다. 구현될 때까지 <see cref="Pending"/> 로 드러내 둔다.
    /// </para>
    /// </remarks>
    public static class ItemEffects
    {
        /// <summary>아직 효과가 반영되지 않는 아이템. <b>조용히 무시하지 않기 위해 이름을 남긴다.</b></summary>
        public static readonly IReadOnlyList<string> Pending = new[] { "conditionalBoost" };

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
                // ★ 미구현 아이템은 여기서 반드시 걸러낸다.
                //   conditionalBoost 의 `value` 는 조건 임계값("체력 50% 이하일 때")이고
                //   실제 강화 수치는 `mult` 에 있다. 거르지 않으면 임계값을 강화값으로 읽어
                //   조건부 아이템이 상시 +50% 로 들어간다 — 안 켜지는 것보다 나쁘다.
                if (IsPending(items[i].Key)) continue;

                var option = OptionOf(data, items[i]);
                if (option == null) continue;

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
