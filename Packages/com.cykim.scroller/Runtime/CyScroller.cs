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
    /// <para>사용 흐름: <see cref="Delegate"/>에 <see cref="ICyScrollerDelegate"/>를 넣고 <see cref="ReloadData"/>를 호출한다.</para>
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
        private bool _listening;
        private bool _isScrolling;
        private bool _warnedEmptyIdentifier;
        private bool _warnedActiveCap;
        private float _lastViewportExtent = -1f;

        // content가 실제로 맞춰져 있는 축. 방향을 바꾼 직후 이전 축으로 위치를 읽는 데 쓴다.
        private bool _appliedVertical = true;

        // 범위 갱신(델리게이트·이벤트 콜백) 안에서 들어온 요청은 끝난 뒤 처리한다.
        private bool _reloadPending;
        private float _pendingReloadFactor;
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

        public event CellViewVisibilityChangedHandler CellViewVisibilityChanged;
        public event CellViewInstantiatedHandler CellViewInstantiated;
        public event CellViewReusedHandler CellViewReused;
        public event CellViewWillRecycleHandler CellViewWillRecycle;
        public event ScrollerScrolledHandler ScrollerScrolled;
        public event ScrollerSnappedHandler ScrollerSnapped;
        public event ScrollerScrollingChangedHandler ScrollerScrollingChanged;
        public event ScrollerTweeningChangedHandler ScrollerTweeningChanged;

        #endregion

        #region Properties

        /// <summary>
        /// 데이터 공급자. 넣으면 다음 LateUpdate(또는 그 전의 첫 범위 갱신)에 처음 위치로 <see cref="ReloadData"/>한다.
        /// 새 델리게이트가 옛 인덱스로 호출되는 일은 없다.
        /// </summary>
        public ICyScrollerDelegate Delegate
        {
            get => _delegate;
            set
            {
                _delegate = value;
                _reloadPending = true;
                _pendingReloadFactor = 0f;
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

        /// <summary>논리 데이터 개수 (마지막 로드 기준).</summary>
        public int NumberOfCells => _layout.DataCount;

        /// <summary>스크롤 시퀀스 슬롯 개수. 루프면 데이터 개수 × 세트 수.</summary>
        public int NumberOfCellSlots => _layout.SlotCount;

        /// <summary>
        /// 콘텐츠 시작(위·왼쪽)에서 뷰포트 시작까지 거리. 대입하면 진행 중인 트윈·관성·점프 정렬을 멈추고 그 위치로 옮긴다.
        /// 드래그 중이면 손가락 아래 기준점도 새 위치로 옮긴다.
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

                CancelTween();
                _alignmentActive = false;
                _snapArmed = false;
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

                ScrollPosition = GetPositionForFactor(value);
            }
        }

        /// <summary>스크롤 가능한 거리 = 콘텐츠 길이 − 뷰포트 길이 (0 이상).</summary>
        public float ScrollSize => Mathf.Max(0f, _layout.ContentExtent - ScrollRectSize);

        /// <summary>뷰포트의 스크롤 축 길이.</summary>
        public float ScrollRectSize
        {
            get
            {
                if (_viewport == null)
                {
                    return 0f;
                }

                Rect rect = _viewport.rect;
                return IsVertical ? rect.height : rect.width;
            }
        }

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
            FlushPendingWork();

            if (!_hasLoaded)
            {
                return;
            }

            CheckViewportResize();

            float deltaTime = Time.unscaledDeltaTime;
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
        }

        #endregion

        #region Public API

        /// <summary>
        /// 델리게이트에서 개수·크기를 다시 받아 처음부터 배치한다. 진행 중인 관성·트윈은 멈춘다.
        /// </summary>
        /// <param name="scrollPositionFactor">
        /// 로드 후 위치. 0 = 처음, 1 = 끝. 루프 모드에서는 한 사이클 안의 비율이다.
        /// 데이터가 바뀌어도 보던 셀을 유지하려면 <see cref="ReloadDataKeepingPosition"/>을 쓴다.
        /// </param>
        public void ReloadData(float scrollPositionFactor = 0f)
        {
            if (!EnsureInitialized())
            {
                return;
            }

            if (_inRangeUpdate)
            {
                // 델리게이트 콜백 안에서 호출되면 범위 갱신이 끝난 뒤 처리한다.
                _reloadPending = true;
                _pendingReloadFactor = scrollPositionFactor;
                return;
            }

            _reloadPending = false;
            _relayoutPending = false;
            _relayoutRequery = false;
            _relayoutReconfigure = false;
            ApplyPendingClears();

            CancelTween();
            _snapArmed = false;
            _alignmentActive = false;
            _scrollRect.StopMovement();

            RecycleAllActive();
            RebuildLayout(true);
            _hasLoaded = true;

            MoveContentTo(GetPositionForFactor(scrollPositionFactor));
            _lastViewportExtent = ScrollRectSize;
            UpdateActiveRange();
            ApplyScrollbarVisibility();
        }

        /// <summary>
        /// 개수·크기를 다시 받되, 뷰포트 맨 앞에 걸친 데이터 인덱스와 그 셀 안의 오프셋을 유지한다.
        /// 셀 크기가 바뀌었거나 뒤쪽에 항목이 추가됐을 때 쓴다. 델리게이트 콜백 안에서 불러도 된다 (끝난 뒤 처리).
        /// </summary>
        public void ReloadDataKeepingPosition()
        {
            if (!_hasLoaded || _reloadPending)
            {
                ReloadData(_reloadPending ? _pendingReloadFactor : 0f);
                return;
            }

            RelayoutKeepingPosition(true, false);
        }

        /// <summary>
        /// 활성 셀마다 <see cref="CyScrollerCellView.RefreshCellView"/>를 호출한다. 크기는 다시 계산하지 않는다.
        /// 기본 구현은 비어 있으므로, 셀 뷰가 이를 재정의해 자기 데이터로 다시 그려야 한다.
        /// </summary>
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
        /// 활성 셀을 파괴한다. 셀 프리팹을 바꾼 뒤 <see cref="ReloadData"/>와 함께 쓴다.
        /// 델리게이트·이벤트 콜백 안에서 부르면 범위 갱신이 끝난 뒤 처리한다.
        /// </summary>
        public void ClearActive()
        {
            if (_inRangeUpdate)
            {
                _clearActivePending = true;
                return;
            }

            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                if (cell != null)
                {
                    cell.Active = false;
                    Destroy(cell.gameObject);
                }
            }

            _activeCells.Clear();
            _activeFirst = 0;
            _activeLast = -1;
        }

        /// <summary>
        /// 재활용 풀의 셀을 모두 파괴한다. 다시는 쓰지 않을 프리팹 종류를 정리할 때 쓴다.
        /// 델리게이트·이벤트 콜백 안에서 부르면 범위 갱신이 끝난 뒤 처리한다.
        /// </summary>
        public void ClearRecycled()
        {
            if (_inRangeUpdate)
            {
                _clearRecycledPending = true;
                return;
            }

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
        /// </summary>
        private bool FlushPendingWork()
        {
            if (_inRangeUpdate || !_initialized)
            {
                return false;
            }

            ApplyPendingClears();

            if (_reloadPending)
            {
                ReloadData(_pendingReloadFactor);
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
                ClearActive();
            }

            if (_clearRecycledPending)
            {
                _clearRecycledPending = false;
                ClearRecycled();
            }
        }

        #endregion

        #region Layout

        private void RebuildLayout(bool requeryDelegate)
        {
            if (requeryDelegate)
            {
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
            }

            GetMainAxisPadding(out float paddingBefore, out float paddingAfter);
            _layout.Build(_spacing, paddingBefore, paddingAfter, _loop, ScrollRectSize, _lookAheadBefore, _lookAheadAfter);

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

            if (_inRangeUpdate)
            {
                _relayoutPending = true;
                _relayoutRequery |= requeryDelegate;
                _relayoutReconfigure |= reconfigure;
                return;
            }

            if (_reloadPending)
            {
                // 델리게이트가 바뀌었으면 위치 유지보다 새 데이터 로드가 먼저다.
                ReloadData(_pendingReloadFactor);
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
        /// 진행 중인 트윈(점프·스냅·ScrollIntoView)은 끊지 않는다. 화면은 맨 앞 셀 기준으로 그대로 두고, 같은 데이터(개수가 줄었으면 잘린 인덱스)를
        /// 향해 남은 시간 동안 계속 간다. 목표는 새 배치에서 다시 계산하고, 트윈 시작점은 다음 프레임에 화면이 튀지 않게 다시 잡는다(<see cref="RebaseTween"/>).
        /// </remarks>
        private void ApplyRelayout(bool requeryDelegate, bool reconfigure)
        {
            // 회수 콜백(셀 이벤트·OnRecycled)이 트윈·정렬·위치를 바꿨어도 바뀐 상태로 처리하도록 셀부터 회수한 뒤 상태를 읽는다.
            RecycleAllActive();

            // 트윈이 아닌 정렬 유지는 데이터를 다시 받지 않을 때만 지킨다 (다시 받으면 같은 인덱스가 다른 항목일 수 있다).
            bool tweening = _tweening;
            bool keepAlignment = _alignmentActive && (tweening || !requeryDelegate);
            int alignDataIndex = keepAlignment ? _layout.SlotToDataIndex(_align.Slot) : -1;

            float previousPosition = ReadPosition(_appliedVertical);
            CaptureAnchor(out int anchorDataIndex, out float anchorOffset, out float anchorOverscroll);

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

            RebuildLayout(requeryDelegate);

            // 모든 경로가 MoveContentTo로 옮긴다 (드래그 중이면 기준점·직전 위치까지 맞춤).
            bool tweenEmptied = false;
            Action emptiedComplete = null;
            if (tweening)
            {
                int anchorSlot = RestoreAnchor(anchorDataIndex, anchorOffset, anchorOverscroll);
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
                RestoreAnchor(anchorDataIndex, anchorOffset, anchorOverscroll);
            }

            _lastViewportExtent = ScrollRectSize;
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

            // 점프·스냅으로 맞춘 상태면 새 뷰포트 크기로 정렬을 다시 계산한다 (예: 첫 프레임 Canvas 크기 확정, 화면 회전).
            // 트윈 중이면 목표는 매 프레임 다시 계산하므로, 다음 프레임에 화면이 튀지 않게 시작점만 다시 잡는다.
            // 정렬이 없으면 콘텐츠 시작 기준 위치를 그대로 둔다.
            if (_tweening)
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

            // 델리게이트 교체·콜백 안 요청이 남아 있으면 옛 배치로 새 델리게이트를 부르지 않도록 먼저 처리한다.
            if (FlushPendingWork())
            {
                return;
            }

            using (_updateActiveRangeMarker.Auto())
            {
                ApplyActiveRange();
            }
        }

        /// <summary>현재 위치에 맞게 활성 슬롯 범위를 맞춘다. 나가는 셀을 먼저 회수하고 들어오는 셀을 받는다.</summary>
        private void ApplyActiveRange()
        {
            float position = ScrollPosition;
            _layout.GetSlotRange(
                position - _lookAheadBefore,
                position + ScrollRectSize + _lookAheadAfter,
                out int first,
                out int last);

            if (last - first + 1 > MAX_ACTIVE_CELLS)
            {
                last = first + MAX_ACTIVE_CELLS - 1;
                if (!_warnedActiveCap)
                {
                    _warnedActiveCap = true;
                    Debug.LogWarning($"[CyScroller] 한 번에 활성화할 셀이 {MAX_ACTIVE_CELLS}개를 넘습니다. 셀 크기가 0에 가깝지 않은지 확인하세요.", this);
                }
            }

            if (first == _activeFirst && last == _activeLast)
            {
                return;
            }

            _inRangeUpdate = true;
            try
            {
                bool hasNew = first <= last;
                bool overlaps = HasActiveRange && hasNew && last >= _activeFirst && first <= _activeLast;

                // 목록·범위 카운터를 먼저 맞춘 뒤 사용자 코드(이벤트·OnRecycled)를 호출한다.
                // 사용자 코드가 예외를 던져도 _activeCells와 [_activeFirst, _activeLast]가 어긋나지 않는다.
                if (!overlaps)
                {
                    RecycleAllActive();
                    if (hasNew)
                    {
                        _activeFirst = first;
                        _activeLast = first - 1;
                        while (_activeLast < last)
                        {
                            CyScrollerCellView added = ActivateSlot(_activeLast + 1, false);
                            _activeLast++;
                            NotifyShown(added);
                        }
                    }

                    return;
                }

                // 나가는 셀을 먼저 풀에 돌려야 들어오는 셀이 재사용할 수 있다.
                while (_activeFirst < first)
                {
                    CyScrollerCellView removed = TakeActiveAt(0);
                    _activeFirst++;
                    RecycleCell(removed);
                }

                while (_activeLast > last)
                {
                    CyScrollerCellView removed = TakeActiveAt(_activeCells.Count - 1);
                    _activeLast--;
                    RecycleCell(removed);
                }

                while (_activeFirst > first)
                {
                    CyScrollerCellView added = ActivateSlot(_activeFirst - 1, true);
                    _activeFirst--;
                    NotifyShown(added);
                }

                while (_activeLast < last)
                {
                    CyScrollerCellView added = ActivateSlot(_activeLast + 1, false);
                    _activeLast++;
                    NotifyShown(added);
                }
            }
            finally
            {
                _inRangeUpdate = false;
            }
        }

        private void NotifyShown(CyScrollerCellView cell)
        {
            if (cell != null)
            {
                CellViewVisibilityChanged?.Invoke(cell);
            }
        }

        /// <summary>
        /// 슬롯의 셀 뷰를 받아 배치하고 활성 목록 앞이나 뒤에 넣는다. 셀을 못 받으면 null 자리를 넣는다.
        /// 보이기 이벤트는 호출하지 않는다 (호출자가 범위 카운터를 맞춘 뒤 <see cref="NotifyShown"/>).
        /// </summary>
        private CyScrollerCellView ActivateSlot(int slot, bool atFront)
        {
            int dataIndex = _layout.SlotToDataIndex(slot);

            _pendingDataIndex = dataIndex;
            _pendingCellIndex = slot;
            CyScrollerCellView cell = _delegate != null ? _delegate.GetCellView(this, dataIndex, slot) : null;
            _pendingDataIndex = -1;
            _pendingCellIndex = -1;

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

                // 슬롯과 목록 인덱스 대응을 유지하려고 빈 자리를 넣는다.
                InsertActive(null, atFront);
                return null;
            }

            cell.Scroller = this;
            cell.DataIndex = dataIndex;
            cell.CellIndex = slot;
            cell.Active = true;

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

            InsertActive(cell, atFront);
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

            _activeFirst = first;
            _activeLast = last;
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
            // 뒤에서부터 목록·카운터를 줄인 뒤 회수한다. 사용자 코드가 예외를 던져도 같은 셀을 두 번 회수하지 않는다.
            while (_activeCells.Count > 0)
            {
                CyScrollerCellView cell = TakeActiveAt(_activeCells.Count - 1);
                _activeLast--;
                RecycleCell(cell);
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
                cell.Active = false;
                cell.DataIndex = -1;
                cell.CellIndex = -1;

                // 재부모화 없이 content 아래에서 끄기만 한다 (SetParent·레이아웃 dirty 비용 회피).
                cell.gameObject.SetActive(false);
                GetPool(cell.CellIdentifier).Add(cell);
            }
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
