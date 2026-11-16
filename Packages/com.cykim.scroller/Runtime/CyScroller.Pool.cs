using System.Collections.Generic;
using UnityEngine;

namespace CyKim.Scroller
{
    // 셀 풀: 셀 식별자마다 회수한 셀을 비활성으로 보관한다. 미리 채우기(프리웜)와 식별자별 상한.
    public partial class CyScroller
    {
        /// <summary>
        /// 한 셀 식별자의 회수 풀. 상한을 풀 객체에 두어 회수할 때마다 사전을 다시 찾지 않는다.
        /// Cells는 회수한 순서이고(앞이 오래됨), 꺼낼 때는 뒤(최근)부터 꺼낸다.
        /// </summary>
        private sealed class CellPool
        {
            public readonly List<CyScrollerCell> Cells = new List<CyScrollerCell>();

            /// <summary>이 식별자에 정한 상한. -1이면 <see cref="DefaultMaxPoolSize"/>를 따르고, 0이면 제한 없음.</summary>
            public int MaxPoolSize = -1;

            /// <summary>진행 중인 비동기 프리웜이 만들 셀 수 (<see cref="PrewarmAsync"/>). 같은 요청이 겹쳐도 더 만들지 않게 센다.</summary>
            public int PendingPrewarm;
        }

        /// <summary>
        /// 셀 식별자마다 회수 풀에 남길 셀 수의 기본 상한. 0이면 제한 없음(기본). <see cref="SetMaxPoolSize"/>로 정하지 않은 식별자에 쓴다.
        /// 넘치면 가장 오래 회수된 셀부터 파괴한다. 바꾸면 이 값을 따르는 풀을 바로 줄인다.
        /// </summary>
        public int DefaultMaxPoolSize
        {
            get => _defaultMaxPoolSize;
            set
            {
                _defaultMaxPoolSize = Mathf.Max(0, value);
                foreach (KeyValuePair<string, CellPool> pair in _pools)
                {
                    if (pair.Value.MaxPoolSize < 0)
                    {
                        TrimPool(pair.Value);
                    }
                }
            }
        }

        /// <summary>
        /// reuseIdentifier 풀에 남길 회수 셀 수의 상한을 정한다. 넘치면 가장 오래 회수된 셀부터 파괴한다(지금 넘쳐 있으면 바로).
        /// 0이면 제한 없음, 음수면 <see cref="DefaultMaxPoolSize"/>를 따른다. 활성 셀 수는 제한하지 않는다.
        /// </summary>
        /// <param name="reuseIdentifier">셀 프리팹의 <see cref="CyScrollerCell.ReuseIdentifier"/>. null은 빈 식별자로 본다.</param>
        /// <param name="max">상한.</param>
        public void SetMaxPoolSize(string reuseIdentifier, int max)
        {
            CellPool pool = GetPool(reuseIdentifier);
            pool.MaxPoolSize = max < 0 ? -1 : max;
            TrimPool(pool);
        }

        /// <summary>reuseIdentifier 풀에 적용되는 상한 (식별자별 값이 없으면 <see cref="DefaultMaxPoolSize"/>). 0이면 제한 없음.</summary>
        public int GetMaxPoolSize(string reuseIdentifier)
        {
            return _pools.TryGetValue(reuseIdentifier ?? string.Empty, out CellPool pool) ? GetMaxPoolSize(pool) : _defaultMaxPoolSize;
        }

        /// <summary>
        /// prefab 식별자의 회수 풀에 비활성 셀이 count개가 될 때까지 지금 만들어 채운다 (상한이 있으면 상한까지, 진행 중인 <see cref="PrewarmAsync"/> 요청도 센다).
        /// 첫 스크롤에 셀을 만드는 비용을 화면 전환·로딩 중으로 옮길 때 쓴다. 만든 셀마다 <see cref="CellCreated"/>를 부른다.
        /// </summary>
        /// <param name="prefab">셀 프리팹. 데이터 소스가 <see cref="DequeueCell"/>에 넘기는 것과 같은 프리팹(같은 식별자)이어야 쓰인다.</param>
        /// <param name="count">풀에 채울 개수 (이미 있는 회수 셀 포함).</param>
        /// <remarks>
        /// 셀은 content 아래에 만들어 바로 끈다. 프리팹이 켜져 있으면 셀의 Awake·OnEnable은 끄기 전에 한 번 불린다.
        /// 활성 셀 수는 세지 않으므로, 화면을 채울 셀까지 미리 만들려면 (뷰포트 + 미리보기 구간 분량)을 넘긴다.
        /// </remarks>
        public void Prewarm(CyScrollerCell prefab, int count)
        {
            if (prefab == null)
            {
                Debug.LogError("[CyScroller] Prewarm에 null 프리팹이 들어왔습니다.");
                return;
            }

            if (!EnsureInitialized())
            {
                return;
            }

            string identifier = ResolvePoolIdentifier(prefab);
            CellPool pool = GetPool(identifier);
            int target = ClampToMaxPoolSize(pool, count);
            while (pool.Cells.Count + pool.PendingPrewarm < target)
            {
                CyScrollerCell cell = Instantiate(prefab, _content, false);
                AddPrewarmedCell(pool, cell, identifier);
                CellCreated?.Invoke(this, cell);
            }
        }

