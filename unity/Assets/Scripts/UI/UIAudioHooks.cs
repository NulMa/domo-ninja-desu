using UnityEngine;
using UnityEngine.UI;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 모든 버튼에 <b>클릭음</b>을 붙인다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 버튼이 만들어지는 경로가 둘이다 — <b>씬에 미리 놓인 것</b>과
    /// <b>컨트롤러가 런타임에 붙이는 것</b>(<see cref="UITheme.EnsureButton"/>). 한쪽만 걸면
    /// <b>화면에 따라 소리가 나기도 하고 안 나기도 한다.</b> 그건 "버그"보다 "이 화면은 원래 조용한가?"로
    /// 읽혀서 더 늦게 발견된다. 그래서 두 경로를 모두 막는다.
    /// </para>
    /// <para>
    /// 컴포넌트를 붙이지 않고 <c>onClick</c> 에 거는 이유 — 씬에 놓인 버튼 34개에 컴포넌트를 심으면
    /// <b>씬 파일이 그만큼 커지고</b> 2인이 같은 씬을 건드릴 이유가 하나 늘어난다(`19` §5.1).
    /// 소리는 씬에 저장할 정보가 아니다.
    /// </para>
    /// <para>
    /// 같은 버튼에 두 번 걸릴 수 있으므로(컨트롤러가 이미 있는 버튼에 <c>EnsureButton</c> 을 부른다)
    /// <b>지우고 다시 건다.</b> 정적 메서드라 <c>RemoveListener</c> 가 같은 대상으로 인식한다.
    /// </para>
    /// </remarks>
    public static class UIAudioHooks
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookSceneButtons()
        {
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
                Attach(button);
        }

        /// <summary>버튼 하나에 클릭음을 건다. 여러 번 불러도 한 번만 걸린다.</summary>
        public static void Attach(Button button)
        {
            if (button == null) return;
            button.onClick.RemoveListener(OnAnyButtonClicked);
            button.onClick.AddListener(OnAnyButtonClicked);
        }

        /// <remarks>
        /// 첫 클릭이 <b>브라우저의 소리 잠금을 푸는 순간</b>이기도 하다.
        /// 잠금 해제와 클릭음을 같은 자리에서 처리하면, "왜 첫 소리만 안 나지"가 생기지 않는다.
        /// </remarks>
        private static void OnAnyButtonClicked()
        {
            var audio = AudioManager.Instance;
            if (audio == null) return;

            audio.UnlockAudio();
            audio.PlaySfx(AudioKeys.Click);
        }
    }
}
