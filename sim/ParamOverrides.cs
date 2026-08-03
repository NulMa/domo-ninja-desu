#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DomoNinja.Sim
{
    /// <summary>
    /// 최적화기가 제시한 파라미터 값을 <b>`/data` 원본 JSON 위에 덮어쓴다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>파일을 고치지 않는다.</b> 읽어온 문자열에만 적용한다 —
    /// `/tune` 은 수천 번 값을 시도하는데 그때마다 `/data` 를 쓰면
    /// <b>중간에 멈췄을 때 저장소가 어느 시도의 값으로 남아 있는지 알 수 없다.</b>
    /// 채택된 값만 사람이 `[BAL]` 커밋으로 넣는다 (`09` §7).
    /// </para>
    /// <para>
    /// ★ <b>경로 문법을 새로 만들지 않았다.</b> Newtonsoft 의 <c>SelectTokens</c>(JSONPath)를 쓴다 —
    /// <c>$.characters[?(@.id=='C1')].attack</c> 처럼 쓴다. 자체 파서를 두면
    /// <b>파서의 버그가 밸런스 결과의 버그로 나타나고</b>, 그건 지표만 봐서는 구분되지 않는다.
    /// </para>
    /// <para>
    /// ★ <b>맞는 곳이 없으면 던진다.</b> 이게 이 타입에서 가장 중요한 줄이다 —
    /// 오타 난 경로를 조용히 넘기면 최적화기는 <b>값을 바꿨다고 믿고</b> 지표가 안 움직인 걸
    /// *"그 파라미터는 영향이 없다"* 로 학습한다. Morris 스크리닝이 통째로 거짓말이 된다.
    /// </para>
    /// </remarks>
    public static class ParamOverrides
    {
        /// <summary>파일 이름 → (JSONPath → 새 값).</summary>
        public sealed class Set : Dictionary<string, Dictionary<string, JToken>>
        {
            public Set() : base(StringComparer.Ordinal) { }
        }

        /// <summary>
        /// <paramref name="read"/> 를 감싸 덮어쓰기가 적용된 읽기 함수를 만든다.
        /// </summary>
        /// <remarks>
        /// <see cref="Core.Data.GameDataFiles.Load"/> 가 읽기 <b>함수</b>를 받게 돼 있어서
        /// core 를 한 줄도 안 고치고 끼어들 수 있다. 그 설계가 여기서 값을 한다.
        /// </remarks>
        public static Func<string, string?> Wrap(Func<string, string?> read, Set? overrides)
        {
            if (overrides == null || overrides.Count == 0) return read;

            return name =>
            {
                string? raw = read(name);
                if (raw == null || !overrides.TryGetValue(name, out var paths)) return raw;

                return Apply(raw, paths, name);
            };
        }

        /// <summary>JSON 문자열 하나에 경로별 값을 적용한다.</summary>
        /// <exception cref="ArgumentException">경로가 아무 데도 안 맞거나 값 형태가 안 맞으면.</exception>
        public static string Apply(string json, IReadOnlyDictionary<string, JToken> paths, string where)
        {
            var root = JToken.Parse(json);

            // 경로 순서를 고정한다. 두 경로가 같은 노드를 가리키면 나중 것이 이기는데,
            // 그 "나중" 이 딕셔너리 순회 순서에 달려 있으면 같은 params.json 이 다른 결과를 낸다.
            foreach (var key in paths.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var targets = root.SelectTokens(key, errorWhenNoMatch: false).ToList();

                if (targets.Count == 0)
                    throw new ArgumentException(
                        $"{where}: 경로 `{key}` 가 아무 데도 안 맞는다. " +
                        "조용히 넘기면 최적화기는 값을 바꿨다고 믿고, 지표가 안 움직인 것을 " +
                        "'그 파라미터는 영향이 없다' 로 학습한다.");

                var value = paths[key];

                foreach (var target in targets)
                {
                    if (target.Type == JTokenType.Object || target.Type == JTokenType.Array)
                        throw new ArgumentException(
                            $"{where}: 경로 `{key}` 가 {target.Type} 를 가리킨다. " +
                            "덮어쓰기는 스칼라만 받는다 — 구조를 바꾸는 건 저작 판단이지 최적화가 아니다.");

                    target.Replace(value.DeepClone());
                }
            }

            return root.ToString();
        }

        /// <summary>
        /// 파라미터 세트의 지문. <b>리포트에 박아 어느 값으로 낸 숫자인지 남긴다</b> (`D-55`).
        /// </summary>
        /// <remarks>
        /// 경로를 정렬해서 넣는다 — 같은 세트가 순서만 달라도 다른 해시가 나오면
        /// <c>[BAL]</c> 커밋의 <c>근거:</c> 가 서로 대조되지 않는다.
        /// </remarks>
        public static string Hash(Set? overrides)
        {
            if (overrides == null || overrides.Count == 0) return "none";

            var sb = new StringBuilder();
            foreach (var file in overrides.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                foreach (var path in overrides[file].Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    sb.Append(file).Append('|').Append(path).Append('=')
                      // 불변 문화권으로 고정한다. 지역 설정에 따라 0.5 가 "0,5" 로 나오면
                      // 같은 세트가 PC 마다 다른 해시를 갖는다.
                      .Append(overrides[file][path].ToString(Newtonsoft.Json.Formatting.None))
                      .Append(';');
                }
            }

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(16);
                for (int i = 0; i < 8; i++) hex.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>`params.json` 의 <c>overrides</c> 절을 읽는다.</summary>
        public static Set? FromJson(JObject? node)
        {
            if (node == null) return null;

            var set = new Set();
            foreach (var file in node)
            {
                if (!(file.Value is JObject paths)) continue;

                var map = new Dictionary<string, JToken>(StringComparer.Ordinal);
                foreach (var entry in paths)
                    if (entry.Value != null) map[entry.Key] = entry.Value;

                if (map.Count > 0) set[file.Key] = map;
            }

            return set.Count == 0 ? null : set;
        }

        /// <summary>리포트에 그대로 실을 형태로 되돌린다.</summary>
        public static JObject ToJson(Set? overrides)
        {
            var result = new JObject();
            if (overrides == null) return result;

            foreach (var file in overrides.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var paths = new JObject();
                foreach (var path in overrides[file].Keys.OrderBy(k => k, StringComparer.Ordinal))
                    paths[path] = overrides[file][path].DeepClone();
                result[file] = paths;
            }

            return result;
        }
    }
}
