using System;
using System.Collections.Generic;
using UnityEngine;

namespace DomoNinja.Unity.View
{
    /// <summary>
    /// 데이터의 <c>sprite</c> 경로를 실제 <see cref="Sprite"/> 로 잇는 표.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>런타임은 파일 경로로 스프라이트를 찾을 수 없다.</b>
    /// <c>Assets/Sprite/...</c> 는 빌드에 그대로 들어가지 않고, 참조된 것만 실려 나간다.
    /// 그래서 <b>에디터가 미리 표를 만들고 런타임은 그 표만 본다</b> —
    /// <c>DataSync</c> 가 <c>/data</c> 를 옮기는 것과 같은 구조다.
    /// </para>
    /// <para>
    /// 이 표에 없는 유닛은 <b>화면에 안 보이는 게 아니라 자리표시자로 보인다.</b>
    /// 안 보이게 두면 "스프라이트가 없는 것"과 "유닛이 안 만들어진 것"이 구분되지 않는다.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "SpriteCatalog", menuName = "DomoNinja/Sprite Catalog")]
    public sealed class SpriteCatalog : ScriptableObject
    {
        /// <summary>`Resources.Load` 로 찾을 이름. 에디터 빌더가 같은 이름으로 만든다.</summary>
        public const string ResourceName = "SpriteCatalog";

        [Serializable]
        public struct Entry
        {
            /// <summary>데이터의 `sprite` 값 그대로 (`Actor/Character/Samurai`).</summary>
            public string Key;
            public Sprite Sprite;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        private Dictionary<string, Sprite> _lookup;

        public int Count => _entries.Length;

        public void SetEntries(Entry[] entries)
        {
            _entries = entries;
            _lookup = null;
        }

        /// <summary>없으면 <c>null</c>. 호출부가 자리표시자를 세운다.</summary>
        public Sprite Find(string key)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<string, Sprite>(_entries.Length);
                foreach (var e in _entries)
                {
                    if (!string.IsNullOrEmpty(e.Key)) _lookup[e.Key] = e.Sprite;
                }
            }

            return _lookup.TryGetValue(key, out var sprite) ? sprite : null;
        }
    }
}
