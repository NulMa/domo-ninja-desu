using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DomoNinja.Unity.Editor
{
    /// <summary>
    /// WebGL 빌드. <b>메뉴로도, 명령줄로도 같은 경로를 탄다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 게이트 1 은 Unity GUI 에서 손으로 빌드했다. 그때는 그게 맞았지만
    /// (`19` §5 — 게이트에 파이프라인 복잡도를 얹지 않는다), 이제는 아니다.
    /// D+8 제출 리허설과 D+9 최종 빌드가 남아 있는데 <b>손으로 하면 재현이 안 된다.</b>
    /// 설정 하나가 다른 빌드를 올려놓고 그걸 모르는 게 마감 직전에 가장 위험하다.
    /// </para>
    /// <para>
    /// ⚠️ <b><c>decompressionFallback</c> 은 반드시 켠다.</b> 이건 취향이 아니라 실측 결과다 —
    /// GitHub Pages 가 <c>content-encoding</c> 헤더를 붙여주지 않는데 파일은 gzip 이라,
    /// 폴백이 꺼져 있으면 <b>브라우저가 압축을 못 풀고 로딩에서 멈춘다.</b> (`13` §3, D+2 확인)
    /// </para>
    /// <para>
    /// 명령줄:
    /// <code>
    /// Unity -quit -batchmode -projectPath unity -executeMethod DomoNinja.Unity.Editor.WebGLBuilder.BuildFromCli
    /// </code>
    /// </para>
    /// </remarks>
    public static class WebGLBuilder
    {
        /// <summary>빌드 산출물. `gh-pages` 로 올리는 게 이 폴더 내용이다.</summary>
        public const string OutputPath = "Build/WebGL";

        [MenuItem("DomoNinja/WebGL 빌드")]
        public static void BuildFromMenu()
        {
            var report = Build();
            if (report == null) return;

            if (report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(Path.GetFullPath(OutputPath));
            }
        }

        /// <summary>명령줄 진입점. 실패하면 <b>0 이 아닌 코드로 종료한다</b> — CI 가 알아야 한다.</summary>
        public static void BuildFromCli()
        {
            var report = Build();
            bool ok = report != null && report.summary.result == BuildResult.Succeeded;

            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static BuildReport Build()
        {
            // 데이터와 스프라이트는 빌드 훅이 알아서 맞추지만, 메뉴로 부를 때도 같아야 한다.
            // 훅에만 맡기면 "에디터에서는 되는데 빌드만 다른" 상태가 생긴다.
            DataSync.Sync();
            SpriteCatalogBuilder.Build();
            AssetDatabase.Refresh();

            ApplyWebGLSettings();

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                // 빈 목록으로 빌드하면 검은 화면이 나오고, 그게 코드 문제로 보인다.
                Debug.LogError("[WebGLBuilder] Build Settings 에 활성화된 씬이 없다.");
                return null;
            }

            Directory.CreateDirectory(OutputPath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[WebGLBuilder] 성공 — {summary.totalSize / 1024 / 1024f:F2}MB · " +
                          $"{summary.totalTime.TotalSeconds:F0}초 · 씬 {scenes.Length}개\n{Path.GetFullPath(OutputPath)}");
            }
            else
            {
                Debug.LogError($"[WebGLBuilder] 실패 — {summary.result} · 에러 {summary.totalErrors}건");
            }

            return report;
        }

        /// <summary>
        /// 배포에 필요한 설정을 코드로 고정한다.
        /// </summary>
        /// <remarks>
        /// 프로젝트 설정 파일에도 저장되지만 여기서 다시 세우는 이유 —
        /// <b>설정이 언제 어떻게 바뀌었는지가 커밋에 안 남는다.</b>
        /// 코드로 두면 왜 그 값인지가 주석으로 같이 남고, 누가 인스펙터에서 되돌려도 빌드가 바로잡는다.
        /// </remarks>
        private static void ApplyWebGLSettings()
        {
            // ★ 실측으로 필요성이 확인된 값이다. 끄면 GitHub Pages 에서 로딩이 멈춘다.
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;

            // 심사자가 어떤 창 크기로 열지 모른다. 캔버스가 창을 따라가게 둔다.
            PlayerSettings.runInBackground = true;

            // ⚠️ 스트리핑은 Minimal 그대로 둔다(19 §6.1a).
            //    High 로 올리면 Newtonsoft 가 리플렉션을 쓰므로 런타임에 조용히 깨지고,
            //    link.xml 을 같이 넣어야 한다. 한 번에 한 가지만 바꾼다 —
            //    지금 목적은 "브라우저에서 도는가"이지 크기 줄이기가 아니다.
        }
    }
}
