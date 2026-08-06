namespace DomoNinja.Unity
{
    /// <summary>
    /// 소리 이름을 한 곳에 모은다. 값은 <c>Assets/Audio</c> 아래 경로(확장자 제외)와 같다.
    /// </summary>
    /// <remarks>
    /// ★ 문자열을 부르는 쪽마다 적으면 <b>오타가 컴파일에서 안 걸리고 "소리가 안 난다"로만 드러난다.</b>
    /// 그건 화면에 아무 표시가 없어서 찾는 데 오래 걸리는 종류다.
    /// <para>
    /// 파일명을 <c>Village</c>·<c>Slash4</c> 같은 원본 이름 대신 <b>쓰임새</b>로 바꿔둔 것도 같은 이유다 —
    /// 곡을 교체할 때 코드를 고칠 필요가 없다. 원본이 무엇이었는지는 `14_에셋_라이선스.md` 에 남긴다.
    /// </para>
    /// </remarks>
    public static class AudioKeys
    {
        // 배경음
        public const string BgmMenu = "Bgm/menu";
        public const string BgmBattle = "Bgm/battle";

        // UI
        public const string Click = "Sfx/click";
        public const string Cancel = "Sfx/cancel";

        // 전투
        public const string Attack = "Sfx/attack";
        public const string Hit = "Sfx/hit";
        public const string Death = "Sfx/death";

        // 결과 · 보상
        public const string Victory = "Sfx/victory";
        public const string Defeat = "Sfx/defeat";
        public const string Reward = "Sfx/reward";
    }
}
