using System;
using System.Collections.Generic;
using UnityEngine;

namespace DomoNinja.Unity
{
    /// <summary>
    /// 소리 이름 → 실제 <see cref="AudioClip"/> 을 잇는 표.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>런타임은 파일 경로로 클립을 찾을 수 없다.</b> <c>Assets/Audio/...</c> 는 빌드에 그대로
    /// 들어가지 않고 <b>참조된 것만</b> 실려 나간다. 그래서 에디터가 미리 표를 만들고 런타임은 그 표만 본다 —
    /// <see cref="View.SpriteCatalog"/> 와 같은 구조다. 같은 문제에 다른 해법을 쓰면
    /// 나중에 "왜 그림은 되는데 소리는 안 되지"를 두 번 조사하게 된다.
    /// </para>
    /// <para>
    /// 표에 없는 이름은 <c>null</c> 을 돌려주고 <b>재생만 조용히 건너뛴다.</b>
    /// 소리는 없어도 게임이 진행되므로 여기서 예외를 던지면 잃는 것이 더 크다 —
    /// 대신 어떤 이름이 없었는지는 한 번 로그로 남긴다(<see cref="AudioManager"/>).
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "DomoNinja/Audio Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        /// <summary>`Resources.Load` 로 찾을 이름. 에디터 빌더가 같은 이름으로 만든다.</summary>
        public const string ResourceName = "AudioCatalog";

        [Serializable]
        public struct Entry
        {
            /// <summary>`Assets/Audio` 아래 확장자를 뗀 경로 (`Bgm/battle`, `Sfx/hit`).</summary>
            public string Key;
            public AudioClip Clip;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        private Dictionary<string, AudioClip> _lookup;

        public int Count => _entries.Length;

        public void SetEntries(Entry[] entries)
        {
            _entries = entries;
            _lookup = null;
        }

        /// <summary>없으면 <c>null</c>.</summary>
        public AudioClip Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (_lookup == null)
            {
                _lookup = new Dictionary<string, AudioClip>(_entries.Length);
                foreach (var e in _entries)
                    if (!string.IsNullOrEmpty(e.Key)) _lookup[e.Key] = e.Clip;
            }

            return _lookup.TryGetValue(key, out var clip) ? clip : null;
        }
    }
}
