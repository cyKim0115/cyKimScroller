using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CyKim.Scroller
{
    /// <summary>
    /// 가상화 스크롤러. ScrollRect와 같은 GameObject에 붙여서, 보이는 구간의 셀 뷰만 만들고 나머지는 빈 스크롤 길이로 둔다.
    /// </summary>
    /// <remarks>
    /// <para>드래그·관성·탄성은 표준 ScrollRect가 처리한다. CyScroller는 onValueChanged를 받아 활성 셀 범위를 갱신한다.</para>
    /// <para>콘텐츠에 LayoutGroup을 쓰지 않는다. 셀 RectTransform을 계산된 위치에 직접 배치한다.</para>
    /// <para>사용 흐름: <see cref="Delegate"/>에 <see cref="ICyScrollerDelegate"/>를 넣고 <see cref="ReloadData()"/>를 호출한다.</para>
    /// </remarks>
#if UNITY_6000_3_OR_NEWER
    [AddComponentMenu("UI (Canvas)/CyKim Scroller")]
#else
    [AddComponentMenu("UI/CyKim Scroller")]
#endif
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScrollRect))]
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public partial class CyScroller : MonoBehaviour
    {
        // ScrollRect.LateUpdate가 드래그·관성을 반영한 뒤에 트윈·스냅을 처리하려고 뒤에 돈다.
        private const int EXECUTION_ORDER = 100;

        // 잘못된 셀 크기(대부분 0) 때문에 한 프레임에 셀 수만 개를 만드는 일을 막는 상한.
        private const int MAX_ACTIVE_CELLS = 2048;

        // 범위 갱신 콜백이 콘텐츠를 옮겼을 때 지금 위치로 범위를 다시 맞추는 최대 횟수. 콜백이 매번 다시 옮기는 경우를 끊는다.
        internal const int MAX_RANGE_PASSES = 4;

        // Profiler에서 보이는 구간. Auto()는 구조체 스코프라 할당하지 않는다.
        private static readonly ProfilerMarker _updateActiveRangeMarker = new ProfilerMarker("CyScroller.UpdateActiveRange");
        private static readonly ProfilerMarker _relayoutMarker = new ProfilerMarker("CyScroller.Relayout");

        [Header("Layout")]
        [SerializeField] private ScrollDirection _scrollDirection = ScrollDirection.Vertical;
        [Tooltip("셀 사이 간격.")]
        [SerializeField, Min(0f)] private float _spacing;
        [Tooltip("콘텐츠 가장자리 여백. 스크롤 축 방향은 앞뒤 여백, 교차 축은 셀 좌우(또는 위아래) 여백.")]
        [SerializeField] private RectOffset _padding = new RectOffset();
        [Tooltip("뷰포트 앞쪽으로 미리 만들어 둘 거리.")]
        [SerializeField, Min(0f)] private float _lookAheadBefore;
        [Tooltip("뷰포트 뒤쪽으로 미리 만들어 둘 거리.")]
        [SerializeField, Min(0f)] private float _lookAheadAfter;

        [Header("Loop")]
        [SerializeField] private bool _loop;
        [Tooltip("끄면 드래그하는 동안에는 순환 보정을 하지 않는다 (놓을 때 보정).")]
        [SerializeField] private bool _loopWhileDragging = true;

        [Header("Scrolling")]
        [Tooltip("루프 모드에서는 항상 숨긴다.")]
        [SerializeField] private ScrollbarVisibility _scrollbarVisibility = ScrollbarVisibility.OnlyIfNeeded;
        [Tooltip("관성 속도 상한. 0이면 제한 없음.")]
        [SerializeField, Min(0f)] private float _maxVelocity;

        [Header("Snapping")]
        [SerializeField] private bool _snapping;
        [Tooltip("드래그를 놓은 뒤 속도가 이 값 이하로 떨어지면 스냅한다.")]
        [SerializeField, Min(0f)] private float _snapVelocityThreshold = 200f;
        [Tooltip("뷰포트에서 스냅할 셀을 고르는 지점. 0 = 앞, 1 = 뒤.")]
        [SerializeField] private float _snapWatchOffset = 0.5f;
        [Tooltip("스냅된 셀을 맞출 뷰포트 지점. 0 = 앞, 1 = 뒤.")]
        [SerializeField] private float _snapJumpToOffset = 0.5f;
        [Tooltip("뷰포트 지점에 맞출 셀 안의 지점. 0 = 앞, 1 = 뒤.")]
        [SerializeField] private float _snapCellCenterOffset = 0.5f;
        [Tooltip("셀 앞뒤 간격의 절반씩을 셀 영역에 포함해 계산한다.")]
        [SerializeField] private bool _snapUseCellSpacing;
        [SerializeField] private TweenType _snapTweenType = TweenType.EaseOutCubic;
        [SerializeField, Min(0f)] private float _snapTweenTime = 0.25f;

        [Header("Tween")]
        [Tooltip("트윈 중 포인터를 누르면(드래그 전이라도) 트윈을 멈춘다.")]
        [SerializeField] private bool _interruptTweenOnPointerDown = true;
        [Tooltip("TweenType.Custom에 쓰는 곡선.")]
        [SerializeField] private AnimationCurve _customTweenCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Cell Hooks")]
        [Tooltip("켜면 위치·범위·레이아웃·뷰포트 크기가 바뀐 프레임에 활성 셀마다 OnViewportPositionChanged를 부른다.")]
        [SerializeField] private bool _notifyCellPositions;
        [Tooltip("셀 위치를 잴 셀 안의 지점. 0 = 앞, 0.5 = 가운데, 1 = 뒤.")]
        [SerializeField, Range(0f, 1f)] private float _cellPositionPivot = 0.5f;

        [Header("Reload")]
        [Tooltip("켜면 델리게이트가 항목 ID를 줄 때 위치를 지키는 리로드(FirstVisible·LastVisible·앵커 지정·ReloadDataKeepingPosition)가 " +
            "ID가 같은 활성 셀을 다시 바인딩하지 않고 새 인덱스로 옮겨 쓴다. 내용 변경은 감지하지 않으므로 바뀐 항목은 RefreshCells로 알린다.")]
        [SerializeField] private bool _preserveCellsById;

        private readonly CyScrollerLayout _layout = new CyScrollerLayout();
        private readonly List<CyScrollerCellView> _activeCells = new List<CyScrollerCellView>(32);
        private readonly Dictionary<string, List<CyScrollerCellView>> _pools = new Dictionary<string, List<CyScrollerCellView>>();

        private ScrollRect _scrollRect;
        private RectTransform _content;
        private RectTransform _viewport;
        private UnityAction<Vector2> _onScrollRectValueChanged;
        private ICyScrollerDelegate _delegate;

        private bool _initialized;
        private bool _hasLoaded;
        private bool _inRangeUpdate;
        private bool _inValueChanged;

        // 범위 갱신 콜백이 매번 콘텐츠를 옮겨 다시 맞추기를 멈췄다. 다음 LateUpdate에 지금 위치로 다시 맞춘다.
        private bool _rangeRetryPending;
        private bool _listening;
        private bool _isScrolling;
        private bool _warnedEmptyIdentifier;
        private bool _warnedActiveCap;
        private float _lastViewportExtent = -1f;

        // content가 실제로 맞춰져 있는 축. 방향을 바꾼 직후 이전 축으로 위치를 읽는 데 쓴다.
        private bool _appliedVertical = true;

        // 범위 갱신(델리게이트·이벤트 콜백) 안에서 들어온 요청은 끝난 뒤 처리한다. 리로드는 다시 읽은 뒤 맞출 위치 기준과 키 유지 여부도 보관한다.
        private bool _reloadPending;
        private ReloadAnchor _pendingReloadAnchor;
        private float _pendingReloadFactor;
        private bool _pendingReloadPreservesCells;
        private bool _relayoutPending;
        private bool _relayoutRequery;
        private bool _relayoutReconfigure;
        private bool _clearActivePending;
        private bool _clearRecycledPending;

        // 숨김(Never·루프) 때문에 ScrollRect에서 떼어 둔 스크롤바.
        private Scrollbar _detachedScrollbar;
        private bool _detachedScrollbarVertical;

        // 활성 슬롯 범위. 비었으면 _activeFirst > _activeLast. _activeCells[i]는 슬롯 _activeFirst + i.
        private int _activeFirst;
        private int _activeLast = -1;

        // GetCellView(prefab)가 델리게이트 안에서 호출될 때 셀에 미리 넣어 줄 인덱스.
        private int _pendingDataIndex = -1;
        private int _pendingCellIndex = -1;

        #region Events

        /// <summary>
        /// 셀 뷰가 활성화되거나(lookAhead 미리보기 구간 포함) 재활용될 때. 실제 뷰포트 기준은 <see cref="CellViewWillDisplay"/>·<see cref="CellViewDidEndDisplay"/>.
        /// </summary>
        public event CellViewVisibilityChangedHandler CellViewVisibilityChanged;

        /// <summary>
        /// 셀 뷰가 실제 뷰포트(lookAhead 구간 제외)에 조금이라도 걸치기 시작할 때. 셀이 활성화된 뒤에 오고,
        /// 같은 셀에 <see cref="CellViewDidEndDisplay"/>와 항상 짝을 이룬다 (스크롤러 자체가 파괴될 때만 예외).
        /// 루프 순환 보정(같은 데이터로 슬롯 번호만 이동)에는 오지 않는다.
        /// </summary>
        public event CellViewDisplayChangedHandler CellViewWillDisplay;

        /// <summary>
        /// 셀 뷰가 실제 뷰포트에서 완전히 벗어나거나, 보이던 채로 재활용되거나 <see cref="ClearActive"/>로 파괴될 때(<see cref="ReloadData()"/>·재배치 등).
        /// 재활용 이벤트(<see cref="CellViewWillRecycle"/>)보다 먼저 온다.
        /// 스크롤러 자체가 파괴될 때는 사용자 코드를 부르지 않으므로 오지 않는다 (<see cref="CyScrollerCellView.IsDisplayed"/>가 마지막 값으로 남는다).
        /// </summary>
        public event CellViewDisplayChangedHandler CellViewDidEndDisplay;

        /// <summary>
        /// <see cref="NotifyCellPositions"/>가 켜져 있을 때 셀 뷰의 뷰포트 안 위치. <see cref="CyScrollerCellView.OnViewportPositionChanged"/> 바로 앞에 같은 값으로 온다.
        /// </summary>
        public event CellViewPositionChangedHandler CellViewPositionChanged;

        public event CellViewInstantiatedHandler CellViewInstantiated;
        public event CellViewReusedHandler CellViewReused;
        public event CellViewWillRecycleHandler CellViewWillRecycle;
        public event ScrollerScrolledHandler ScrollerScrolled;

        /// <summary>
        /// 스냅이 끝났을 때. cellIndex는 루프 순환 보정 뒤 슬롯이고, cellView는 그 슬롯의 활성 셀 뷰다(없으면 null).
        /// 범위 갱신 콜백(셀 이벤트·델리게이트 등) 안에서 끝난 스냅은 범위 갱신이 끝난 뒤(미룬 순환 보정과 새 위치의 셀 활성화 뒤) 온다.
        /// </summary>
        public event ScrollerSnappedHandler ScrollerSnapped;
        public event ScrollerScrollingChangedHandler ScrollerScrollingChanged;
        public event ScrollerTweeningChangedHandler ScrollerTweeningChanged;

        #endregion

        #region Properties

        /// <summary>
        /// 데이터 공급자. 넣으면 다음 LateUpdate(또는 그 전의 첫 범위 갱신)에 <see cref="ReloadData()"/>한다
        /// (처음 위치로, 그 전에 <see cref="RestoreAnchor"/>로 보관한 앵커가 있으면 그 자리로). 새 델리게이트가 옛 인덱스로 호출되는 일은 없다.
        /// <see cref="ICyScrollerItemIdProvider"/>도 구현했으면 다시 읽을 때마다 항목 ID를 받는다.
        /// </summary>
        public ICyScrollerDelegate Delegate
        {
            get => _delegate;
            set
            {
                _delegate = value;

                // 선택 구현인 ID 제공자는 넣을 때 한 번만 확인한다. 항목 ID는 다시 읽을 때 받는다.
                _idProvider = value as ICyScrollerItemIdProvider;
                _reloadPending = true;
                _pendingReloadAnchor = ReloadAnchor.Factor;
                _pendingReloadFactor = 0f;
                _pendingReloadPreservesCells = false;

                // 옛 델리게이트가 바인딩한 셀은 ID가 같아도 새 델리게이트의 셀이 아니다. 그 전에 바로 부른 키 유지 리로드도 모두 다시 바인딩한다.
                _forceRebindOnReload = true;
            }
        }

        /// <summary>스크롤 축. 바꾸면 셀 크기를 다시 받고 맨 앞 셀 기준으로 위치를 유지한다.</summary>
        public ScrollDirection ScrollDirection
        {
            get => _scrollDirection;
            set
            {
                if (_scrollDirection == value)
                {
                    return;
                }

                _scrollDirection = value;
                RelayoutKeepingPosition(true, true);
            }
        }

        public float Spacing
        {
            get => _spacing;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(_spacing, value))
                {
                    return;
                }

                _spacing = value;
                RelayoutKeepingPosition(false, false);
            }
        }

        /// <summary>여백. 필드를 직접 바꿨다면 다시 대입하거나 <see cref="ReloadDataKeepingPosition"/>을 호출한다.</summary>
        public RectOffset Padding
        {
            get => _padding;
            set
            {
                _padding = value ?? new RectOffset();
                RelayoutKeepingPosition(false, false);
            }
        }

        public float LookAheadBefore
        {
            get => _lookAheadBefore;
            set
            {
                _lookAheadBefore = Mathf.Max(0f, value);
                OnCoverageChanged();
            }
        }

        public float LookAheadAfter
        {
            get => _lookAheadAfter;
            set
            {
                _lookAheadAfter = Mathf.Max(0f, value);
                OnCoverageChanged();
            }
        }

        /// <summary>끝과 처음을 이어 무한히 순환한다. 바꾸면 맨 앞 셀 기준으로 위치를 유지한다.</summary>
        public bool Loop
        {
            get => _loop;
            set
            {
                if (_loop == value)
                {
                    return;
                }

                _loop = value;
                RelayoutKeepingPosition(false, false);
                ApplyScrollbarVisibility();
            }
        }

        public bool LoopWhileDragging
        {
            get => _loopWhileDragging;
            set => _loopWhileDragging = value;
        }

        public ScrollbarVisibility ScrollbarVisibility
        {
            get => _scrollbarVisibility;
            set
            {
                _scrollbarVisibility = value;
                ApplyScrollbarVisibility();
            }
        }

        public float MaxVelocity
        {
            get => _maxVelocity;
            set => _maxVelocity = Mathf.Max(0f, value);
        }

        public bool Snapping
        {
            get => _snapping;
            set => _snapping = value;
        }

        public float SnapVelocityThreshold
        {
            get => _snapVelocityThreshold;
            set => _snapVelocityThreshold = Mathf.Max(0f, value);
        }

        public float SnapWatchOffset
        {
            get => _snapWatchOffset;
            set => _snapWatchOffset = value;
        }

        public float SnapJumpToOffset
        {
            get => _snapJumpToOffset;
            set => _snapJumpToOffset = value;
        }

        public float SnapCellCenterOffset
        {
            get => _snapCellCenterOffset;
            set => _snapCellCenterOffset = value;
        }

        public bool SnapUseCellSpacing
        {
            get => _snapUseCellSpacing;
            set => _snapUseCellSpacing = value;
        }

        public TweenType SnapTweenType
        {
            get => _snapTweenType;
            set => _snapTweenType = value;
        }

        public float SnapTweenTime
        {
            get => _snapTweenTime;
            set => _snapTweenTime = Mathf.Max(0f, value);
        }

        public bool InterruptTweenOnPointerDown
        {
            get => _interruptTweenOnPointerDown;
            set => _interruptTweenOnPointerDown = value;
        }

        public AnimationCurve CustomTweenCurve
        {
            get => _customTweenCurve;
            set => _customTweenCurve = value;
        }

        /// <summary>
        /// 켜면 위치·활성 범위·레이아웃·뷰포트 크기가 바뀐 프레임에 한 번(LateUpdate 끝) 활성 셀마다
        /// <see cref="CyScrollerCellView.OnViewportPositionChanged"/>와 <see cref="CellViewPositionChanged"/>를 부른다.
        /// 새로 활성화된 셀은 활성화 즉시 받는다. 끄면 위치 계산을 아예 건너뛴다.
        /// </summary>
        public bool NotifyCellPositions
        {
            get => _notifyCellPositions;
            set
            {
                if (_notifyCellPositions == value)
                {
                    return;
                }

                _notifyCellPositions = value;

                // 꺼져 있던 동안 바뀐 위치를 다음 LateUpdate에 모든 활성 셀에 알린다.
                _cellPositionsDirty |= value;
            }
        }

        /// <summary>셀 위치를 잴 셀 안의 지점 (0~1). 0 = 앞, 0.5 = 가운데, 1 = 뒤.</summary>
        public float CellPositionPivot
        {
            get => _cellPositionPivot;
            set
            {
                value = Mathf.Clamp01(value);
                if (_cellPositionPivot == value)
                {
                    return;
                }

                _cellPositionPivot = value;
                _cellPositionsDirty = true;
            }
        }

        /// <summary>
        /// 키 유지 리로드 (기본 false). 켜고 델리게이트가 <see cref="ICyScrollerItemIdProvider"/>를 구현하면, 위치를 지키며 다시 읽는 리로드
        /// (<see cref="ReloadData(ReloadAnchor, float)"/>의 FirstVisible·LastVisible, <see cref="ReloadData(in CyScrollerAnchor)"/>, <see cref="ReloadDataKeepingPosition"/>)가
        /// 항목 ID가 같은 활성 셀을 회수·재바인딩하지 않고 새 인덱스로 옮겨 쓴다. 인덱스가 바뀐 셀은 <see cref="CyScrollerCellView.OnDataIndexChanged"/>를 받고
        /// <see cref="CyScrollerCellView.BindVersion"/>은 그대로이며, 계속 보이는 셀에는 표시 이벤트가 없다. 지워진 ID의 셀은 회수하고 새로 활성 범위에 들어온 자리만 델리게이트로 받는다.
        /// 끄거나 ID 제공자가 없으면 지금처럼 모든 셀을 다시 바인딩한다.
        /// </summary>
        /// <remarks>
        /// <para><b>내용 변경은 감지하지 않는다.</b> 내용이 바뀐 항목은 리로드한 뒤 새 인덱스로 <see cref="RefreshCells"/>를, 셀 종류(프리팹)가 바뀐 항목은 <see cref="ReloadCellView"/>를 부른다.
        /// 델리게이트에 셀 종류만 따로 물을 방법이 없으므로 같은 ID는 같은 셀 종류로 보고 그 셀을 그대로 쓴다. 크기는 새로 받은 값으로 배치한다.
        /// 셀 프리팹을 통째로 바꿨으면 <see cref="ClearActive"/>를 먼저 부르거나 <see cref="ReloadData()"/>를 쓴다.
        /// 셀을 새 인덱스로 맞추기 전에 사용자 코드(트윈 멈춤 알림, 다시 읽는 중의 델리게이트)가 부른 <see cref="RefreshCells"/>는 새 인덱스로 보고 맞춘 뒤 남은 셀에 부른다.</para>
        /// <para>처음부터 다시 그리거나 위치를 정하는 리로드(<see cref="ReloadData()"/>·<see cref="ReloadData(float)"/>, ReloadAnchor의 Factor·Start·End),
        /// 다시 읽지 않는 재배치(<see cref="Spacing"/>·<see cref="Padding"/>·<see cref="Loop"/> 등)와 축 전환, 증분 변경이 리로드로 바뀌는 경우(개수 불일치·루프 모드·콜백 안)는
        /// 옵션과 무관하게 지금처럼 모두 다시 바인딩한다. 델리게이트를 바꾼 뒤 처음 다시 읽을 때와, 리로드를 기다리는 동안 알린 내용 갱신·다시 받기
        /// (<see cref="RefreshCells"/>·<see cref="ReloadCellView"/>)를 그 리로드가 대신 처리해야 할 때도 모두 다시 바인딩한다.</para>
        /// <para>루프 모드에서는 같은 항목의 사본 중 셀이 화면에 있던 자리에 가장 가까운 사본으로 옮긴다. 같은 ID가 여럿이면 셀마다 이전 인덱스 자리에 같은 ID가 남아 있으면 그 자리,
        /// 아니면 앞 인덱스로 찾고, 한 자리에는 셀 하나만 옮긴다(나머지는 회수).</para>
        /// </remarks>
        public bool PreserveCellsById
        {
            get => _preserveCellsById;
            set => _preserveCellsById = value;
        }

        public ScrollRect ScrollRect
        {
            get
            {
                EnsureInitialized();
                return _scrollRect;
            }
        }

        /// <summary>셀이 배치되는 콘텐츠 RectTransform.</summary>
        public RectTransform Container
        {
            get
            {
                EnsureInitialized();
                return _content;
            }
        }

        /// <summary>논리 데이터 개수 (마지막 로드·증분 변경 기준. 증분 변경 배치 중에는 배치 전 값).</summary>
        public int NumberOfCells => _layout.DataCount;

        /// <summary>스크롤 시퀀스 슬롯 개수. 루프면 데이터 개수 × 세트 수.</summary>
        public int NumberOfCellSlots => _layout.SlotCount;

        /// <summary>
        /// 콘텐츠 시작(위·왼쪽)에서 뷰포트 시작까지 거리. 대입하면 진행 중인 트윈·관성·점프 정렬을 멈추고 그 위치로 옮긴다.
        /// 드래그 중이면 손가락 아래 기준점도 새 위치로 옮긴다. 적용을 기다리는 앵커(<see cref="RestoreAnchor"/>)는 버린다.
        /// </summary>
        public float ScrollPosition
        {
            get => _content != null ? ReadPosition(_appliedVertical) : 0f;
            set
            {
                if (!EnsureInitialized())
                {
                    return;
                }

                RecoverBeforeMove();
                CancelTween();
                _alignmentActive = false;
                _snapArmed = false;
                _hasPendingAnchor = false;
                _scrollRect.StopMovement();
                MoveContentTo(Mathf.Clamp(value, 0f, ScrollSize));
                UpdateActiveRange();
            }
        }

        /// <summary>
        /// 0 = 처음, 1 = 끝. 루프 모드에서는 한 사이클 안의 위치(0 이상 1 미만)이고, 대입은 같은 셀 배치를 뜻한다.
        /// </summary>
        public float NormalizedScrollPosition
        {
            get
            {
                if (_layout.IsLoop)
                {
                    float relative = (ScrollPosition - _layout.MiddleSetStart) / _layout.CycleExtent;
                    return relative - Mathf.Floor(relative);
                }

                float size = ScrollSize;
                return size > 0f ? Mathf.Clamp01(ScrollPosition / size) : 0f;
            }
            set
            {
                if (!EnsureInitialized())
                {
                    return;
                }

                RecoverBeforeMove();
                ScrollPosition = GetPositionForFactor(value);
            }
        }

        /// <summary>스크롤 가능한 거리 = 콘텐츠 길이 − 뷰포트 길이 (0 이상).</summary>
        public float ScrollSize => Mathf.Max(0f, _layout.ContentExtent - ScrollRectSize);

        /// <summary>뷰포트의 스크롤 축 길이.</summary>
        public float ScrollRectSize => ReadViewportExtent(IsVertical);

        /// <summary>콘텐츠 전체 길이 (패딩 포함).</summary>
        public float ContentSize => _layout.ContentExtent;

        /// <summary>활성 범위 첫 슬롯. 비었으면 -1.</summary>
        public int StartCellViewIndex => HasActiveRange ? _activeFirst : -1;

        /// <summary>활성 범위 마지막 슬롯. 비었으면 -1.</summary>
        public int EndCellViewIndex => HasActiveRange ? _activeLast : -1;

        /// <summary>활성 범위 첫 데이터 인덱스. 비었으면 -1.</summary>
        public int StartDataIndex => HasActiveRange ? _layout.SlotToDataIndex(_activeFirst) : -1;

        /// <summary>활성 범위 마지막 데이터 인덱스. 비었으면 -1.</summary>
        public int EndDataIndex => HasActiveRange ? _layout.SlotToDataIndex(_activeLast) : -1;

        /// <summary>
        /// 활성 셀 뷰 목록 (슬롯 순서). 델리게이트가 null을 반환한 슬롯은 null 항목이다.
        /// GC를 피하려면 foreach 대신 for와 인덱서로 순회한다.
        /// </summary>
        public IReadOnlyList<CyScrollerCellView> ActiveCellViews => _activeCells;

        /// <summary>드래그 중이거나 관성으로 움직이는 중. 트윈 이동은 제외한다.</summary>
        public bool IsScrolling => _isScrolling;

        public bool IsTweening => _tweening;

        public bool IsDragging => _dragging;

        public Vector2 Velocity
        {
            get => _scrollRect != null ? _scrollRect.velocity : Vector2.zero;
            set
            {
                if (EnsureInitialized())
                {
                    _scrollRect.velocity = value;
                }
            }
        }

        /// <summary>스크롤 축 속도. 양수면 스크롤 위치가 커지는 방향(아래·오른쪽).</summary>
        public float LinearVelocity
        {
            get
            {
                if (_scrollRect == null)
                {
                    return 0f;
                }

                Vector2 velocity = _scrollRect.velocity;
                return _appliedVertical ? velocity.y : -velocity.x;
            }
            set
            {
                if (EnsureInitialized())
                {
                    _scrollRect.velocity = _appliedVertical ? new Vector2(0f, value) : new Vector2(-value, 0f);
                }
            }
        }

        private bool IsVertical => _scrollDirection == ScrollDirection.Vertical;
        private bool HasActiveRange => _activeFirst <= _activeLast;

        internal CyScrollerLayout Layout => _layout;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // AddComponent 직후에는 ScrollRect.content가 아직 비어 있을 수 있다. 여기서는 조용히 넘어가고
            // 첫 공개 API 호출 때 다시 초기화한다.
            EnsureInitialized(false);
        }

        private void OnEnable()
        {
            if (EnsureInitialized(false))
            {
                StartListening();
            }
        }

        private void OnDisable()
        {
            StopListening();
            CancelTween();
            _dragging = false;
            _dragEventData = null;
            _pressEventData = null;
            _snapArmed = false;
            SetScrolling(false);
        }

        private void OnDestroy()
        {
            // 함께 파괴되는 활성 셀의 바인딩을 푼다. 늦게 끝난 비동기 작업이 BindVersion을 비교해 결과를 버리게 한다.
            // 파괴 순서가 정해져 있지 않으므로 사용자 코드(표시 끝 이벤트·OnBecameHidden)는 부르지 않고 IsDisplayed는 마지막 값으로 둔다.
            // 이미 파괴된 셀(Unity null)도 관리 쪽 값만 바꾸므로 참조로 확인한다.
            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                if (!ReferenceEquals(cell, null))
                {
                    UnbindCell(cell);
                }
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || !_hasLoaded)
            {
                return;
            }

            // 인스펙터 변경은 다음 LateUpdate에 한 번에 반영한다. 축이 바뀌었으면 셀 크기도 다시 받는다.
            _relayoutPending = true;
            _relayoutReconfigure = true;
            _relayoutRequery |= IsVertical != _appliedVertical;
        }

        private void LateUpdate()
        {
            if (_updateDepth > 0)
            {
                // 증분 변경 배치가 열려 있으면 아래 작업이 모두 미뤄진다. 프레임을 넘겨 열려 있으면 짝이 깨졌을 가능성이 크므로 알린다.
                WarnIfUpdatesLeftOpen();
            }

            FlushPendingWork();

            if (!_hasLoaded)
            {
                return;
            }

            CheckViewportResize();

            if (_hasPendingAnchor && CanApplyPendingAnchor)
            {
                // 데이터·뷰포트가 준비되기 전에 받은 앵커. 크기 변화를 놓쳤어도(같은 프레임에 0이 됐다가 돌아옴 등) 준비되면 적용한다.
                MoveToPendingAnchor();
                UpdateActiveRange();
            }

            if (_rangeRetryPending)
            {
                // 범위 갱신 콜백이 매번 콘텐츠를 옮겨 다시 맞추기를 멈췄다. 이번 프레임 위치로 다시 맞춘다
                // (ScrollRect 스크롤 이벤트 안이었으면 ScrollRect가 콜백이 옮긴 위치를 직전 위치로 저장하므로 다음 스크롤 이벤트가 오지 않는다).
                UpdateActiveRange();
            }

            float deltaTime = Time.unscaledDeltaTime;

            // 셀 크기 애니메이션을 먼저 진행한다. 트윈 목표는 바뀐 배치에서 다시 계산한다.
            UpdateResizeAnimations(deltaTime);

            if (_tweening)
            {
                UpdateTween(deltaTime);
            }
            else
            {
                ClampVelocity();
                UpdateSnap(deltaTime);
            }

            SetScrolling(_dragging || LinearVelocity != 0f);

            // 이번 프레임의 드래그·관성·트윈이 모두 반영된 뒤 셀 위치를 한 번 알린다.
            NotifyCellPositionsIfChanged();
        }

        #endregion

        #region Public API

        /// <summary>
        /// 델리게이트에서 개수·크기(와 항목 ID)를 다시 받아 처음부터 배치한다. 진행 중인 관성·트윈·점프 정렬은 멈춘다.
        /// 데이터·뷰포트가 준비되기 전에 <see cref="RestoreAnchor"/>로 보관한 앵커가 있으면 처음 대신 그 자리로 간다.
        /// 델리게이트·셀 이벤트 콜백 안에서 불러도 된다 (범위 갱신이 끝난 뒤 처리). 증분 변경 배치 중이면 <see cref="EndUpdates"/>에서 기록한 연산 대신 처리한다.
        /// </summary>
        /// <remarks>
        /// 데이터가 바뀌어도 보던 항목을 유지하려면 <see cref="ReloadDataKeepingPosition"/>이나 <see cref="ReloadData(ReloadAnchor, float)"/>를,
        /// 바뀐 자리를 알면 <see cref="InsertCells"/>·<see cref="RemoveCells"/>·<see cref="MoveCell"/>을, 내용만 바뀐 항목은 <see cref="RefreshCells"/>·<see cref="ReloadCellView"/>를 쓴다.
        /// 처음부터 다시 그리는 리로드라 <see cref="PreserveCellsById"/>와 무관하게 모든 셀을 다시 바인딩한다 (셀 프리팹을 바꾼 뒤 <see cref="ClearActive"/>와 함께 쓰는 경로다).
        /// </remarks>
        public void ReloadData()
        {
            RequestReload(ReloadAnchor.Factor, 0f, false);
        }

        /// <summary>
        /// 델리게이트에서 개수·크기(와 항목 ID)를 다시 받아 처음부터 배치하고 비율 위치로 간다. 진행 중인 관성·트윈·점프 정렬은 멈춘다.
        /// 위치를 정해 부르므로 보관한 앵커(<see cref="RestoreAnchor"/>)는 버린다. <see cref="PreserveCellsById"/>와 무관하게 모든 셀을 다시 바인딩한다.
        /// </summary>
        /// <param name="scrollPositionFactor">
        /// 로드 후 위치. 0 = 처음, 1 = 끝. 루프 모드에서는 한 사이클 안의 비율이다.
        /// 데이터가 바뀌어도 보던 셀을 유지하려면 <see cref="ReloadDataKeepingPosition"/>을 쓴다.
        /// </param>
        public void ReloadData(float scrollPositionFactor)
        {
            _hasPendingAnchor = false;
            RequestReload(ReloadAnchor.Factor, scrollPositionFactor, false);
        }

        /// <summary>
        /// 델리게이트에서 다시 읽고 anchor가 정한 위치로 간다. 진행 중인 관성·트윈·점프 정렬은 멈춘다.
        /// 델리게이트·셀 이벤트 콜백 안에서 부르면 위치 기준까지 보관했다가 범위 갱신이 끝난 뒤, 증분 변경 배치 중이면 <see cref="EndUpdates"/>에서 처리한다.
        /// </summary>
        /// <param name="anchor">
        /// Factor·Start·End는 위치를 정하므로 보관한 앵커(<see cref="RestoreAnchor"/>)를 버리고, 모든 셀을 다시 바인딩한다.
        /// FirstVisible·LastVisible은 다시 읽기 직전에 <see cref="CaptureAnchor"/>(false·true)로 적은 앵커를 다시 읽은 뒤 복원한다
        /// (보관한 앵커가 있으면 그쪽이 가려던 자리이므로 그 앵커를 복원한다). 드래그 중 가장자리 너머로 당기고 있었으면 그 거리는 남긴다.
        /// <see cref="PreserveCellsById"/>를 켜면 이 둘은 항목 ID가 같은 활성 셀을 다시 바인딩하지 않는다.
        /// </param>
        /// <param name="scrollPositionFactor">Factor일 때 위치 (0 = 처음, 1 = 끝). 다른 값에서는 쓰지 않는다.</param>
        public void ReloadData(ReloadAnchor anchor, float scrollPositionFactor = 0f)
        {
            bool keepsPosition = anchor == ReloadAnchor.FirstVisible || anchor == ReloadAnchor.LastVisible;
            if (!keepsPosition)
            {
                _hasPendingAnchor = false;
            }

            RequestReload(anchor, scrollPositionFactor, keepsPosition);
        }

        /// <summary>
        /// 델리게이트에서 다시 읽은 뒤 anchor를 복원한다 (<see cref="RestoreAnchor"/>와 같은 규칙: 항목 ID가 있으면 ID로 찾고, 스크롤 범위 안으로 자른다).
        /// 다시 읽은 데이터가 0개이거나 뷰포트 길이가 0이면 앵커를 보관했다가 준비되면 적용한다.
        /// <see cref="PreserveCellsById"/>를 켜면 항목 ID가 같은 활성 셀을 다시 바인딩하지 않는다.
        /// </summary>
        public void ReloadData(in CyScrollerAnchor anchor)
        {
            _pendingAnchor = anchor;
            _hasPendingAnchor = true;
            RequestReload(ReloadAnchor.Factor, 0f, true);
        }

        /// <summary>
        /// 개수·크기를 다시 받되, 뷰포트 맨 앞에 걸친 항목과 그 셀 안의 오프셋을 유지한다. 델리게이트 콜백 안에서 불러도 된다 (끝난 뒤 처리).
        /// </summary>
        /// <remarks>
        /// <para>델리게이트가 <see cref="ICyScrollerItemIdProvider"/>를 구현하면 인덱스 대신 ID로 같은 항목을 찾는다. 앞쪽에 항목이 삽입·삭제돼도 같은 항목이 맨 위에 남는다
        /// (그 항목이 지워졌으면 같은 인덱스). ID가 없으면 같은 데이터 인덱스를 유지하므로 셀 크기가 바뀌었거나 뒤쪽에 항목이 추가됐을 때 쓴다.</para>
        /// <para><see cref="ReloadData(ReloadAnchor, float)"/>와 달리 셀만 다시 배치하는 재배치라 진행 중인 트윈은 같은 항목(개수가 줄었으면 잘린 인덱스)을 향해 이어 가고,
        /// 점프·스냅 정렬은 ID가 있으면 같은 항목에 맞춘 채 유지한다 (ID가 없으면 정렬을 풀고 맨 앞 셀 기준으로 둔다).</para>
        /// <para>모든 항목의 크기를 다시 받고 셀을 다시 바인딩한다(<see cref="PreserveCellsById"/>를 켜면 항목 ID가 같은 활성 셀은 다시 바인딩하지 않고 새 인덱스로 옮긴다).
        /// 바뀐 자리를 알면 <see cref="InsertCells"/>·<see cref="RemoveCells"/>·<see cref="MoveCell"/>이 그 자리만 반영한다.
        /// 증분 변경 배치 중에 부르면 <see cref="EndUpdates"/>에서 기록한 연산 대신 처리한다 (위치 기준은 배치 전 화면).</para>
        /// </remarks>
        public void ReloadDataKeepingPosition()
        {
            if (!_hasLoaded || _reloadPending)
            {
                // 아직 로드 전이거나 델리게이트가 바뀌었으면 위치 유지보다 새 데이터 로드가 먼저다.
                RequestReload(
                    _reloadPending ? _pendingReloadAnchor : ReloadAnchor.Factor,
                    _reloadPending ? _pendingReloadFactor : 0f,
                    _reloadPending && _pendingReloadPreservesCells);
                return;
            }

            RelayoutKeepingPosition(true, false);
        }

        /// <summary>
        /// 활성 셀마다 <see cref="CyScrollerCellView.RefreshCellView()"/>를 호출한다. 크기는 다시 계산하지 않는다.
        /// 기본 구현은 비어 있으므로, 셀 뷰가 이를 재정의해 자기 데이터로 다시 그려야 한다.
        /// </summary>
        /// <remarks>
        /// 증분 변경 배치 중에 불러도 바로(배치 전 상태의 셀에) 호출한다. 바뀐 항목만 알리거나 배치 끝에 맞춰 부르려면
        /// <see cref="RefreshCells"/>·<see cref="RefreshActiveCellViews(int)"/>를 쓴다.
        /// </remarks>
        public void RefreshActiveCellViews()
        {
            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                if (cell != null)
                {
                    cell.RefreshCellView();
                }
            }
        }

        /// <summary>
        /// 델리게이트의 <see cref="ICyScrollerDelegate.GetCellView"/> 안에서 호출한다.
        /// 같은 <see cref="CyScrollerCellView.CellIdentifier"/>의 재활용 뷰가 있으면 꺼내고, 없으면 프리팹을 Instantiate한다.
        /// </summary>
        public CyScrollerCellView GetCellView(CyScrollerCellView cellPrefab)
        {
            if (cellPrefab == null)
            {
                Debug.LogError("[CyScroller] GetCellView에 null 프리팹이 들어왔습니다.");
                return null;
            }

            if (!EnsureInitialized())
            {
                return null;
            }

            string identifier = cellPrefab.CellIdentifier;
            if (string.IsNullOrEmpty(identifier))
            {
                identifier = string.Empty;
                if (!_warnedEmptyIdentifier)
                {
                    _warnedEmptyIdentifier = true;
                    Debug.LogWarning("[CyScroller] CellIdentifier가 빈 셀 프리팹이 있습니다. 종류가 다른 프리팹이 같은 풀을 공유하게 됩니다.", cellPrefab);
                }
            }

            CyScrollerCellView cell = PopRecycled(identifier);
            if (cell != null)
            {
                AssignPendingIndices(cell);
                CellViewReused?.Invoke(this, cell);
                return cell;
            }

            cell = Instantiate(cellPrefab, _content, false);
            cell.CellIdentifier = identifier;
            AssignPendingIndices(cell);
            CellViewInstantiated?.Invoke(this, cell);
            return cell;
        }

        /// <summary>활성 범위에 있는 해당 데이터의 셀 뷰. 루프에서 사본이 여럿이면 앞쪽 것. 없으면 null.</summary>
        public CyScrollerCellView GetCellViewAtDataIndex(int dataIndex)
        {
            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                if (cell != null && cell.DataIndex == dataIndex)
                {
                    return cell;
                }
            }

            return null;
        }

        /// <summary>활성 범위에 있는 해당 슬롯의 셀 뷰. 없으면 null.</summary>
        public CyScrollerCellView GetCellViewAtCellIndex(int cellIndex)
        {
            if (!HasActiveRange || cellIndex < _activeFirst || cellIndex > _activeLast)
            {
                return null;
            }

            return _activeCells[cellIndex - _activeFirst];
        }

        /// <summary>해당 슬롯의 시작 위치 (콘텐츠 좌표).</summary>
        public float GetScrollPositionForCellViewIndex(int cellIndex)
        {
            if (_layout.SlotCount == 0)
            {
                return 0f;
            }

            return _layout.GetSlotStart(Mathf.Clamp(cellIndex, 0, _layout.SlotCount - 1));
        }

        /// <summary>해당 데이터 셀의 시작 위치. 루프 모드에서는 가운데 세트 사본 기준. <see cref="GetCellStart"/>와 같다.</summary>
        public float GetScrollPositionForDataIndex(int dataIndex) => GetCellStart(dataIndex);

        /// <summary>위치(콘텐츠 좌표)에 있는 슬롯. 시작 위치가 position 이하인 마지막 슬롯. 비었으면 -1.</summary>
        public int GetCellViewIndexAtPosition(float position) => _layout.GetSlotAtPosition(position);

        /// <summary>슬롯 번호를 데이터 인덱스로 바꾼다.</summary>
        public int GetDataIndexForCellViewIndex(int cellIndex) => _layout.SlotToDataIndex(cellIndex);

        /// <summary>
        /// 활성 셀을 파괴한다. 셀 프리팹을 바꾼 뒤 <see cref="ReloadData()"/>와 함께 쓴다.
        /// 보이던 셀은 파괴 전에 <see cref="CellViewDidEndDisplay"/>를 받는다. 파괴할 셀은 바인딩을 푼다(<see cref="CyScrollerCellView.IsBound"/> false).
        /// 델리게이트·이벤트 콜백 안에서 부르면 범위 갱신이 끝난 뒤, 증분 변경 배치 중이면 <see cref="EndUpdates"/>에서 처리한다.
        /// </summary>
        public void ClearActive()
        {
            if (_inRangeUpdate || _updateDepth > 0)
            {
                _clearActivePending = true;
                return;
            }

            ClearActiveNow();
        }

        private void ClearActiveNow()
        {
            EndDisplayAll();

            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                if (cell != null)
                {
                    UnbindCell(cell);
                    Destroy(cell.gameObject);
                }
            }

            _activeCells.Clear();
            _activeFirst = 0;
            _activeLast = -1;
        }

        /// <summary>
        /// 재활용 풀의 셀을 모두 파괴한다. 다시는 쓰지 않을 프리팹 종류를 정리할 때 쓴다.
        /// 델리게이트·이벤트 콜백 안에서 부르면 범위 갱신이 끝난 뒤, 증분 변경 배치 중이면 <see cref="EndUpdates"/>에서 처리한다.
        /// </summary>
        public void ClearRecycled()
        {
            if (_inRangeUpdate || _updateDepth > 0)
            {
                _clearRecycledPending = true;
                return;
            }

            ClearRecycledNow();
        }

        private void ClearRecycledNow()
        {
            foreach (KeyValuePair<string, List<CyScrollerCellView>> pair in _pools)
            {
                List<CyScrollerCellView> pool = pair.Value;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i] != null)
                    {
                        Destroy(pool[i].gameObject);
                    }
                }

                pool.Clear();
            }
        }

        /// <summary><see cref="ClearActive"/> + <see cref="ClearRecycled"/>.</summary>
        public void ClearAll()
        {
            ClearActive();
            ClearRecycled();
        }

        /// <summary>재활용 풀에 있는 셀 수.</summary>
        public int GetRecycledCellCount()
        {
            int count = 0;
            foreach (KeyValuePair<string, List<CyScrollerCellView>> pair in _pools)
            {
                count += pair.Value.Count;
            }

            return count;
        }

        #endregion

        #region Initialization

        private bool EnsureInitialized(bool logErrors = true)
        {
            if (_initialized)
            {
                return true;
            }

            if (!TryGetComponent(out _scrollRect))
            {
                if (logErrors)
                {
                    Debug.LogError("[CyScroller] 같은 GameObject에 ScrollRect가 없습니다.", this);
                }

                return false;
            }

            _content = _scrollRect.content;
            if (_content == null)
            {
                if (logErrors)
                {
                    Debug.LogError("[CyScroller] ScrollRect.content가 비어 있습니다.", this);
                }

                return false;
            }

            _viewport = _scrollRect.viewport != null ? _scrollRect.viewport : _scrollRect.transform as RectTransform;
            _onScrollRectValueChanged = OnScrollRectValueChanged;

            DisableConflictingLayout();
            ConfigureScrollRect();
            _initialized = true;

            // OnEnable이 초기화 전에 지나갔으면 여기서 리스너를 붙인다.
            if (isActiveAndEnabled)
            {
                StartListening();
            }

            return true;
        }

        private void StartListening()
        {
            if (_listening)
            {
                return;
            }

            _scrollRect.onValueChanged.AddListener(_onScrollRectValueChanged);
            _listening = true;
        }

        private void StopListening()
        {
            if (!_listening)
            {
                return;
            }

            if (_scrollRect != null)
            {
                _scrollRect.onValueChanged.RemoveListener(_onScrollRectValueChanged);
            }

            _listening = false;
        }

        private void DisableConflictingLayout()
        {
            if (_content.TryGetComponent(out LayoutGroup layoutGroup) && layoutGroup.enabled)
            {
                layoutGroup.enabled = false;
                Debug.LogWarning("[CyScroller] content의 LayoutGroup을 비활성화했습니다. CyScroller가 셀을 직접 배치합니다.", layoutGroup);
            }

            if (_content.TryGetComponent(out ContentSizeFitter fitter) && fitter.enabled)
            {
                fitter.enabled = false;
                Debug.LogWarning("[CyScroller] content의 ContentSizeFitter를 비활성화했습니다. CyScroller가 콘텐츠 크기를 정합니다.", fitter);
            }
        }

        private void ConfigureScrollRect()
        {
            bool vertical = IsVertical;
            _scrollRect.vertical = vertical;
            _scrollRect.horizontal = !vertical;

            if (vertical)
            {
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.sizeDelta = new Vector2(0f, _content.sizeDelta.y);
            }
            else
            {
                _content.anchorMin = new Vector2(0f, 0f);
                _content.anchorMax = new Vector2(0f, 1f);
                _content.pivot = new Vector2(0f, 0.5f);
                _content.sizeDelta = new Vector2(_content.sizeDelta.x, 0f);
            }

            _content.anchoredPosition = Vector2.zero;
            _appliedVertical = vertical;
        }

        #endregion

        #region Deferred Work

        /// <summary>콜백 안에서 미뤄 둔 정리·리로드·재배치가 남아 있는지.</summary>
        private bool HasPendingWork => _reloadPending || _relayoutPending || _clearActivePending || _clearRecycledPending;

        /// <summary>
        /// 콜백 안에서 미뤄 둔 정리·리로드·재배치를 처리한다. 리로드나 재배치를 했으면 범위도 이미 갱신됐으므로 true.
        /// 증분 변경 배치 중에는 처리하지 않는다 (<see cref="EndUpdates"/>가 처리한다).
        /// </summary>
        private bool FlushPendingWork()
        {
            if (_inRangeUpdate || !_initialized || _updateDepth > 0)
            {
                return false;
            }

            ApplyPendingClears();

            if (_reloadPending)
            {
                ReloadNow(_pendingReloadAnchor, _pendingReloadFactor, _pendingReloadPreservesCells);
                return _hasLoaded;
            }

            if (_relayoutPending && _hasLoaded)
            {
                bool requery = _relayoutRequery;
                bool reconfigure = _relayoutReconfigure;
                _relayoutPending = false;
                _relayoutRequery = false;
                _relayoutReconfigure = false;
                RelayoutKeepingPosition(requery, reconfigure);
                return true;
            }

            return false;
        }

        private void ApplyPendingClears()
        {
            if (_clearActivePending)
            {
                _clearActivePending = false;
                ClearActiveNow();
            }

            if (_clearRecycledPending)
            {
                _clearRecycledPending = false;
                ClearRecycledNow();
            }
        }

        #endregion

        #region Reload

        /// <summary>
        /// 리로드 요청 공통 경로. 범위 갱신(델리게이트·이벤트 콜백) 안이면 위치 기준·키 유지 여부까지 보관했다가 끝난 뒤,
        /// 증분 변경 배치 중이면 <see cref="EndUpdates"/>에서 처리한다. 보관한 앵커를 버릴지는 호출자가 정한다 (위치를 정한 요청만 버린다).
        /// </summary>
        /// <param name="preserveCells">위치를 지키는 리로드라 <see cref="PreserveCellsById"/>를 따를지. 처리할 때 옵션·ID 제공자를 다시 본다.</param>
        private void RequestReload(ReloadAnchor anchor, float factor, bool preserveCells)
        {
            if (!EnsureInitialized())
            {
                return;
            }

            if (_inRangeUpdate || _updateDepth > 0)
            {
                _reloadPending = true;
                _pendingReloadAnchor = anchor;
                _pendingReloadFactor = factor;
                _pendingReloadPreservesCells = preserveCells;
                return;
            }

            ReloadNow(anchor, factor, preserveCells);
        }

        /// <summary>
        /// 리로드 본체. 다시 읽은 뒤 위치는 <see cref="MoveAfterReload"/>가 정한다.
        /// 키 유지 리로드(<see cref="PreserveCellsById"/>)면 셀을 먼저 회수하지 않는 <see cref="ReloadPreservingCells"/>로 처리한다.
        /// </summary>
        private void ReloadNow(ReloadAnchor anchor, float factor, bool preserveCells)
        {
            // 범위 갱신 콜백 안에서 끝난 스냅이 아직 알려지지 않았으면 배치를 다시 만들기 전에 알린다.
            RaisePendingSnapEvent();

            _reloadPending = false;
            _relayoutPending = false;
            _relayoutRequery = false;
            _relayoutReconfigure = false;

            // 키 유지 여부는 알림 핸들러 뒤에 정한다 (핸들러가 내용 갱신을 알려 모두 다시 바인딩해야 할 수 있다).
            bool preserve = preserveCells && CanPreserveCells;
            _forceRebindOnReload = false;
            if (preserve)
            {
                ReloadPreservingCells(anchor, factor);
                return;
            }

            ApplyPendingClears();

            CancelTween();
            _snapArmed = false;
            _alignmentActive = false;
            _scrollRect.StopMovement();

            RecycleAllActive();

            // 회수 콜백이 위치를 바꿨어도 바뀐 화면을 기준으로 삼도록 셀부터 회수한 뒤 읽는다.
            // 보관한 앵커가 있으면 그쪽이 가려던 자리이므로 지금 화면 대신 그 앵커를 쓴다.
            // 다시 읽다 멈춘 배치(키 유지 리로드·재배치의 복구)에서는 화면 항목을 읽을 수 없다. 보관한 앵커(다시 읽기 전 화면)를
            // 이동 요청(배치 안의 ScrollPosition 대입, 회수 콜백 등)이 버렸으면 그 요청이 옮긴 콘텐츠 위치를 지킨다.
            bool keepVisible = !_hasPendingAnchor && (anchor == ReloadAnchor.FirstVisible || anchor == ReloadAnchor.LastVisible);
            bool keepPosition = keepVisible && _rebuildFailed;
            keepVisible &= !keepPosition;
            float position = ReadPosition(_appliedVertical);
            float overscroll = 0f;
            CyScrollerAnchor visible = keepVisible ? CaptureLayoutAnchor(anchor == ReloadAnchor.LastVisible, out overscroll) : default;

            RebuildLayout(true);
            _hasLoaded = true;
            _lastViewportExtent = ScrollRectSize;
            if (keepPosition)
            {
                MoveContentTo(Mathf.Clamp(position, 0f, ScrollSize));
            }
            else
            {
                MoveAfterReload(anchor, factor, keepVisible, in visible, overscroll);
            }

            UpdateActiveRange();
            ApplyScrollbarVisibility();
        }

        /// <summary>
        /// 다시 읽은 뒤 위치: 보관한 앵커(적용할 수 있으면) → FirstVisible·LastVisible이면 다시 읽기 전 화면의 앵커(visible) →
        /// End면 끝 → 나머지는 비율(Start는 0) 순으로 정한다. 사용자 코드를 부르지 않는다.
        /// </summary>
        private void MoveAfterReload(ReloadAnchor anchor, float factor, bool keepVisible, in CyScrollerAnchor visible, float overscroll)
        {
            if (_hasPendingAnchor && CanApplyPendingAnchor)
            {
                MoveToPendingAnchor();
            }
            else if (keepVisible)
            {
                RestoreAnchorPosition(in visible, overscroll);
            }
            else if (anchor == ReloadAnchor.End)
            {
                MoveContentTo(Mathf.Clamp(GetEndPosition(), 0f, ScrollSize));
            }
            else
            {
                MoveContentTo(GetPositionForFactor(anchor == ReloadAnchor.Start ? 0f : factor));
            }
        }

        #endregion

        #region Layout

        private void RebuildLayout(bool requeryDelegate)
        {
            if (requeryDelegate)
            {
                // 다시 읽은 크기가 최종 값이다. 진행 중인 크기 애니메이션은 버린다 (다시 읽다 예외로 멈춰도 남지 않게 먼저).
                ClearResizeAnimations();

                int count = 0;
                if (_delegate != null)
                {
                    count = Mathf.Max(0, _delegate.GetNumberOfCells(this));
                }
                else
                {
                    Debug.LogWarning("[CyScroller] Delegate가 없어 빈 목록으로 로드합니다.", this);
                }

                _layout.SetDataCount(count);
                for (int i = 0; i < count; i++)
                {
                    _layout.SetSize(i, _delegate.GetCellViewSize(this, i));
                }

                RebuildItemIds(count);

                // 끝까지 다시 읽었다. 다시 읽다 멈춰 반쯤 읽힌 배치였어도 이제 맞다 (아래 Build는 사용자 코드를 부르지 않는다).
                _rebuildFailed = false;
            }

            GetMainAxisPadding(out float paddingBefore, out float paddingAfter);
            _layout.Build(_spacing, paddingBefore, paddingAfter, _loop, ScrollRectSize, _lookAheadBefore, _lookAheadAfter);
            _cellPositionsDirty = true;

            _content.SetSizeWithCurrentAnchors(
                IsVertical ? RectTransform.Axis.Vertical : RectTransform.Axis.Horizontal,
                _layout.ContentExtent);
        }

        private void GetMainAxisPadding(out float before, out float after)
        {
            if (IsVertical)
            {
                before = _padding.top;
                after = _padding.bottom;
            }
            else
            {
                before = _padding.left;
                after = _padding.right;
            }
        }

        /// <summary>
        /// 배치를 다시 계산하되 보던 위치를 유지한다. 트윈 중이면 맨 앞 셀을 유지한 채 트윈을 이어 가고,
        /// 점프·스냅 정렬이 살아 있으면 그 정렬을, 아니면 맨 앞 셀과 그 안의 오프셋을 유지한다.
        /// </summary>
        private void RelayoutKeepingPosition(bool requeryDelegate, bool reconfigure)
        {
            if (!_initialized)
            {
                return;
            }

            if (!_hasLoaded)
            {
                if (reconfigure)
                {
                    ConfigureScrollRect();
                }

                return;
            }

            if (_inRangeUpdate || _updateDepth > 0)
            {
                _relayoutPending = true;
                _relayoutRequery |= requeryDelegate;
                _relayoutReconfigure |= reconfigure;
                return;
            }

            if (_reloadPending)
            {
                // 델리게이트가 바뀌었으면 위치 유지보다 새 데이터 로드가 먼저다.
                ReloadNow(_pendingReloadAnchor, _pendingReloadFactor, _pendingReloadPreservesCells);
                return;
            }

            using (_relayoutMarker.Auto())
            {
                ApplyRelayout(requeryDelegate, reconfigure);
            }
        }

        /// <summary>
        /// <see cref="RelayoutKeepingPosition"/>의 본체. 위치 복원(앵커·정렬)은 <see cref="MoveContentTo"/>를 거쳐 드래그 기준점까지 맞춘다.
        /// </summary>
        /// <remarks>
        /// <para>진행 중인 트윈(점프·스냅·ScrollIntoView)은 끊지 않는다. 화면은 맨 앞 셀 기준으로 그대로 두고, 같은 데이터(개수가 줄었으면 잘린 인덱스)를
        /// 향해 남은 시간 동안 계속 간다. 목표는 새 배치에서 다시 계산하고, 트윈 시작점은 다음 프레임에 화면이 튀지 않게 다시 잡는다(<see cref="RebaseTween"/>).
        /// 항목 ID가 있으면 맨 앞 셀과 트윈 목표·정렬 대상을 ID로 다시 찾으므로 앞쪽에 항목이 삽입·삭제돼도 같은 항목을 지킨다.</para>
        /// <para>델리게이트를 다시 읽고 축은 그대로인 재배치(<see cref="ReloadDataKeepingPosition"/>)는 <see cref="PreserveCellsById"/>를 따른다:
        /// 셀을 먼저 회수하지 않고 다시 읽은 뒤 ID가 같은 활성 셀을 새 인덱스로 옮긴다(<see cref="ReconcilePreservedCells"/>). 다른 재배치는 모든 셀을 다시 바인딩한다.
        /// 키 유지 리로드(<see cref="ReloadPreservingCells"/>)와 같이, 셀을 맞추기 전에 사용자 코드(다시 읽는 중의 델리게이트)가 부른 <see cref="RefreshCells"/>는
        /// 새 인덱스로 보고 맞춘 뒤 남은 셀에 부르고, 다시 읽거나 맞추는 도중 사용자 코드 예외로 멈추면 모두 다시 바인딩하는 앵커 보존 리로드를 미뤄 다음 갱신에 맞춘다
        /// (다시 읽다 멈췄으면 반쯤 읽은 배치 대신 다시 읽기 전 화면으로 간다). 셀을 맞추기 전에 닫은 배치의 미룬 작업은 맞춘 뒤 범위 갱신에서 처리한다.</para>
        /// </remarks>
        private void ApplyRelayout(bool requeryDelegate, bool reconfigure)
        {
            // 범위 갱신 콜백 안에서 끝난 스냅이 아직 알려지지 않았으면 슬롯 번호가 바뀌기 전에 알린다.
            RaisePendingSnapEvent();

            // 키 유지 여부는 알림 핸들러 뒤에 정한다 (핸들러가 내용 갱신을 알려 모두 다시 바인딩해야 할 수 있다).
            bool preserve = requeryDelegate && !reconfigure && CanPreserveCells;
            _forceRebindOnReload = false;

            if (!preserve)
            {
                // 회수 콜백(셀 이벤트·OnRecycled)이 트윈·정렬·위치를 바꿨어도 바뀐 상태로 처리하도록 셀부터 회수한 뒤 상태를 읽는다.
                RecycleAllActive();
            }

            // 트윈이 아닌 정렬 유지는 데이터를 다시 받지 않을 때만 지킨다 (다시 받으면 같은 인덱스가 다른 항목일 수 있다).
            // 항목 ID가 있으면 다시 받아도 ID로 같은 항목을 찾을 수 있으므로 지킨다.
            bool tweening = _tweening;
            int alignDataIndex = _alignmentActive ? _layout.SlotToDataIndex(_align.Slot) : -1;
            bool alignById = requeryDelegate && _hasItemIds && alignDataIndex >= 0;
            long alignItemId = alignById ? _itemIds[alignDataIndex] : 0L;
            bool keepAlignment = _alignmentActive && (tweening || !requeryDelegate || alignById);

            float previousPosition = ReadPosition(_appliedVertical);
            CyScrollerAnchor anchor = CaptureLayoutAnchor(false, out float anchorOverscroll);
            if (preserve)
            {
                RecordPreservedCellOffsets();
            }

            // 루프 트윈이 맨 앞 셀보다 몇 사이클 앞뒤 사본으로 가던 중인지. 새 배치에서도 같은 방향 사본으로 가게 한다.
            int alignSetOffset = 0;
            if (tweening && _layout.IsLoop)
            {
                int dataCount = _layout.DataCount;
                alignSetOffset = _align.Slot / dataCount - _layout.GetSlotAtPosition(previousPosition) / dataCount;
            }

            if (reconfigure)
            {
                ConfigureScrollRect();
            }

            // 키 유지면 셀을 새 인덱스로 맞출 때까지 사용자 코드(다시 읽는 중의 델리게이트)가 부른 RefreshCells를 미뤘다가 맞춘 뒤 남은 셀에 부른다
            // (활성 셀이 아직 옛 인덱스라 바로 부르면 다른 항목의 셀이 받는다). ReloadPreservingCells와 같다.
            bool tweenEmptied = false;
            Action emptiedComplete = null;
            bool rebuilt = false;
            bool completed = false;
            if (preserve)
            {
                _deferRefreshes = true;
            }

            try
            {
                RebuildLayout(requeryDelegate);
                rebuilt = true;

                if (alignById)
                {
                    // 같은 자리에 같은 항목이 남아 있으면 그 자리 (같은 ID가 여럿이어도 데이터가 그대로면 정렬 대상이 옮겨 가지 않는다).
                    int found = ResolveItemIdIndex(alignItemId, alignDataIndex);
                    if (found >= 0)
                    {
                        alignDataIndex = found;
                    }
                    else if (!tweening)
                    {
                        // 정렬하던 항목이 지워졌다. 정렬을 풀고 맨 앞 셀 기준으로 둔다 (트윈은 잘린 인덱스로 계속 간다).
                        keepAlignment = false;
                    }
                }

                // 모든 경로가 MoveContentTo로 옮긴다 (드래그 중이면 기준점·직전 위치까지 맞춤).
                if (tweening)
                {
                    int anchorSlot = RestoreAnchorPosition(in anchor, anchorOverscroll);
                    if (_layout.DataCount > 0)
                    {
                        _align.Slot = RemapAlignSlot(alignDataIndex, anchorSlot, alignSetOffset);
                        RebaseTween(ReadPosition(_appliedVertical) - previousPosition);
                    }
                    else
                    {
                        // 갈 셀이 없어졌다. 빈 목록 점프처럼 그 자리에서 끝내고 완료 콜백을 부른다.
                        tweenEmptied = true;
                        emptiedComplete = _tweenComplete;
                        ClearTweenState();
                        _alignmentActive = false;
                    }
                }
                else if (keepAlignment && alignDataIndex >= 0 && alignDataIndex < _layout.DataCount)
                {
                    _align.Slot = (_layout.IsLoop ? _layout.MiddleSetFirstSlot : 0) + alignDataIndex;
                    ReapplyAlignment();
                }
                else
                {
                    _alignmentActive = false;
                    if (_hasPendingAnchor && CanApplyPendingAnchor)
                    {
                        // 데이터·뷰포트가 준비되기 전에 받은 앵커가 이 재배치로 적용할 수 있게 됐다 (데이터가 생김, 축이 바뀌어 뷰포트 길이가 생김 등).
                        MoveToPendingAnchor();
                    }
                    else
                    {
                        RestoreAnchorPosition(in anchor, anchorOverscroll);
                    }
                }

                _lastViewportExtent = ScrollRectSize;
                if (preserve)
                {
                    // 새 배치·위치에서 ID가 같은 활성 셀을 새 인덱스로 옮기고, 미룬 갱신을 남은 셀에 부른다.
                    ReconcilePreservedCells();
                }

                completed = true;
            }
            finally
            {
                if (preserve)
                {
                    _deferRefreshes = false;
                    _deferredRefreshes.Clear();
                    if (!completed)
                    {
                        // 다시 읽거나 셀을 맞추는 도중 사용자 코드 예외로 멈췄다. 활성 셀이 옛 배치에 남을 수 있으므로 모두 다시 바인딩하는 리로드가 다음 갱신에 맞춘다.
                        // 다시 읽다 멈췄으면 그 리로드는 반쯤 읽은 배치 대신 다시 읽기 전 화면(anchor)으로 간다.
                        ScheduleRecoveryReload(!rebuilt, in anchor);
                    }
                }
            }

            UpdateActiveRange();
            ApplyScrollbarVisibility();

            if (tweenEmptied)
            {
                CompleteTween(emptiedComplete, false, -1, true);
            }
        }

        /// <summary>정규화 위치 → 스크롤 위치. 루프면 가운데 세트 기준 ±반 사이클 창 안의 같은 배치.</summary>
        private float GetPositionForFactor(float factor)
        {
            if (_layout.IsLoop)
            {
                float wrapped = factor - Mathf.Round(factor);
                return Mathf.Clamp(_layout.MiddleSetStart + wrapped * _layout.CycleExtent, 0f, ScrollSize);
            }

            return Mathf.Clamp01(factor) * ScrollSize;
        }

        private void CheckViewportResize()
        {
            float viewportExtent = ScrollRectSize;
            if (Mathf.Approximately(viewportExtent, _lastViewportExtent))
            {
                return;
            }

            _lastViewportExtent = viewportExtent;

            if (IsLoopSetCountStale())
            {
                RelayoutKeepingPosition(false, false);
                return;
            }

            // 뷰포트 길이가 0이라 보관해 둔 앵커가 있으면 이제 적용한다 (보관 중에는 트윈·정렬이 없다).
            // 점프·스냅으로 맞춘 상태면 새 뷰포트 크기로 정렬을 다시 계산한다 (예: 첫 프레임 Canvas 크기 확정, 화면 회전).
            // 트윈 중이면 목표는 매 프레임 다시 계산하므로, 다음 프레임에 화면이 튀지 않게 시작점만 다시 잡는다.
            // 정렬이 없으면 콘텐츠 시작 기준 위치를 그대로 둔다.
            if (_hasPendingAnchor && CanApplyPendingAnchor)
            {
                MoveToPendingAnchor();
            }
            else if (_tweening)
            {
                RebaseTween(0f);
            }
            else if (_alignmentActive)
            {
                ReapplyAlignment();
            }

            UpdateActiveRange();
            ApplyScrollbarVisibility();
        }

        private bool IsLoopSetCountStale()
        {
            if (!_layout.IsLoop)
            {
                return false;
            }

            int setCount = CyScrollerLayout.ComputeLoopSetCount(
                _layout.CycleExtent, ScrollRectSize, _layout.Spacing, _lookAheadBefore, _lookAheadAfter);
            return setCount != _layout.SetCount;
        }

        private void OnCoverageChanged()
        {
            if (IsLoopSetCountStale())
            {
                RelayoutKeepingPosition(false, false);
                return;
            }

            UpdateActiveRange();
        }

        private float ReadPosition(bool vertical)
        {
            Vector2 anchored = _content.anchoredPosition;
            return vertical ? anchored.y : -anchored.x;
        }

        /// <summary>content 축 위치만 쓴다. 스크롤러 코드는 드래그 기준까지 맞추는 <see cref="MoveContentTo"/>를 쓴다.</summary>
        private void SetScrollPositionInternal(float position)
        {
            Vector2 anchored = _content.anchoredPosition;
            if (_appliedVertical)
            {
                anchored.y = position;
            }
            else
            {
                anchored.x = -position;
            }

            _content.anchoredPosition = anchored;
        }

        /// <summary>
        /// 스크롤 축 스크롤바 표시. Never·루프에서는 ScrollRect에서 떼어 낸다
        /// (연결돼 있으면 ScrollRect가 LateUpdate마다 다시 켜고, 루프 콘텐츠 전체를 스크롤바로 끌게 된다).
        /// </summary>
        private void ApplyScrollbarVisibility()
        {
            if (!_initialized)
            {
                return;
            }

            bool vertical = IsVertical;
            ScrollbarVisibility visibility = _loop ? ScrollbarVisibility.Never : _scrollbarVisibility;

            if (_detachedScrollbar != null && (visibility != ScrollbarVisibility.Never || _detachedScrollbarVertical != vertical))
            {
                ReattachScrollbar();
            }

            Scrollbar scrollbar = vertical ? _scrollRect.verticalScrollbar : _scrollRect.horizontalScrollbar;
            if (scrollbar == null)
            {
                return;
            }

            if (visibility == ScrollbarVisibility.Never)
            {
                _detachedScrollbar = scrollbar;
                _detachedScrollbarVertical = vertical;
                if (vertical)
                {
                    _scrollRect.verticalScrollbar = null;
                }
                else
                {
                    _scrollRect.horizontalScrollbar = null;
                }

                scrollbar.gameObject.SetActive(false);
                return;
            }

            ScrollRect.ScrollbarVisibility current = vertical
                ? _scrollRect.verticalScrollbarVisibility
                : _scrollRect.horizontalScrollbarVisibility;

            ScrollRect.ScrollbarVisibility next = current;
            if (visibility == ScrollbarVisibility.Always)
            {
                next = ScrollRect.ScrollbarVisibility.Permanent;
            }
            else if (current == ScrollRect.ScrollbarVisibility.Permanent)
            {
                // OnlyIfNeeded. AutoHideAndExpandViewport는 사용자가 고른 것이므로 그대로 둔다.
                next = ScrollRect.ScrollbarVisibility.AutoHide;
            }

            if (next != current)
            {
                if (vertical)
                {
                    _scrollRect.verticalScrollbarVisibility = next;
                }
                else
                {
                    _scrollRect.horizontalScrollbarVisibility = next;
                }
            }

            if (visibility == ScrollbarVisibility.Always && !scrollbar.gameObject.activeSelf)
            {
                scrollbar.gameObject.SetActive(true);
            }
        }

        private void ReattachScrollbar()
        {
            Scrollbar scrollbar = _detachedScrollbar;
            _detachedScrollbar = null;
            if (scrollbar == null)
            {
                return;
            }

            if (_detachedScrollbarVertical)
            {
                if (_scrollRect.verticalScrollbar == null)
                {
                    _scrollRect.verticalScrollbar = scrollbar;
                }
            }
            else if (_scrollRect.horizontalScrollbar == null)
            {
                _scrollRect.horizontalScrollbar = scrollbar;
            }

            // 자동 숨김이면 ScrollRect가 다음 LateUpdate에 필요 여부로 다시 정리한다.
            scrollbar.gameObject.SetActive(true);
        }

        #endregion

        #region Virtualization

        private void OnScrollRectValueChanged(Vector2 normalizedPosition)
        {
            if (!_hasLoaded)
            {
                return;
            }

            _inValueChanged = true;
            try
            {
                // ScrollRect는 뷰포트 크기가 바뀐 프레임에도 이 콜백을 부른다. 탄성 보정 이동을
                // 사용자 이동으로 오인하지 않도록 크기 변화부터 반영한다.
                CheckViewportResize();
                RecenterLoopIfNeeded();
                ReleaseAlignmentIfMoved();
                UpdateActiveRange();
                ScrollerScrolled?.Invoke(this, normalizedPosition, ScrollPosition);
            }
            finally
            {
                _inValueChanged = false;
            }
        }

        private void UpdateActiveRange()
        {
            if (!_hasLoaded || _inRangeUpdate)
            {
                return;
            }

            if (_updateDepth > 0)
            {
                // 증분 변경 배치 중이다. 델리게이트가 바뀌는 중일 수 있으므로 배치가 끝날 때 맞춘다.
                _rangeUpdateDeferred = true;
                return;
            }

            // 델리게이트 교체·콜백 안 요청이 남아 있으면 옛 배치로 새 델리게이트를 부르지 않도록 먼저 처리한다.
            if (FlushPendingWork())
            {
                return;
            }

            if (_rebuildFailed)
            {
                // 다시 읽다 멈춘 배치다(복구 리로드가 아직 다시 읽기 전, 그 리로드의 회수 콜백 안 등). 반쯤 읽은 배치로 범위를 맞추지 않고 다시 읽은 뒤 맞춘다.
                return;
            }

            using (_updateActiveRangeMarker.Auto())
            {
                ApplyActiveRange();
            }
        }

        /// <summary>
        /// 현재 위치에 맞게 활성 슬롯 범위와 표시 범위(실제 뷰포트)를 맞춘다. 표시 끝 → 회수 → 활성화 → 표시 시작 순서다.
        /// 활성 범위가 그대로여도 미리보기 구간 셀이 뷰포트에 드나들면 표시 범위만 바꾼다.
        /// </summary>
        /// <remarks>
        /// 콜백(델리게이트·셀 이벤트·셀 뷰 가상 메서드)이 콘텐츠를 옮기면(즉시 점프·<see cref="ScrollPosition"/> 대입 등) 옛 위치 기준의 남은 작업을 멈추고
        /// 지금 위치에서 다시 맞춘다. 콜백 안에서는 루프 순환 보정(슬롯 번호 이동)을 하지 않고 다시 맞추기 직전에 한다(<see cref="RecenterLoopIfNeeded"/>).
        /// 그래서 범위 계산 도중 슬롯 번호가 바뀌지 않고, 한 번에 활성화하는 셀은 매번 <see cref="MAX_ACTIVE_CELLS"/> 안이다.
        /// 콜백이 매번 다시 옮기면 <see cref="MAX_RANGE_PASSES"/>번에서 멈추고 다음 LateUpdate에 다시 맞춘다.
        /// 콜백 안에서 끝난 스냅은 여기서 범위를 다 맞춘 뒤 알린다(<see cref="RaiseSnapEventAfterRangeUpdate"/>). 할당 없음.
        /// </remarks>
        private void ApplyActiveRange()
        {
            _rangeRetryPending = false;
            for (int pass = 0; pass < MAX_RANGE_PASSES; pass++)
            {
                if (_recenterDeferred)
                {
                    // 콜백 안에서 미룬 순환 보정. 범위 갱신 밖이므로 이제 슬롯 번호를 옮겨도 된다.
                    _recenterDeferred = false;
                    RecenterLoopIfNeeded();
                }

                float position = ScrollPosition;
                ApplyActiveRangeAt(position);

                // 콜백이 콘텐츠를 옮기지 않았고 순환 보정도 미루지 않았으면 끝이다.
                // 미뤄 둔 리로드·재배치가 있으면 그쪽이 새 배치로 범위를 다시 만드므로, 옛 배치로 (새) 델리게이트를 부르지 않게 여기서 멈춘다.
                if ((!HasMovedFrom(position) && !_recenterDeferred) || _reloadPending || _relayoutPending)
                {
                    RaiseSnapEventAfterRangeUpdate();
                    return;
                }
            }

            _rangeRetryPending = true;
            RaiseSnapEventAfterRangeUpdate();
        }

        /// <summary>
        /// 범위 갱신을 마칠 때 콜백 안에서 끝난 스냅을 알린다. 상한·리로드 대기로 멈춰 콜백이 미룬 순환 보정이 남았으면
        /// 먼저 끝내 슬롯 번호를 순환 보정 뒤로 맞춘다 (범위는 다음 갱신이 맞춘다).
        /// </summary>
        private void RaiseSnapEventAfterRangeUpdate()
        {
            if (!_snapEventPending)
            {
                return;
            }

            if (_recenterDeferred)
            {
                _recenterDeferred = false;
                RecenterLoopIfNeeded();
            }

            // 순환 보정이 셀을 모두 회수하며 부른 콜백 안에서 범위를 다시 갱신했으면 이미 알렸다 (RaisePendingSnapEvent가 다시 확인한다).
            RaisePendingSnapEvent();
        }

        /// <summary>범위 갱신을 시작한 위치에서 콘텐츠가 옮겨졌는지 (콜백 안의 즉시 점프·<see cref="ScrollPosition"/> 대입 등).</summary>
        private bool HasMovedFrom(float position) => ScrollPosition != position;

        /// <summary>
        /// position 기준으로 활성 범위와 표시 범위를 한 번 맞춘다. 콜백이 콘텐츠를 옮기면 옛 위치 기준의 남은 단계를 건너뛴다
        /// (호출자가 새 위치로 다시 맞춘다).
        /// </summary>
        private void ApplyActiveRangeAt(float position)
        {
            ComputeActiveRange(position, out int first, out int last, out int visibleFirst, out int visibleLast);

            bool activeChanged = first != _activeFirst || last != _activeLast;
            if (!activeChanged && IsSameRange(visibleFirst, visibleLast, _visibleFirst, _visibleLast))
            {
                return;
            }

            _inRangeUpdate = true;
            try
            {
                // 벗어나는 셀의 표시 끝은 회수보다 먼저, 들어오는 셀의 표시 시작은 활성화한 뒤에 알린다.
                // 각 단계는 콜백이 콘텐츠를 옮기면 false를 돌려주고, 그러면 옛 위치 기준의 남은 단계는 건너뛴다.
                if (!ShrinkVisibleRange(visibleFirst, visibleLast, position))
                {
                    return;
                }

                if (activeChanged)
                {
                    _cellPositionsDirty = true;
                    if (!ApplyActiveSlots(first, last, position))
                    {
                        return;
                    }
                }

                GrowVisibleRange(visibleFirst, visibleLast, position);
            }
            finally
            {
                _inRangeUpdate = false;
            }
        }

        /// <summary>
        /// position에서 활성 슬롯 범위(뷰포트 + 미리보기 구간, <see cref="MAX_ACTIVE_CELLS"/>개까지)와 표시 범위(실제 뷰포트, 활성 범위로 자름). 할당 없음.
        /// </summary>
        private void ComputeActiveRange(float position, out int first, out int last, out int visibleFirst, out int visibleLast)
        {
            float viewportSize = ScrollRectSize;
            _layout.GetSlotRange(
                position - _lookAheadBefore,
                position + viewportSize + _lookAheadAfter,
                out first,
                out last);

            if (last - first + 1 > MAX_ACTIVE_CELLS)
            {
                last = first + MAX_ACTIVE_CELLS - 1;
                if (!_warnedActiveCap)
                {
                    _warnedActiveCap = true;
                    Debug.LogWarning($"[CyScroller] 한 번에 활성화할 셀이 {MAX_ACTIVE_CELLS}개를 넘습니다. 셀 크기가 0에 가깝지 않은지 확인하세요.", this);
                }
            }

            GetVisibleSlotRange(position, viewportSize, first, last, out visibleFirst, out visibleLast);
        }

        /// <summary>
        /// 활성 슬롯 범위를 [first, last]로 맞춘다. 나가는 셀을 먼저 회수하고 들어오는 셀을 받는다.
        /// 콜백이 콘텐츠를 position에서 옮기면 옛 위치 기준의 남은 회수·활성화를 멈추고 false를 돌려준다.
        /// </summary>
        private bool ApplyActiveSlots(int first, int last, float position)
        {
            bool hasNew = first <= last;
            bool overlaps = HasActiveRange && hasNew && last >= _activeFirst && first <= _activeLast;

            // 목록·범위 카운터를 먼저 맞춘 뒤 사용자 코드(이벤트·OnRecycled)를 호출한다.
            // 사용자 코드가 예외를 던지거나 도중에 멈춰도 _activeCells와 [_activeFirst, _activeLast]가 어긋나지 않는다.
            if (!overlaps)
            {
                RecycleAllActive();
                if (HasMovedFrom(position))
                {
                    return false;
                }

                if (hasNew)
                {
                    _activeFirst = first;
                    _activeLast = first - 1;
                    while (_activeLast < last)
                    {
                        CyScrollerCellView added = ActivateSlot(_activeLast + 1, false);
                        _activeLast++;
                        NotifyShown(added);
                        if (HasMovedFrom(position))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            // 나가는 셀을 먼저 풀에 돌려야 들어오는 셀이 재사용할 수 있다.
            // 표시 범위는 이미 새 범위 안으로 줄였으므로 나가는 셀은 보통 보이지 않는다. 그래도 보이는 셀을 빼야 하면
            // 표시 끝을 먼저 알리고 상태를 다시 읽는다 (표시 범위 ⊆ 활성 범위 유지).
            while (_activeFirst < first && HasActiveRange)
            {
                if (HasVisibleRange && _visibleFirst == _activeFirst)
                {
                    _visibleFirst++;
                    EndDisplayAt(_activeFirst);
                }
                else
                {
                    CyScrollerCellView removed = TakeActiveAt(0);
                    _activeFirst++;
                    RecycleCell(removed);
                }

                if (HasMovedFrom(position))
                {
                    return false;
                }
            }

            while (_activeLast > last && HasActiveRange)
            {
                if (HasVisibleRange && _visibleLast == _activeLast)
                {
                    _visibleLast--;
                    EndDisplayAt(_activeLast);
                }
                else
                {
                    CyScrollerCellView removed = TakeActiveAt(_activeCells.Count - 1);
                    _activeLast--;
                    RecycleCell(removed);
                }

                if (HasMovedFrom(position))
                {
                    return false;
                }
            }

            // 겹치는 범위는 회수로 비지 않는다 (순환 보정은 범위 갱신 뒤로 미루므로 콜백이 슬롯 번호를 바꾸지 못한다).
            // 그래도 비었으면 옛 번호에서 이어 채우지 않고 새 범위 처음부터 채워 활성화 수를 상한 안에 둔다.
            if (!HasActiveRange)
            {
                _activeFirst = first;
                _activeLast = first - 1;
            }

            while (_activeFirst > first)
            {
                CyScrollerCellView added = ActivateSlot(_activeFirst - 1, true);
                _activeFirst--;
                NotifyShown(added);
                if (HasMovedFrom(position))
                {
                    return false;
                }
            }

            while (_activeLast < last)
            {
                CyScrollerCellView added = ActivateSlot(_activeLast + 1, false);
                _activeLast++;
                NotifyShown(added);
                if (HasMovedFrom(position))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 새로 활성화된 셀을 알린다. 위치 훅이 켜져 있으면 렌더 전에 첫 위치도 바로 준다
        /// (다음 LateUpdate까지 기다리면 재사용된 뷰가 이전 데이터 자리의 모습으로 한 프레임 보일 수 있다).
        /// </summary>
        private void NotifyShown(CyScrollerCellView cell)
        {
            if (cell == null)
            {
                return;
            }

            CellViewVisibilityChanged?.Invoke(cell);

            if (_notifyCellPositions && cell.Active)
            {
                NotifyCellPosition(cell, ScrollPosition, ScrollRectSize);
            }
        }

        /// <summary>
        /// 슬롯의 셀 뷰를 받아 배치하고 활성 목록 앞이나 뒤에 넣는다. 셀을 못 받으면 null 자리를 넣는다.
        /// 보이기 이벤트는 호출하지 않는다 (호출자가 범위 카운터를 맞춘 뒤 <see cref="NotifyShown"/>).
        /// </summary>
        private CyScrollerCellView ActivateSlot(int slot, bool atFront)
        {
            CyScrollerCellView cell = BindSlot(slot);

            // 셀을 못 받았으면 슬롯과 목록 인덱스 대응을 유지하려고 빈 자리를 넣는다.
            InsertActive(cell, atFront);
            return cell;
        }

        /// <summary>
        /// 슬롯의 셀 뷰를 델리게이트에서 받아 바인딩하고 배치한다. 못 받으면 null. 활성 목록에는 넣지 않는다 (호출자가 넣는다).
        /// </summary>
        private CyScrollerCellView BindSlot(int slot)
        {
            int dataIndex = _layout.SlotToDataIndex(slot);

            _pendingDataIndex = dataIndex;
            _pendingCellIndex = slot;
            _pendingBoundCell = null;
            CyScrollerCellView cell = _delegate != null ? _delegate.GetCellView(this, dataIndex, slot) : null;
            CyScrollerCellView boundCell = _pendingBoundCell;
            _pendingDataIndex = -1;
            _pendingCellIndex = -1;
            _pendingBoundCell = null;

            if (cell != null && cell.Active)
            {
                Debug.LogError($"[CyScroller] 이미 활성인 셀 뷰가 다시 반환됐습니다. dataIndex={dataIndex}. GetCellView(prefab)로 받은 뷰를 반환하세요.", cell);
                cell = null;
            }

            if (cell == null)
            {
                if (_delegate != null)
                {
                    Debug.LogError($"[CyScroller] GetCellView가 셀 뷰를 반환하지 않았습니다. dataIndex={dataIndex}", this);
                }

                return null;
            }

            // GetCellView(prefab)로 받지 않은 뷰(델리게이트가 직접 만든 뷰 등)도 이번 바인딩으로 센다.
            if (cell != boundCell)
            {
                cell.BindVersion++;
            }

            cell.Scroller = this;
            cell.DataIndex = dataIndex;
            cell.CellIndex = slot;
            cell.Active = true;
            AssignItemId(cell, dataIndex);

            Transform cellTransform = cell.transform;
            if (cellTransform.parent != _content)
            {
                cellTransform.SetParent(_content, false);
            }

            PositionCell(cell, slot);

            if (!cell.gameObject.activeSelf)
            {
                cell.gameObject.SetActive(true);
            }

            return cell;
        }

        private void InsertActive(CyScrollerCellView cell, bool atFront)
        {
            if (atFront)
            {
                _activeCells.Insert(0, cell);
            }
            else
            {
                _activeCells.Add(cell);
            }
        }

        private void PositionCell(CyScrollerCellView cell, int slot)
        {
            RectTransform rect = cell.RectTransform;
            float start = _layout.GetSlotStart(slot);
            float size = _layout.GetSlotSize(slot);
            Vector2 pivot = rect.pivot;

            if (_appliedVertical)
            {
                // 위쪽 가장자리에 붙이고 가로는 좌우 여백만큼 늘린다.
                SetAnchors(rect, new Vector2(0f, 1f), new Vector2(1f, 1f));
                float crossInset = _padding.left + _padding.right;
                rect.sizeDelta = new Vector2(-crossInset, size);
                rect.anchoredPosition = new Vector2(
                    _padding.left - crossInset * pivot.x,
                    -start - size * (1f - pivot.y));
            }
            else
            {
                // 왼쪽 가장자리에 붙이고 세로는 위아래 여백만큼 늘린다.
                SetAnchors(rect, new Vector2(0f, 0f), new Vector2(0f, 1f));
                float crossInset = _padding.top + _padding.bottom;
                rect.sizeDelta = new Vector2(size, -crossInset);
                rect.anchoredPosition = new Vector2(
                    start + size * pivot.x,
                    _padding.bottom - crossInset * pivot.y);
            }
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            if (rect.anchorMin != min)
            {
                rect.anchorMin = min;
            }

            if (rect.anchorMax != max)
            {
                rect.anchorMax = max;
            }
        }

        /// <summary>
        /// 활성 셀의 슬롯 번호를 slotDelta만큼 옮긴다 (루프 순환 보정). 범위 갱신 중에는 부르지 않는다
        /// (진행 중인 범위 계산이 옛 번호를 쓰므로 <see cref="RecenterLoopIfNeeded"/>가 미룬다).
        /// </summary>
        private void ShiftActiveSlots(int slotDelta)
        {
            if (!HasActiveRange || slotDelta == 0)
            {
                return;
            }

            int first = _activeFirst + slotDelta;
            int last = _activeLast + slotDelta;
            if (first < 0 || last >= _layout.SlotCount)
            {
                // 미리보기 구간이 콘텐츠 끝을 넘으면 옮기지 않고 다시 만든다 (호출자가 범위를 갱신한다).
                RecycleAllActive();
                return;
            }

            // 같은 셀이 슬롯 번호만 바뀌므로 표시 범위도 같이 옮기고 표시 이벤트는 내지 않는다.
            _activeFirst = first;
            _activeLast = last;
            if (HasVisibleRange)
            {
                _visibleFirst += slotDelta;
                _visibleLast += slotDelta;
            }

            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                if (cell == null)
                {
                    continue;
                }

                cell.CellIndex += slotDelta;
                PositionCell(cell, cell.CellIndex);
            }
        }

        private void RecycleAllActive()
        {
            // 보이던 셀의 표시 끝을 먼저 알린다.
            EndDisplayAll();

            // 뒤에서부터 목록·카운터를 줄인 뒤 회수한다. 사용자 코드가 예외를 던져도 같은 셀을 두 번 회수하지 않는다.
            while (_activeCells.Count > 0)
            {
                CyScrollerCellView cell = TakeActiveAt(_activeCells.Count - 1);
                _activeLast--;
                RecycleCell(cell);

                // 회수 콜백 안의 리로드가 새 셀을 보였으면 그 셀도 회수하기 전에 표시 끝을 알린다. 비었으면 O(1).
                EndDisplayAll();
            }

            _activeFirst = 0;
            _activeLast = -1;
        }

        private CyScrollerCellView TakeActiveAt(int index)
        {
            CyScrollerCellView cell = _activeCells[index];
            _activeCells.RemoveAt(index);
            return cell;
        }

        private void RecycleCell(CyScrollerCellView cell)
        {
            if (cell == null)
            {
                return;
            }

            try
            {
                CellViewWillRecycle?.Invoke(cell);
                cell.OnRecycled();
                cell.Active = false;
                CellViewVisibilityChanged?.Invoke(cell);
            }
            finally
            {
                // 사용자 코드가 예외를 던져도 셀은 반드시 꺼서 풀에 넣는다 (보이는 채로 새지 않게).
                UnbindCell(cell);

                // 재부모화 없이 content 아래에서 끄기만 한다 (SetParent·레이아웃 dirty 비용 회피).
                cell.gameObject.SetActive(false);
                GetPool(cell.CellIdentifier).Add(cell);
            }
        }

        /// <summary>셀의 바인딩을 푼다. 바인딩 버전을 올려 늦게 끝난 비동기 작업이 결과를 버리게 한다.</summary>
        private static void UnbindCell(CyScrollerCellView cell)
        {
            cell.Active = false;
            cell.DataIndex = -1;
            cell.CellIndex = -1;
            cell.ItemId = 0L;
            cell.HasItemId = false;
            cell.BindVersion++;
        }

        private CyScrollerCellView PopRecycled(string identifier)
        {
            if (!_pools.TryGetValue(identifier, out List<CyScrollerCellView> pool))
            {
                return null;
            }

            while (pool.Count > 0)
            {
                int lastIndex = pool.Count - 1;
                CyScrollerCellView cell = pool[lastIndex];
                pool.RemoveAt(lastIndex);
                if (cell != null)
                {
                    return cell;
                }
            }

            return null;
        }

        private List<CyScrollerCellView> GetPool(string identifier)
        {
            identifier ??= string.Empty;
            if (!_pools.TryGetValue(identifier, out List<CyScrollerCellView> pool))
            {
                pool = new List<CyScrollerCellView>();
                _pools.Add(identifier, pool);
            }

            return pool;
        }

        private void AssignPendingIndices(CyScrollerCellView cell)
        {
            cell.Scroller = this;
            cell.DataIndex = _pendingDataIndex;
            cell.CellIndex = _pendingCellIndex;
            AssignItemId(cell, _pendingDataIndex);

            if (_pendingDataIndex >= 0)
            {
                // 델리게이트가 이 뒤에 데이터를 채우므로 여기서 올려야 바인딩 코드가 읽는 값이 이번 바인딩의 값이다.
                cell.BindVersion++;
                _pendingBoundCell = cell;
            }
        }

        private void SetScrolling(bool scrolling)
        {
            if (_isScrolling == scrolling)
            {
                return;
            }

            _isScrolling = scrolling;
            ScrollerScrollingChanged?.Invoke(this, scrolling);
        }

        private void ClampVelocity()
        {
            if (_maxVelocity <= 0f)
            {
                return;
            }

            float velocity = LinearVelocity;
            if (Mathf.Abs(velocity) > _maxVelocity)
            {
                LinearVelocity = Mathf.Sign(velocity) * _maxVelocity;
            }
        }

        #endregion
    }
}
