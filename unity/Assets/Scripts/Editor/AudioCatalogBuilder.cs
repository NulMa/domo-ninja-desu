using System.Collections.Generic;
using System.IO;
using DomoNinja.Unity;
using UnityEditor;
using UnityEngine;

namespace DomoNinja.Unity.Editor
{
    /// <summary>
    /// <c>Assets/Audio/**</c> 를 훑어 <see cref="AudioCatalog"/> 를 만들고,
    /// <b>웹 빌드에 맞는 임포트 설정</b>을 강제한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>임포트 설정을 손으로 두면 안 되는 이유가 웹 빌드에 있다.</b>
    /// 팩의 오디오 전체는 95MB 다. 지금 웹 빌드가 19MB 인데 기본 설정으로 몇 개만 넣어도 금방 커진다.
    /// 크기는 <b>다운로드 시간</b>이고, 심사자는 로딩이 길면 그냥 닫는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>WebGL 은 스트리밍 재생을 지원하지 않는다.</b> <c>Streaming</c> 으로 두면
    /// Unity 가 조용히 <c>CompressedInMemory</c> 로 바꿔버려서, 설정만 보고는 무엇이 적용됐는지 알 수 없다.
    /// 그래서 여기서 <b>명시적으로</b> 지정한다 —
    /// BGM 은 압축된 채 메모리에 두고(길다), SFX 는 미리 풀어둔다(짧고 즉시 나야 한다).
    /// </para>
    /// <para>
    /// 효과음은 <b>모노로 강제</b>한다. 위치감이 필요 없는 UI·타격음에 스테레오는 용량만 두 배다.
    /// </para>
    /// </remarks>
    public static class AudioCatalogBuilder
    {
        private const string AudioRoot = "Assets/Audio";
        private const string OutputPath = "Assets/Resources/AudioCatalog.asset";

        [MenuItem("DomoNinja/오디오 카탈로그 생성", false, 40)]
        public static void BuildFromMenu()
        {
            if (!Directory.Exists(AudioRoot))
            {
                Debug.LogError($"[AudioCatalogBuilder] {AudioRoot} 가 없다.");
                return;
            }

            var entries = new List<AudioCatalog.Entry>();

            foreach (string path in Directory.GetFiles(AudioRoot, "*.*", SearchOption.AllDirectories))
            {
                string normalized = path.Replace('\\', '/');
                if (normalized.EndsWith(".meta")) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(normalized);
                if (clip == null) continue;

                ApplyImportSettings(normalized);

                // "Assets/Audio/Sfx/hit.wav" → "Sfx/hit"
                string key = normalized.Substring(AudioRoot.Length + 1);
                key = key.Substring(0, key.LastIndexOf('.'));
                entries.Add(new AudioCatalog.Entry { Key = key, Clip = clip });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            Directory.CreateDirectory("Assets/Resources");
            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(OutputPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                catalog.SetEntries(entries.ToArray());
                AssetDatabase.CreateAsset(catalog, OutputPath);
            }
            else
            {
                catalog.SetEntries(entries.ToArray());
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AudioCatalogBuilder] {entries.Count}개 색인 — {string.Join(", ", entries.ConvertAll(e => e.Key))}");
        }

        /// <summary>BGM 은 길고 SFX 는 짧다. 같은 설정을 쓰면 한쪽이 반드시 손해를 본다.</summary>
        private static void ApplyImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;

            bool isBgm = path.Contains("/Bgm/");

            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.loadType = isBgm ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.quality = isBgm ? 0.4f : 0.6f;
            settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;

            // `preloadAudioData` 는 플랫폼별 설정으로 옮겨졌다 — 임포터의 옛 프로퍼티는 쓰면 안 된다.
            // BGM 은 미리 받아두지 않는다(첫 화면 로딩이 그만큼 늦어진다), SFX 는 즉시 나야 하므로 받아둔다.
            settings.preloadAudioData = !isBgm;

            importer.defaultSampleSettings = settings;
            importer.forceToMono = !isBgm;
            importer.loadInBackground = isBgm;

            importer.SaveAndReimport();
        }
    }
}
