using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DomoNinja.Unity.Editor
{
    /// <summary>
    /// 저장소 루트의 <c>/data</c> 를 <c>StreamingAssets</c> 로 복사한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>정본은 언제나 저장소 루트의 <c>/data</c> 하나다.</b>
    /// 게임 데이터는 <c>sim</c>·<c>tune</c>·Unity 셋이 공유하는데, Unity 만 자기 사본을 들면
    /// <b>최적화기가 <c>/data</c> 를 고쳐도 게임은 낡은 값으로 돈다.</b>
    /// 밸런스 루프가 만든 수치가 실제 게임에 반영되지 않는 상태인데,
    /// 그건 화면에도 로그에도 안 나타난다 — 숫자가 그냥 조금 다를 뿐이다.
    /// </para>
    /// <para>
    /// 그래서 사본은 <b>빌드할 때마다 기계가 새로 만든다.</b> 손으로 복사하지 않고,
    /// 사본을 git 에도 올리지 않는다(`.gitignore`). 사람이 만질 수 있는 상태로 두면
    /// 언젠가 사본 쪽을 고치는 사람이 나오고, 그때 두 벌이 갈린다.
    /// </para>
    /// <para>
    /// <c>Resources</c> 가 아니라 <c>StreamingAssets</c> 를 쓰는 이유 —
    /// <c>Resources</c> 는 빌드에 통째로 실려 <b>스트리핑·직렬화 대상</b>이 되지만
    /// <c>StreamingAssets</c> 는 원본 파일 그대로 남는다.
    /// JSON 을 <see cref="TextAsset"/> 으로 바꿀 이유가 없고, 원본이 남아야
    /// 문제가 생겼을 때 배포된 파일을 직접 열어 확인할 수 있다.
    /// </para>
    /// </remarks>
    public sealed class DataSync : IPreprocessBuildWithReport
    {
        /// <summary>다른 빌드 훅보다 먼저 돈다 — 데이터가 없으면 뒤의 어떤 처리도 의미가 없다.</summary>
        public int callbackOrder => 0;

        private const string StreamingSubfolder = "data";

        public void OnPreprocessBuild(BuildReport report) => Sync();

        [MenuItem("DomoNinja/데이터 동기화 (/data → StreamingAssets)")]
        public static void SyncFromMenu()
        {
            int count = Sync();
            Debug.Log($"[DataSync] JSON {count}개를 StreamingAssets/{StreamingSubfolder} 로 복사했다.");
            AssetDatabase.Refresh();
        }

        /// <summary>복사한 파일 수.</summary>
        public static int Sync()
        {
            string source = FindDataDir();
            if (source == null)
            {
                // 여기서 조용히 넘어가면 데이터 없는 빌드가 나가고, 실행하고 나서야 안다.
                throw new BuildFailedException(
                    "[DataSync] 저장소의 /data 를 찾지 못했다. Unity 프로젝트가 저장소 안에 있어야 한다.");
            }

            string target = Path.Combine(Application.streamingAssetsPath, StreamingSubfolder);
            Directory.CreateDirectory(target);

            // 지운 뒤 복사한다. 남겨두면 /data 에서 삭제된 파일이 사본에만 남고,
            // 그게 언젠가 "없는 스테이지가 로드되는" 형태로 나타난다.
            foreach (string stale in Directory.GetFiles(target, "*.json"))
                File.Delete(stale);

            int count = 0;
            foreach (string file in Directory.GetFiles(source, "*.json"))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                count++;
            }

            return count;
        }

        /// <summary>
        /// <c>Application.dataPath</c> 에서 거슬러 올라가며 <c>/data</c> 를 찾는다.
        /// </summary>
        /// <remarks>
        /// 상대 경로를 박지 않는 이유 — Unity 프로젝트가 <c>unity/</c> 아래 있지만
        /// 그 깊이를 코드에 두면 폴더 구조를 바꿀 때 조용히 어긋난다.
        /// <c>economy.json</c> 이 있는지로 확인하므로 이름이 같은 다른 폴더에 속지 않는다.
        /// </remarks>
        private static string FindDataDir()
        {
            var dir = new DirectoryInfo(Application.dataPath);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "economy.json"))) return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
