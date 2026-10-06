using UnityEngine;

namespace CyKim.Scroller
{
    /// <summary>
    /// 스크롤러가 재활용하는 셀 뷰의 기반 클래스. 프리팹 루트에 붙이고 상속해서 바인딩 코드를 넣는다.
    /// </summary>
    /// <remarks>
    /// 같은 <see cref="CellIdentifier"/>끼리 한 풀을 공유한다. 레이아웃이 다른 프리팹은 식별자를 다르게 둔다.
    /// 재사용된 뷰에는 이전 데이터의 상태(선택 표시·리스너·코루틴 등)가 남아 있으므로 바인딩 때 모두 덮어쓴다.
    /// </remarks>
    [DisallowMultipleComponent]
    public class CyScrollerCellView : MonoBehaviour
    {
        [Tooltip("재활용 풀 키. 같은 프리팹 종류끼리만 같게 둔다.")]
        [SerializeField] private string _cellIdentifier;

        private RectTransform _rectTransform;

        public string CellIdentifier
        {
            get => _cellIdentifier;
            set => _cellIdentifier = value;
        }

        /// <summary>바인딩된 데이터 인덱스. 풀에 있으면 -1.</summary>
        public int DataIndex { get; internal set; } = -1;

        /// <summary>스크롤 시퀀스 슬롯 번호. 루프 모드에서는 <see cref="DataIndex"/>와 다르다. 풀에 있으면 -1.</summary>
        public int CellIndex { get; internal set; } = -1;

        /// <summary>
        /// 바인딩된 항목의 안정 ID (델리게이트가 <see cref="ICyScrollerItemIdProvider"/>를 구현할 때). <see cref="HasItemId"/>가 false면 0.
        /// 바인딩할 때 채워지고(<see cref="CyScroller.GetCellView"/>로 받은 뷰는 델리게이트 <see cref="ICyScrollerDelegate.GetCellView"/> 안에서 이미 들어 있다),
        /// 바인딩이 풀리면(회수, <see cref="CyScroller.ClearActive"/>로 파괴) 지워진다. 마지막으로 델리게이트를 다시 받을 때 받은 ID다.
        /// </summary>
        public long ItemId { get; internal set; }

        /// <summary><see cref="ItemId"/>가 유효한지. 델리게이트가 ID를 주지 않거나 바인딩이 풀린 뷰는 false.</summary>
        public bool HasItemId { get; internal set; }

        /// <summary>현재 활성 범위에 있어 화면(또는 미리보기 구간)에 배치돼 있는지.</summary>
        public bool Active { get; internal set; }

        /// <summary>이 뷰를 만든 스크롤러.</summary>
        public CyScroller Scroller { get; internal set; }

        /// <summary>
        /// 바인딩 세대. 스크롤러가 이 뷰를 데이터에 바인딩할 때(<see cref="CyScroller.GetCellView"/>가 활성화할 슬롯용으로 내줄 때)와
        /// 바인딩을 풀 때(풀로 돌려보낼 때, <see cref="CyScroller.ClearActive"/>로 파괴할 때, 활성인 채로 스크롤러와 함께 파괴될 때) 1씩 는다.
        /// 같은 데이터로 남는 갱신(<see cref="RefreshCellView"/>, 루프 순환 보정, 증분 변경으로 인덱스만 바뀔 때(<see cref="OnDataIndexChanged"/>))에는 늘지 않는다.
        /// 스크롤러를 거치지 않고 셀을 직접 파괴하면 늘지 않는다.
        /// </summary>
        /// <example>
        /// 비동기 로드는 시작할 때 값을 기억해 두고, 끝났을 때 값이 다르면(그사이 재활용·재바인딩되거나 스크롤러와 함께 파괴됨) 결과를 버린다.
        /// <code>
        /// public void SetData(ItemData item)
        /// {
        ///     int version = BindVersion;
        ///     _icon.sprite = null;
        ///     IconLoader.Load(item.IconKey, sprite =>
        ///     {
        ///         if (version == BindVersion)
        ///         {
        ///             _icon.sprite = sprite;   // 아직 같은 바인딩일 때만 그린다
        ///         }
        ///     });
        /// }
        /// </code>
        /// </example>
        public int BindVersion { get; internal set; }