        /// <summary>
        /// <see cref="Prewarm"/>의 비동기판. 모자란 수만큼 <see cref="Object.InstantiateAsync{T}(T, int, Transform)"/>로 만들고, 끝나면 끈 채 풀에 넣는다.
        /// 할 일이 없으면(이미 차 있거나 진행 중인 요청이 채울 예정이면) null을 돌려준다.
        /// </summary>
        /// <param name="prefab">셀 프리팹.</param>
        /// <param name="count">풀에 채울 개수 (이미 있는 회수 셀과 진행 중인 비동기 요청 포함).</param>
        /// <returns>진행 중인 만들기 작업. 코루틴에서 기다리거나 <c>completed</c>에 이어 붙인다.</returns>
        /// <remarks>
        /// 끝났을 때 상한을 넘는 분은 파괴하고, 그사이 스크롤러가 파괴됐으면 만든 셀을 모두 파괴한다. 만든 셀마다 <see cref="CellCreated"/>를 부른다.
        /// </remarks>
        public AsyncInstantiateOperation<CyScrollerCell> PrewarmAsync(CyScrollerCell prefab, int count)
        {
            if (prefab == null)
            {
                Debug.LogError("[CyScroller] PrewarmAsync에 null 프리팹이 들어왔습니다.");
                return null;
            }

            if (!EnsureInitialized())
            {
                return null;
            }

            string identifier = ResolvePoolIdentifier(prefab);
            CellPool pool = GetPool(identifier);
            int needed = ClampToMaxPoolSize(pool, count) - pool.Cells.Count - pool.PendingPrewarm;
            if (needed <= 0)
            {
                return null;
            }

            AsyncInstantiateOperation<CyScrollerCell> operation = InstantiateAsync(prefab, needed, _content);
            pool.PendingPrewarm += needed;
            operation.completed += _ => CompletePrewarmAsync(operation, pool, identifier, needed);
            return operation;
        }

        /// <summary>
        /// 비동기 프리웜이 끝났을 때. 스크롤러가 살아 있으면 먼저 모두 끈 채 풀에 넣고(사용자 코드 전) 상한으로 자른 뒤 남은 셀마다 알리고,
        /// 파괴됐으면 만든 셀을 모두 파괴한다. 알림 핸들러가 예외를 던져도 만든 셀은 이미 풀에 있다.
        /// </summary>
        private void CompletePrewarmAsync(AsyncInstantiateOperation<CyScrollerCell> operation, CellPool pool, string identifier, int requested)
        {
            pool.PendingPrewarm = Mathf.Max(0, pool.PendingPrewarm - requested);
            CyScrollerCell[] results = operation.Result;
            if (results == null)
            {
                return;
            }

            bool alive = this != null;
            for (int i = 0; i < results.Length; i++)
            {
                CyScrollerCell cell = results[i];
                if (cell == null)
                {
                    continue;
                }

                if (alive)
                {
                    AddPrewarmedCell(pool, cell, identifier);
                }
                else
                {
                    Destroy(cell.gameObject);
                }
            }

            if (!alive)
            {
                return;
            }

            // 넘친 만큼(오래된 셀부터) 먼저 자르고, 풀에 남은 새 셀에만 알린다.
            TrimPool(pool);
            for (int i = 0; i < results.Length; i++)
            {
                CyScrollerCell cell = results[i];
                if (cell != null && pool.Cells.Contains(cell))
                {
                    CellCreated?.Invoke(this, cell);
                }
            }
        }

        /// <summary>만든 셀을 회수한 셀과 같은 상태(끔, 바인딩 없음)로 풀 뒤에 넣는다. 사용자 코드는 부르지 않는다.</summary>
        private static void AddPrewarmedCell(CellPool pool, CyScrollerCell cell, string identifier)
        {
            cell.ReuseIdentifier = identifier;
            cell.gameObject.SetActive(false);
            pool.Cells.Add(cell);
        }

        /// <summary>풀이 상한을 넘으면 가장 오래 회수된 셀(앞)부터 파괴한다. 상한이 0이면 아무것도 하지 않는다. 할당 없음.</summary>
        private void TrimPool(CellPool pool)
        {
            int max = GetMaxPoolSize(pool);
            if (max <= 0)
            {
                return;
            }

            List<CyScrollerCell> cells = pool.Cells;
            int excess = cells.Count - max;
            if (excess <= 0)
            {
                return;
            }

            for (int i = 0; i < excess; i++)
            {
                CyScrollerCell cell = cells[i];
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            cells.RemoveRange(0, excess);
        }

        private int GetMaxPoolSize(CellPool pool) => pool.MaxPoolSize >= 0 ? pool.MaxPoolSize : _defaultMaxPoolSize;

        /// <summary>count를 그 풀의 상한(있으면)으로 자른다.</summary>
        private int ClampToMaxPoolSize(CellPool pool, int count)
        {
            int max = GetMaxPoolSize(pool);
            return max > 0 ? Mathf.Min(count, max) : count;
        }

        /// <summary>프리팹의 풀 식별자. 비어 있으면 빈 문자열이고, 처음 한 번 경고한다 (종류가 다른 프리팹이 같은 풀을 쓰게 되므로).</summary>
        private string ResolvePoolIdentifier(CyScrollerCell prefab)
        {
            string identifier = prefab.ReuseIdentifier;
            if (!string.IsNullOrEmpty(identifier))
            {
                return identifier;
            }

            if (!_warnedEmptyIdentifier)
            {
                _warnedEmptyIdentifier = true;
                Debug.LogWarning("[CyScroller] ReuseIdentifier가 빈 셀 프리팹이 있습니다. 종류가 다른 프리팹이 같은 풀을 공유하게 됩니다.", prefab);
            }

            return string.Empty;
        }
    }
}
