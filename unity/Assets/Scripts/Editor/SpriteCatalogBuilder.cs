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

                bool needsChange = importer.textureType != TextureImporterType.Sprite
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

        public static int Build()
        {
            var allPngs = Directory.GetFiles(SpriteRoot, "*.png", SearchOption.AllDirectories);
            int fixedCount = EnsureSpriteImport(allPngs);
            if (fixedCount > 0)
                Debug.Log($"[SpriteCatalog] 텍스처 {fixedCount}개를 Sprite 로 다시 임포트했다.");

            var entries = new List<SpriteCatalog.Entry>();

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
