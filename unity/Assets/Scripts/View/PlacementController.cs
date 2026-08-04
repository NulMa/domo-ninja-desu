using System.Collections.Generic;
using DomoNinja.Core.Data;
using DomoNinja.Core.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DomoNinja.Unity.View
{
    /// <summary>
    /// 배치 조정 입력 — 드래그앤드롭과 클릭-선택 후 클릭-배치를 동시에 지원한다 (`08` §5-5, `D-53`, `D-75`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>겹침을 애초에 만들지 않는다.</b> 대상 칸이 비어 있지 않으면 두 유닛의 자리를 맞바꾼다 —
    /// "겹쳤다"는 상태 자체가 여기서 발생하지 않는다. 그래도 최종 검증은 core
    /// (<c>BattleSetup.ApplyPlayerPlacement</c>)가 한다 — View 를 신뢰하지 않는다는 `D-75`의 원칙은 그대로다.
    /// </para>
    /// <para>
    /// 좌표는 격자로만 존재한다. 유닛에 콜라이더를 달지 않고, 마우스의 월드 좌표를
    /// <see cref="BoardView.CoordAt"/> 로 바로 칸으로 되돌려 "이 칸에 누가 있는가"를 배치 사전으로 판정한다.
    /// </para>
    /// </remarks>
    public sealed class PlacementController : MonoBehaviour
    {
        private static readonly int[] RowOrder = { 2, 3, 1, 4, 0, 5 };

        private BoardView _board;
        private readonly Dictionary<string, Coord> _placement = new Dictionary<string, Coord>();

        private string _selected;
        private string _dragging;
        private Coord _dragOrigin;

        public IReadOnlyDictionary<string, Coord> Placement => _placement;

        /// <param name="previousPlacement">
        /// 지난 라운드 좌표. 살아 있는 아군이 이어받는다 — 없거나 무효하면 기본 자리를 새로 배정한다.
        /// </param>
        public void Setup(BoardView board, IReadOnlyList<string> allyCharacterIds,
                          IReadOnlyDictionary<string, Coord> previousPlacement,
                          IReadOnlyList<EnemyPlacement> enemies)
        {
            _board = board;
            _placement.Clear();
            _selected = null;
            _dragging = null;

            var occupied = new HashSet<int>();

            foreach (string id in allyCharacterIds)
            {
                Coord at;
                if (previousPlacement != null && previousPlacement.TryGetValue(id, out at) &&
                    at.IsAllyZone && occupied.Add(at.OrderKey))
                {
                    // 이전 라운드 자리를 그대로 이어받는다.
                }
                else
                {
                    at = NextFreeCell(occupied);
                    occupied.Add(at.OrderKey);
                }

                _placement[id] = at;
            }

            _board.ShowPlacementPreview(_placement, enemies);
        }

        /// <summary>앞열부터, 가운데 행부터 채운다 — `BattleSetup.PlaceAllies` 표준 배치와 같은 순서다.</summary>
        private static Coord NextFreeCell(HashSet<int> occupied)
        {
            for (int col = Coord.AllyMaxX; col >= 0; col--)
                foreach (int row in RowOrder)
                {
                    var c = new Coord(col, row);
                    if (!occupied.Contains(c.OrderKey)) return c;
                }

            return Coord.Origin; // 24칸에 최대 3명 — 이론상 도달하지 않는다.
        }

        private void Update()
        {
            if (_board == null || Mouse.current == null) return;

            // UI 버튼 위 클릭은 배치로 새지 않는다. 이미 드래그 중이면 버튼 위에서 놓쳐도 계속 따라간다.
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUi && _dragging == null) return;

            if (!overUi && Mouse.current.leftButton.wasPressedThisFrame) OnPress();

            if (_dragging != null)
            {
                _board.SetAllyPreviewFreePosition(_dragging, PointerWorld());

                if (Mouse.current.leftButton.wasReleasedThisFrame) OnRelease();
            }
        }

        private void OnPress()
        {
            var coord = PointerCoord();
            string hit = CharacterAt(coord);

            if (_selected != null)
            {
                if (hit == _selected)
                {
                    Deselect();
                    return;
                }

                if (coord.IsAllyZone)
                {
                    MoveOrSwap(_selected, coord);
                    Deselect();
                    return;
                }
            }

            if (hit != null)
            {
                _dragging = hit;
                _dragOrigin = _placement[hit];
            }
        }

        private void OnRelease()
        {
            string moved = _dragging;
            _dragging = null;
            if (moved == null) return;

            var coord = PointerCoord();

            if (!coord.IsAllyZone || coord.OrderKey == _dragOrigin.OrderKey)
            {
                // 진영 밖에 놓거나 제자리에 놓았다 — 되돌리고, 움직이지 않았다면 클릭으로 본다(선택 토글).
                _board.MoveAllyPreview(moved, _dragOrigin);

                if (coord.OrderKey == _dragOrigin.OrderKey)
                {
                    _selected = _selected == moved ? null : moved;
                    _board.SetPlacementSelected(_selected);
                }

                return;
            }

            MoveOrSwap(moved, coord);
            Deselect();
        }

        private void MoveOrSwap(string characterId, Coord to)
        {
            string other = CharacterAt(to);
            var from = _placement[characterId];

            _placement[characterId] = to;
            _board.MoveAllyPreview(characterId, to);

            if (other != null)
            {
                _placement[other] = from;
                _board.MoveAllyPreview(other, from);
            }
        }

        private void Deselect()
        {
            _selected = null;
            _board.SetPlacementSelected(null);
        }

        private string CharacterAt(Coord coord)
        {
            foreach (var kv in _placement)
                if (kv.Value.OrderKey == coord.OrderKey) return kv.Key;
            return null;
        }

        private Coord PointerCoord() => BoardView.CoordAt(PointerWorld());

        /// <summary>
        /// 화면 좌표를 보드 평면(월드 z=0)의 월드 좌표로 바꾼다.
        /// </summary>
        /// <remarks>
        /// ★ <c>ScreenToWorldPoint</c> 의 z 는 <b>카메라로부터의 거리</b>다 — 0 을 넘기면 카메라 코앞이
        /// 나온다. 직교 카메라가 z=0 평면에 놓인 보드를 바라보므로, 그 거리는 카메라 z 좌표의 부호를 뒤집은 값이다.
        /// </remarks>
        private static Vector3 PointerWorld()
        {
            var camera = Camera.main;
            Vector2 screen = Mouse.current.position.ReadValue();
            float distance = -camera.transform.position.z;
            return camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, distance));
        }
    }
}