        /// <summary>
        /// 데이터에 바인딩돼 있는지 (<see cref="DataIndex"/> 0 이상). 풀에 있거나 <see cref="CyScroller.ClearActive"/>·스크롤러 파괴로 바인딩이 풀린 뷰는 false.
        /// </summary>
        public bool IsBound => DataIndex >= 0;

        /// <summary>
        /// 실제 뷰포트(lookAhead 구간 제외)에 걸쳐 있는지. <see cref="OnBecameVisible"/> 직전에 true, <see cref="OnBecameHidden"/> 직전에 false가 된다.
        /// 스크롤러와 함께 파괴될 때는 표시 끝 없이 마지막 값으로 남는다. 표시 중 시작한 일(노출 타이머 등)은 셀의 OnDestroy에서 이 값을 보고 정리한다.
        /// </summary>
        public bool IsDisplayed { get; internal set; }

        public RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null)
                {
                    _rectTransform = transform as RectTransform;
                }

                return _rectTransform;
            }
        }

        /// <summary><see cref="CyScroller.RefreshActiveCellViews"/>가 활성 셀마다 호출한다. 다시 바인딩할 때 쓴다.</summary>
        public virtual void RefreshCellView()
        {
        }

        /// <summary>풀로 돌아가기 직전에 호출된다. 이전 데이터 상태를 정리할 때 쓴다.</summary>
        public virtual void OnRecycled()
        {
        }

        /// <summary>
        /// 증분 변경(<see cref="CyScroller.InsertCells"/>·<see cref="CyScroller.RemoveCells"/>·<see cref="CyScroller.MoveCell"/>)으로 다시 바인딩하지 않고
        /// 같은 항목의 인덱스만 바뀌었을 때. <see cref="DataIndex"/>·<see cref="CellIndex"/>는 이미 새 값이고 <see cref="BindVersion"/>은 그대로다.
        /// 이 호출 뒤에 새 위치로 옮겨진다. 인덱스를 화면에 그리는 셀은 여기서 다시 그린다.
        /// </summary>
        /// <param name="previousDataIndex">바뀌기 전 데이터 인덱스.</param>
        protected internal virtual void OnDataIndexChanged(int previousDataIndex)
        {
        }

        /// <summary>
        /// 실제 뷰포트(lookAhead 구간 제외)에 조금이라도 걸치기 시작할 때. <see cref="CyScroller.CellViewWillDisplay"/> 바로 뒤에 불린다.
        /// 등장 애니메이션·노출 기록처럼 정말 보일 때 할 일에 쓴다. <see cref="OnBecameHidden"/>과 항상 짝을 이룬다
        /// (스크롤러와 함께 파괴될 때만 예외, <see cref="IsDisplayed"/> 참고).
        /// </summary>
        protected internal virtual void OnBecameVisible()
        {
        }

        /// <summary>
        /// 뷰포트에서 완전히 벗어나거나, 보이던 채로 재활용되거나 <see cref="CyScroller.ClearActive"/>로 파괴될 때.
        /// <see cref="CyScroller.CellViewDidEndDisplay"/> 바로 뒤, 재활용이면 <see cref="OnRecycled"/>보다 먼저 불린다.
        /// 스크롤러와 함께 파괴될 때는 불리지 않는다.
        /// </summary>
        protected internal virtual void OnBecameHidden()
        {
        }

        /// <summary>
        /// <see cref="CyScroller.NotifyCellPositions"/>가 켜져 있을 때 뷰포트 안 위치를 받는다. 위치·활성 범위·레이아웃·뷰포트 크기가 바뀐 프레임에
        /// 한 번(스크롤러 LateUpdate 끝) 불리고, 새로 활성화된 셀은 활성화 즉시 한 번 더 받는다.
        /// </summary>
        /// <param name="normalizedOffset">
        /// (셀 기준점 − 뷰포트 시작) / 뷰포트 길이. 기준점은 셀 시작 + 셀 크기 × <see cref="CyScroller.CellPositionPivot"/>.
        /// 0 = 뷰포트 앞(위·왼쪽) 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리. 미리보기 구간 셀은 0 미만·1 초과가 될 수 있다.
        /// </param>
        /// <remarks>셀 루트 RectTransform은 스크롤러가 배치하므로 여기서는 자식(스케일·회전·투명도 등)만 바꾼다.</remarks>
        protected internal virtual void OnViewportPositionChanged(float normalizedOffset)
        {
        }

        protected virtual void Reset()
        {
            _cellIdentifier = gameObject.name;
        }
    }
}
