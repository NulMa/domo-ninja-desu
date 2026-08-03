#nullable enable

using System;
using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Core.Tests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DomoNinja.Sim.Tests
{
    /// <summary>
    /// 최적화기가 제시한 값을 `/data` 위에 덮어쓰는 경로 (`P4` 의 관문).
    /// </summary>
    /// <remarks>
    /// ★ <b>여기서 조용히 실패하면 밸런스 루프 전체가 헛돈다.</b>
    /// 경로가 안 맞아도 넘어가면 최적화기는 <b>값을 바꿨다고 믿고</b>,
    /// 지표가 안 움직인 것을 *"그 파라미터는 영향이 없다"* 로 학습한다 —
    /// Morris 스크리닝 결과가 통째로 거짓말이 된다. <b>그리고 그건 지표만 봐서는 구분되지 않는다.</b>
    /// </remarks>
    [TestFixture]
    public class ParamOverridesTests
    {
        private static Dictionary<string, JToken> Paths(params (string path, object value)[] entries)
        {
            var map = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var (path, value) in entries) map[path] = JToken.FromObject(value);
            return map;
        }

        [Test]
        public void 값이_실제로_바뀐다()
        {
            string json = ParamOverrides.Apply(
                RepoData.Characters,
                Paths(("$.characters[?(@.id=='C2')].hp", 123)),
                "characters.json");

            var c2 = JObject.Parse(json).SelectToken("$.characters[?(@.id=='C2')].hp");
            Assert.That((int)c2!, Is.EqualTo(123));
        }

        [Test]
        public void 다른_값은_안_건드린다()
        {
            string json = ParamOverrides.Apply(
                RepoData.Characters,
                Paths(("$.characters[?(@.id=='C2')].hp", 123)),
                "characters.json");

            var before = JObject.Parse(RepoData.Characters).SelectToken("$.characters[?(@.id=='C3')].hp");
            var after = JObject.Parse(json).SelectToken("$.characters[?(@.id=='C3')].hp");

            Assert.That((int)after!, Is.EqualTo((int)before!));
        }

        [Test]
        public void 안_맞는_경로는_조용히_넘어가지_않는다()
        {
            // ★ 이 테스트가 이 파일에서 가장 중요하다.
            var ex = Assert.Throws<ArgumentException>(() => ParamOverrides.Apply(
                RepoData.Characters,
                Paths(("$.characters[?(@.id=='C9')].hp", 123)),
                "characters.json"));

            Assert.That(ex!.Message, Does.Contain("C9"));
        }

        [Test]
        public void 구조를_가리키는_경로는_거부한다()
        {
            // 객체·배열 통째로 갈아끼우는 건 저작 판단이지 최적화가 아니다.
            Assert.Throws<ArgumentException>(() => ParamOverrides.Apply(
                RepoData.Characters,
                Paths(("$.characters[?(@.id=='C2')]", new JObject())),
                "characters.json"));
        }

        [Test]
        public void 덮어쓴_데이터가_검증_규칙을_그대로_통과한다()
        {
            // 최적화기가 낸 값도 R01~R24 를 지켜야 한다. 안 지키면 여기서 멈추는 게 맞다 —
            // 검증을 건너뛰면 "규칙을 어긴 밸런스" 가 리포트에 정상값처럼 실린다.
            var overrides = new ParamOverrides.Set
            {
                ["characters.json"] = Paths(("$.characters[?(@.id=='C2')].hp", 150)),
            };

            var data = GameDataFiles.Load(ParamOverrides.Wrap(RepoData.TryRead, overrides));

            Assert.That(data.FindCharacter("C2")!.Hp, Is.EqualTo(150));
        }

        [Test]
        public void 규칙을_어기는_값은_로더가_잡는다()
        {
            // R19 — moveInterval 은 1 이상이어야 한다. 0 이면 매 틱 이동이 된다.
            var overrides = new ParamOverrides.Set
            {
                ["characters.json"] = Paths(("$.characters[?(@.id=='C2')].moveInterval", 0)),
            };

            Assert.Throws<DataValidationException>(
                () => GameDataFiles.Load(ParamOverrides.Wrap(RepoData.TryRead, overrides)));
        }

        [Test]
        public void 덮어쓰기가_없으면_원본_읽기를_그대로_쓴다()
        {
            var read = ParamOverrides.Wrap(RepoData.TryRead, null);
            Assert.That(read(GameDataFiles.Characters), Is.EqualTo(RepoData.Characters));
        }

        // ────────────────────────────── 지문 (D-55)

        [Test]
        public void 같은_세트는_순서가_달라도_같은_지문이다()
        {
            // [BAL] 커밋의 `근거:` 가 서로 대조되려면 순서에 안 흔들려야 한다.
            var a = new ParamOverrides.Set
            {
                ["characters.json"] = Paths(("$.a", 1), ("$.b", 2)),
                ["economy.json"] = Paths(("$.c", 3)),
            };
            var b = new ParamOverrides.Set
            {
                ["economy.json"] = Paths(("$.c", 3)),
                ["characters.json"] = Paths(("$.b", 2), ("$.a", 1)),
            };

            Assert.That(ParamOverrides.Hash(b), Is.EqualTo(ParamOverrides.Hash(a)));
        }

        [Test]
        public void 값이_하나라도_다르면_지문이_달라진다()
        {
            var a = new ParamOverrides.Set { ["characters.json"] = Paths(("$.a", 1)) };
            var b = new ParamOverrides.Set { ["characters.json"] = Paths(("$.a", 2)) };

            Assert.That(ParamOverrides.Hash(b), Is.Not.EqualTo(ParamOverrides.Hash(a)));
        }

        [Test]
        public void 덮어쓰기가_없으면_지문이_none_이다()
        {
            Assert.That(ParamOverrides.Hash(null), Is.EqualTo("none"));
            Assert.That(ParamOverrides.Hash(new ParamOverrides.Set()), Is.EqualTo("none"));
        }

        [Test]
        public void params_json_을_왕복해도_같은_지문이다()
        {
            // JSONPath 의 문자열 리터럴이 작은따옴표라 여기서는 치환 트릭을 못 쓴다.
            string json = "{ \"seeds\": 3, \"overrides\": { \"characters.json\": "
                        + "{ \"$.characters[?(@.id=='C2')].hp\": 150 } } }";

            var p = SimParams.FromJson(json);
            Assert.That(p.Overrides, Is.Not.Null);

            var round = SimParams.FromJson(new JObject { ["overrides"] = ParamOverrides.ToJson(p.Overrides) }.ToString());

            Assert.That(ParamOverrides.Hash(round.Overrides), Is.EqualTo(ParamOverrides.Hash(p.Overrides)));
        }
    }
}
