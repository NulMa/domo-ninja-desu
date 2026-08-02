using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DomoNinja.Core.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace DomoNinja.Unity
{
    /// <summary>
    /// <c>StreamingAssets</c> 에서 게임 데이터를 읽는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>core 는 파일을 직접 열지 않는다.</b> WebGL 에 파일 시스템이 없기 때문이고,
    /// 그래서 <see cref="GameDataFiles.Load"/> 는 "이름을 주면 내용을 돌려주는 함수"를 받는다.
    /// 읽는 방법은 실행 환경이 정한다 — <c>sim</c> 은 디스크, 여기는 <see cref="UnityWebRequest"/>.
    /// </para>
    /// <para>
    /// ⚠️ <b>WebGL 은 <c>StreamingAssets</c> 를 비동기로만 읽을 수 있다.</b>
    /// 그래서 파일을 <b>먼저 전부 받아 캐시에 담고</b>, 그 다음에 core 를 부른다.
    /// "필요할 때 읽는" 구조로 짜면 에디터에서는 되고 브라우저에서만 멈춘다 —
    /// 이 프로젝트가 반복해서 겪은 "했다 ≠ 된다" 와 같은 함정이다.
    /// </para>
    /// <para>
    /// 어떤 파일을 받을지는 <see cref="GameDataFiles.EncounterFileNames"/> 에 묻는다.
    /// 여기서 파일 이름을 알고 있으면 <c>economy.stages</c> 매핑이 또 한 벌 복제된다.
    /// </para>
    /// </remarks>
    public static class StreamingGameData
    {
        private const string Subfolder = "data";

        /// <summary>
        /// 전부 읽어 <see cref="GameData"/> 를 만든다. 코루틴으로 돌린다.
        /// </summary>
        /// <param name="onLoaded">검증까지 통과했을 때. 실패하면 호출되지 않는다.</param>
        /// <param name="onError">파일을 못 읽었거나 검증에 걸렸을 때.</param>
        public static IEnumerator LoadAsync(Action<GameData> onLoaded, Action<string> onError)
        {
            var cache = new Dictionary<string, string>();

            // ① 필수 5개
            foreach (string name in GameDataFiles.Required)
            {
                string error = null;
                yield return Fetch(name, text => cache[name] = text, e => error = e);

                if (error != null)
                {
                    onError?.Invoke($"{name} 을 읽지 못했다: {error}");
                    yield break;
                }
            }

            // ② economy 가 가리키는 관문 파일. 없으면 건너뛴다 —
            //    저작이 안 들어온 스테이지 때문에 게임 전체가 안 뜨면 안 된다.
            foreach (var pair in GameDataFiles.EncounterFileNames(cache[GameDataFiles.Economy]))
            {
                string fileName = pair.Value;
                yield return Fetch(fileName, text => cache[fileName] = text, _ => { });
            }

            GameData data;
            try
            {
                data = GameDataFiles.Load(name => cache.TryGetValue(name, out string text) ? text : null);
            }
            catch (DataValidationException ex)
            {
                // 검증 실패는 조용히 넘어가면 안 된다. 잘못된 데이터로 도는 게임은
                // 정상으로 보이면서 밸런스만 틀린다.
                onError?.Invoke(ex.Message);
                yield break;
            }

            onLoaded?.Invoke(data);
        }

        /// <remarks>
        /// 에디터·스탠드얼론에서도 <see cref="UnityWebRequest"/> 를 쓴다.
        /// 플랫폼마다 다른 경로를 타면 <b>브라우저에서만 실패하는 상황</b>이 생기는데,
        /// 그건 WebGL 빌드를 돌리기 전까지 드러나지 않는다. 경로를 하나로 둔다.
        /// </remarks>
        private static IEnumerator Fetch(string fileName, Action<string> onText, Action<string> onError)
        {
            string path = Path.Combine(Application.streamingAssetsPath, Subfolder, fileName);
            string url = path.Contains("://") ? path : "file://" + path;

            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                onText?.Invoke(request.downloadHandler.text);
            }
        }
    }
}
