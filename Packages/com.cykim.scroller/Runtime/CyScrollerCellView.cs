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

        /// <summary>현재 활성 범위에 있어 화면(또는 미리보기 구간)에 배치돼 있는지.</summary>
        public bool Active { get; internal set; }

        /// <summary>이 뷰를 만든 스크롤러.</summary>
        public CyScroller Scroller { get; internal set; }

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

        protected virtual void Reset()
        {
            _cellIdentifier = gameObject.name;
        }
    }
}
