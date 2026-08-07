using System.Collections.Generic;
using System.IO;
using System.Linq;
using DomoNinja.Unity.View;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DomoNinja.Unity.Editor
{
    /// <summary>
    /// <c>Assets/Sprite/Actor/**</c> 를 훑어 <see cref="SpriteCatalog"/> 를 만든다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 데이터의 <c>sprite</c> 값은 <b>폴더</b>를 가리킨다(`Actor/Character/Samurai`).
    /// 그 폴더 안에 <c>Faceset.png</c>·<c>Idle.png</c> 같은 파일이 여럿 있으므로
    /// <b>어느 것을 대표로 쓸지</b>를 여기서 정한다.
    /// </para>
    /// <para>
    /// ★ <c>Faceset</c> 을 먼저 찾는 이유 — 한 장짜리 초상이라 <b>자르지 않고 바로 쓸 수 있다.</b>
    /// <c>Idle</c> 은 방향별 프레임이 붙은 시트라 슬라이싱 설정이 필요하고,
    /// 그건 아트 파이프라인 결정이라 View 가 혼자 정할 일이 아니다.
    /// 지금은 "보드에 누가 있는지 보이게 하는 것"이 목적이므로 초상으로 충분하다.
    /// </para>
    /// </remarks>
    public sealed class SpriteCatalogBuilder : IPreprocessBuildWithReport
    {
        /// <summary><see cref="DataSync"/> 다음에 돈다. 순서가 서로 무관하지만 로그 읽기가 편하다.</summary>
        public int callbackOrder => 1;

        private const string SpriteRoot = "Assets/Sprite";
        private const string OutputPath = "Assets/Resources/" + SpriteCatalog.ResourceName + ".asset";

        /// <summary>대표 스프라이트를 고르는 우선순위. 앞에 있는 걸 먼저 쓴다.</summary>
        private static readonly string[] Preferred = { "Faceset", "Idle", "Idle40x40", "Walk" };

        /// <summary>
        /// 보드 전투 애니메이션(캐릭터 6 + 보스 2, Idle/Attack)과 타격 이펙트(FX/Hit — 범용 1종 +
        /// 캐릭터별 테마 6종)에 쓰는 시트. 값은 정사각 프레임 한 변(px).
        /// </summary>
        /// <remarks>
        /// 키는 <c>Assets/Sprite</c> 기준 확장자 없는 상대 경로 — 파일형 색인 키와 같은 형식이다.
        /// 여기 없는 PNG는 전부 기존처럼 <see cref="SpriteImportMode.Single"/> 로 남는다
        /// (몬스터 66종은 방향별 분리 프레임이 없어 이 표에 넣을 게 없다 — `D-77` 스코프 결정).
        /// </remarks>
        private static readonly Dictionary<string, int> AnimatedSpriteFrameSize = new Dictionary<string, int>
        {
            ["Actor/Character/Samurai/SeparateAnim/Idle"] = 16,
            ["Actor/Character/Samurai/SeparateAnim/Attack"] = 16,
            ["Actor/Character/Monk/SeparateAnim/Idle"] = 16,
            ["Actor/Character/Monk/SeparateAnim/Attack"] = 16,
            ["Actor/Character/NinjaRed/SeparateAnim/Idle"] = 16,
            ["Actor/Character/NinjaRed/SeparateAnim/Attack"] = 16,
            ["Actor/Character/Hunter/SeparateAnim/Idle"] = 16,
            ["Actor/Character/Hunter/SeparateAnim/Attack"] = 16,
            ["Actor/Character/NinjaMageBlack/SeparateAnim/Idle"] = 16,
            ["Actor/Character/NinjaMageBlack/SeparateAnim/Attack"] = 16,
            ["Actor/Character/Shaman/SeparateAnim/Idle"] = 16,
            ["Actor/Character/Shaman/SeparateAnim/Attack"] = 16,
            ["Actor/Boss/TenguRed/Idle"] = 82,
            ["Actor/Boss/TenguRed/Attack"] = 82,
            ["Actor/Boss/GiantFrog/Idle40x40"] = 40,
            ["Actor/Boss/GiantFrog/Attack"] = 40,
            ["FX/Hit/SpriteSheet"] = 32,
            ["FX/Hit/Samurai/SpriteSheet"] = 32,
            ["FX/Hit/Monk/SpriteSheet"] = 32,
            ["FX/Hit/NinjaRed/SpriteSheet"] = 32,
            ["FX/Hit/Hunter/SpriteSheet"] = 32,
            ["FX/Hit/NinjaMageBlack/SpriteSheet"] = 32,
            ["FX/Hit/Shaman/SpriteSheet"] = 32,

            // ★ 투사체인데 한 장이 아니다. 64×16 = 16px 프레임 4장이 이어 붙은 띠라
            //   Single 로 두면 **구슬 4개가 가로로 늘어선 채** 날아간다.
            //   파일 크기만 보면 "큰 스프라이트" 와 구분이 안 돼서 눈으로 열어보기 전엔 안 드러났다.
            ["FX/Projectile/EnergyBall"] = 16,
        };

        /// <summary>
        /// 몬스터 "몸통" 시트의 프레임 한 변(px)과 격자 크기. 전 종에서 64×64 / 4×4 로 균일하다
        /// (D+6 실측 — Bear·KappaGreen 을 갈라 육안 확인). 열이 방향(정면·측면·후면), 행이 같은
        /// 방향의 미세한 유휴 프레임이다. 보드에는 정면(0행 0열)만 쓴다.
        /// </summary>
        private const int MonsterBodyFrameSize = 16;

        private static string MonsterBodyFrameName(int row, int col) => $"body_r{row}_c{col}";

        public void OnPreprocessBuild(BuildReport report) => Build();

        [MenuItem("DomoNinja/스프라이트 카탈로그 생성")]
        public static void BuildFromMenu()
        {
            int count = Build();
            Debug.Log($"[SpriteCatalog] {count}개 항목 생성 → {OutputPath}");
        }

        /// <summary>
        /// PNG 를 <see cref="TextureImporterType.Sprite"/> 로 맞춘다.
        /// </summary>
        /// <remarks>
        /// ★ <b>URP 3D 템플릿에서 만든 프로젝트라 텍스처 기본값이 <c>Default</c> 다.</b>
        /// 그 상태에서는 <c>LoadAssetAtPath&lt;Sprite&gt;</c> 가 <c>null</c> 을 돌려주고,
        /// 스프라이트를 아무리 잘 넣어도 <b>화면에는 자리표시자만 뜬다.</b>
        /// 반입한 사람은 파일을 넣었으니 됐다고 보고, 쓰는 쪽은 왜 안 나오는지 모른다 —
        /// 임포트 설정은 <c>.meta</c> 에 있어서 코드 어디에도 안 보이기 때문이다.
        ///
        /// 픽셀 아트라 <c>Point</c> 필터에 압축을 끈다. 기본값(Bilinear + 압축)이면
        /// 16px 스프라이트가 뭉개져서 <b>"스프라이트가 흐리다"</b> 로 보인다.
        /// </remarks>
        private static int EnsureSpriteImport(string[] pngPaths)
        {
            int changed = 0;

            foreach (string path in pngPaths)
            {
                string asset = path.Replace('\\', '/');
                if (!(AssetImporter.GetAtPath(asset) is TextureImporter importer)) continue;

                string key = asset.Substring(SpriteRoot.Length + 1);
                key = key.Substring(0, key.Length - ".png".Length);

                if (AnimatedSpriteFrameSize.TryGetValue(key, out int frameSize))
                {
                    if (ApplyAnimatedImport(importer, frameSize)) changed++;
                    continue;
                }

                if (IsMonsterBodyFile(asset))
                {
                    if (ApplyMonsterBodyGridImport(importer)) changed++;
                    continue;
                }

                // 예전엔 여기 애니메이션 시트도 섞여 있었다 — Multiple 로 한 번 바뀐 파일이
                // 이 표에서 빠지면(스코프 축소) 이 조건이 없으면 Multiple 로 영영 남는다.
                bool needsChange = importer.textureType != TextureImporterType.Sprite
                                   || importer.spriteImportMode != SpriteImportMode.Single
                                   || importer.filterMode != FilterMode.Point
                                   || importer.textureCompression != TextureImporterCompression.Uncompressed;

                if (!needsChange) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;

                importer.SaveAndReimport();
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 정사각 프레임이 가로로 나열된 시트를 <see cref="SpriteImportMode.Multiple"/> 로 자른다.
        /// </summary>
        /// <remarks>
        /// 프레임 수는 시트 폭에서 역산한다(폭 ÷ <paramref name="frameSize"/>) — 프레임 수를 표에
        /// 따로 박아두면 아트가 프레임을 늘렸을 때 코드도 같이 고쳐야 하고, 둘이 어긋나면 마지막 프레임이
        /// 잘리거나 빈 칸이 섞인다. 이름은 <c>{파일명}_{인덱스}</c> — <see cref="AddAnimatedFrameEntries"/> 가
        /// 이 이름으로 되찾는다.
        /// </remarks>
        private static bool ApplyAnimatedImport(TextureImporter importer, int frameSize)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out _);
            int frameCount = Mathf.Max(1, width / frameSize);

            bool needsChange = importer.textureType != TextureImporterType.Sprite
                               || importer.spriteImportMode != SpriteImportMode.Multiple
                               || importer.filterMode != FilterMode.Point
                               || importer.textureCompression != TextureImporterCompression.Uncompressed
                               || importer.spritesheet == null
                               || importer.spritesheet.Length != frameCount;

            if (!needsChange) return false;

            string baseName = Path.GetFileNameWithoutExtension(importer.assetPath);
            var meta = new SpriteMetaData[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                meta[i] = new SpriteMetaData
                {
                    name = $"{baseName}_{i}",
                    rect = new Rect(i * frameSize, 0, frameSize, frameSize),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                };
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
#pragma warning disable CS0618 // spritesheet 는 레거시 API지만 개별 서브스프라이트를 코드로 지정하는 유일한 방법이다.
            importer.spritesheet = meta;
#pragma warning restore CS0618

            importer.SaveAndReimport();
            return true;
        }

        /// <summary>
        /// <paramref name="normalizedPath"/> 가 몬스터 폴더 바로 아래의 "몸통" 파일인지 —
        /// Faceset 이 아니고, <c>SeparateAnim</c> 처럼 더 깊은 하위 폴더도 아니어야 한다.
        /// </summary>
        /// <remarks>
        /// 파일명이 폴더마다 다르다(SpriteSheet·Eye·Slime·mushroom...) — 이름으로 찾을 수 없어
        /// "Faceset 이 아닌, 폴더 바로 아래 파일"이라는 배제 규칙으로 찾는다.
        /// </remarks>
        private static bool IsMonsterBodyFile(string normalizedPath)
        {
            const string prefix = "Assets/Sprite/Actor/Monster/";
            if (!normalizedPath.StartsWith(prefix)) return false;

            string rest = normalizedPath.Substring(prefix.Length); // "Bear/SpriteSheet.png"
            int slash = rest.IndexOf('/');
            if (slash < 0) return false;

            string afterFolder = rest.Substring(slash + 1); // "SpriteSheet.png" 또는 "SeparateAnim/Idle.png"
            if (afterFolder.Contains('/')) return false; // 더 깊은 하위 폴더 — Statue/SeparateAnim 류는 제외

            return Path.GetFileNameWithoutExtension(afterFolder) != "Faceset";
        }

        /// <summary>
        /// 몬스터 몸통 시트를 4×4 격자로 자른다. 절차적 공격 연출(확대+돌진, `D-77` 후속)이
        /// 이 중 정면(0행 0열) 한 장만 골라 쓴다 — 시트 전체를 그대로 색인하면 보드에
        /// 16장이 뭉쳐진 콜라주가 그대로 뜬다.
        /// </summary>
        private static bool ApplyMonsterBodyGridImport(TextureImporter importer)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            int cols = Mathf.Max(1, width / MonsterBodyFrameSize);
            int rows = Mathf.Max(1, height / MonsterBodyFrameSize);

            bool needsChange = importer.textureType != TextureImporterType.Sprite
                               || importer.spriteImportMode != SpriteImportMode.Multiple
                               || importer.filterMode != FilterMode.Point
                               || importer.textureCompression != TextureImporterCompression.Uncompressed
                               || importer.spritesheet == null
                               || importer.spritesheet.Length != cols * rows
                               || importer.spritesheet[0].name != MonsterBodyFrameName(0, 0);

            if (!needsChange) return false;

            var meta = new SpriteMetaData[cols * rows];
            int idx = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    meta[idx++] = new SpriteMetaData
                    {
                        name = MonsterBodyFrameName(r, c),
                        // Unity Rect 는 좌하단 원점 — 이미지 맨 위 행(r=0)을 얻으려면 y 를 뒤집는다.
                        rect = new Rect(c * MonsterBodyFrameSize, height - (r + 1) * MonsterBodyFrameSize,
                                        MonsterBodyFrameSize, MonsterBodyFrameSize),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                    };
                }
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
#pragma warning disable CS0618
            importer.spritesheet = meta;
#pragma warning restore CS0618

            importer.SaveAndReimport();
            return true;
        }

        public static int Build()
        {
            var allPngs = Directory.GetFiles(SpriteRoot, "*.png", SearchOption.AllDirectories);
            int fixedCount = EnsureSpriteImport(allPngs);
            if (fixedCount > 0)
                Debug.Log($"[SpriteCatalog] 텍스처 {fixedCount}개를 Sprite 로 다시 임포트했다.");

            var entries = new List<SpriteCatalog.Entry>();

            // ★ 데이터가 스프라이트를 가리키는 방식이 두 가지다. 둘 다 색인한다.
            //
            //   폴더형 — characters.sprite = "Actor/Character/Samurai"
            //            한 유닛의 프레임이 폴더에 여럿 들어 있어 대표를 하나 고른다.
            //   파일형 — skills.icon = "Skill/Samurai/Main/C1-A_일격"
            //            items.icon  = "Item/statBoost_스탯강화"
            //            파일 하나가 아이콘 하나다.
            //
            // 한쪽만 색인하면 다른 쪽이 통째로 자리표시자가 되는데, 그게
            // 아이콘 30개가 들어온 뒤에야 드러났다. 키가 겹칠 일은 없다 —
            // 폴더 경로와 파일 경로는 같아질 수 없다.

            foreach (string png in allPngs)
            {
                string normalized = png.Replace('\\', '/');

                // "Assets/Sprite/Item/statBoost_스탯강화.png" → "Item/statBoost_스탯강화"
                string key = normalized.Substring(SpriteRoot.Length + 1);
                key = key.Substring(0, key.Length - ".png".Length);

                if (AnimatedSpriteFrameSize.ContainsKey(key))
                {
                    // Multiple 모드라 텍스처 자체는 Sprite 가 아니다 — 프레임마다 따로 색인한다.
                    AddAnimatedFrameEntries(entries, normalized, key);
                    continue;
                }

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalized);
                if (sprite == null) continue;

                entries.Add(new SpriteCatalog.Entry { Key = key, Sprite = sprite });
            }

            foreach (string folder in Directory.GetDirectories(SpriteRoot, "*", SearchOption.AllDirectories))
            {
                string normalized = folder.Replace('\\', '/');
                var pngs = Directory.GetFiles(normalized, "*.png");
                if (pngs.Length == 0) continue;

                string best = Pick(pngs);
                if (best == null) continue;

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(best.Replace('\\', '/'));
                if (sprite == null) continue;

                // "Assets/Sprite/Actor/Character/Samurai" → "Actor/Character/Samurai"
                string key = normalized.Substring(SpriteRoot.Length + 1);
                entries.Add(new SpriteCatalog.Entry { Key = key, Sprite = sprite });

                // ★ 몬스터는 Idle/Attack 분리 프레임이 없다 — 대신 Faceset(초상) 말고
                //   "몸통 전체" 그림도 따로 색인해둔다. 절차적 공격 연출(확대+돌진, `D-77` 후속)이
                //   보드에서 이 그림을 쓴다. 캐릭터·보스는 이미 프레임 애니메이션이 있어 필요 없으니
                //   몬스터 폴더로 한정한다 — 파일명이 폴더마다 다르다(SpriteSheet·Eye·Slime...)
                //   그래서 "Faceset 이 아닌 파일"로 찾는다.
                if (normalized.StartsWith("Assets/Sprite/Actor/Monster/"))
                {
                    string body = pngs.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) != "Faceset");
                    if (body != null)
                    {
                        // Multiple 모드라 텍스처 자체는 Sprite 가 아니다 — 정면(0행 0열) 서브스프라이트만 찾는다.
                        var frontFrame = AssetDatabase.LoadAllAssetRepresentationsAtPath(body.Replace('\\', '/'))
                                                       .OfType<Sprite>()
                                                       .FirstOrDefault(s => s.name == MonsterBodyFrameName(0, 0));
                        if (frontFrame != null)
                            entries.Add(new SpriteCatalog.Entry { Key = key + "/Body", Sprite = frontFrame });
                    }
                }
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            Directory.CreateDirectory("Assets/Resources");

            var catalog = AssetDatabase.LoadAssetAtPath<SpriteCatalog>(OutputPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SpriteCatalog>();
                catalog.SetEntries(entries.ToArray());
                AssetDatabase.CreateAsset(catalog, OutputPath);
            }
            else
            {
                catalog.SetEntries(entries.ToArray());
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            return entries.Count;
        }

        /// <summary>
        /// <paramref name="path"/> 를 자른 프레임들을 <c>{key}_0</c>, <c>{key}_1</c>, ... 로 색인한다.
        /// </summary>
        /// <remarks>
        /// 서브스프라이트 순서가 아니라 <b>이름으로</b> 찾는다 — <c>LoadAllAssetRepresentationsAtPath</c> 의
        /// 반환 순서는 보장되지 않지만, 이름은 <see cref="ApplyAnimatedImport"/> 가 직접 지었으니 확실하다.
        /// </remarks>
        private static void AddAnimatedFrameEntries(List<SpriteCatalog.Entry> entries, string path, string key)
        {
            var frames = AssetDatabase.LoadAllAssetRepresentationsAtPath(path)
                                       .OfType<Sprite>()
                                       .ToDictionary(s => s.name);

            string baseName = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; frames.TryGetValue($"{baseName}_{i}", out var frame); i++)
                entries.Add(new SpriteCatalog.Entry { Key = $"{key}_{i}", Sprite = frame });
        }

        private static string Pick(string[] pngs)
        {
            foreach (string name in Preferred)
            {
                string hit = pngs.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
                if (hit != null) return hit;
            }

            // 우선순위에 없으면 이름순 첫 번째. 아무것도 안 쓰는 것보다는 낫고,
            // 어차피 자리표시자보다는 정확하다.
            return pngs.OrderBy(p => p, System.StringComparer.Ordinal).FirstOrDefault();
        }
    }
}
