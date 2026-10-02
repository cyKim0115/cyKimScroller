using System;
using UnityEngine;

namespace CyKim.Scroller.Tests
{
    /// <summary>
    /// 스크롤러(실행 순서 100)보다 늦은 LateUpdate에서 동작을 한 번 실행한다. 스크롤러 LateUpdate 뒤에 일어나는 변화를 흉내 낸다.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class LateUpdateProbe : MonoBehaviour
    {
        /// <summary>다음 LateUpdate에 한 번 실행할 동작.</summary>
        public Action Pending;

        /// <summary>마지막으로 동작을 실행한 프레임.</summary>
        public int RanFrame = -1;

        private void LateUpdate()
        {
            Action action = Pending;
            if (action == null)
            {
                return;
            }

            Pending = null;
            RanFrame = Time.frameCount;
            action();
        }
    }
}
