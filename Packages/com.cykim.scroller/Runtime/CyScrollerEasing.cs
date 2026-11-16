using UnityEngine;

namespace CyKim.Scroller
{
    /// <summary>
    /// <see cref="ScrollEase"/> 곡선 계산. 입력 0→0, 1→1을 만족한다 (Back·Elastic은 중간에 범위를 넘는다).
    /// 할당이 없으므로 매 프레임 호출해도 된다.
    /// </summary>
    public static class CyScrollerEasing
    {
        private const float BACK_C1 = 1.70158f;
        private const float BACK_C2 = BACK_C1 * 1.525f;
        private const float BACK_C3 = BACK_C1 + 1f;
        private const float ELASTIC_C4 = 2f * Mathf.PI / 3f;
        private const float ELASTIC_C5 = 2f * Mathf.PI / 4.5f;
        private const float BOUNCE_N1 = 7.5625f;
        private const float BOUNCE_D1 = 2.75f;

        /// <param name="type">곡선 종류.</param>
        /// <param name="t">진행률. 0~1로 잘린다.</param>
        /// <param name="customCurve"><see cref="ScrollEase.Custom"/>일 때 쓰는 곡선. null이면 선형.</param>
        public static float Evaluate(ScrollEase type, float t, AnimationCurve customCurve = null)
        {
            t = Mathf.Clamp01(t);

            switch (type)
            {
                case ScrollEase.Instant: return 1f;
                case ScrollEase.Linear: return t;

                case ScrollEase.EaseInSine: return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case ScrollEase.EaseOutSine: return Mathf.Sin(t * Mathf.PI * 0.5f);
                case ScrollEase.EaseInOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;

                case ScrollEase.EaseInQuad: return t * t;
                case ScrollEase.EaseOutQuad: return 1f - Pow(1f - t, 2);
                case ScrollEase.EaseInOutQuad: return t < 0.5f ? 2f * t * t : 1f - Pow(-2f * t + 2f, 2) * 0.5f;

                case ScrollEase.EaseInCubic: return t * t * t;
                case ScrollEase.EaseOutCubic: return 1f - Pow(1f - t, 3);
                case ScrollEase.EaseInOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Pow(-2f * t + 2f, 3) * 0.5f;

                case ScrollEase.EaseInQuart: return Pow(t, 4);
                case ScrollEase.EaseOutQuart: return 1f - Pow(1f - t, 4);
                case ScrollEase.EaseInOutQuart: return t < 0.5f ? 8f * Pow(t, 4) : 1f - Pow(-2f * t + 2f, 4) * 0.5f;

                case ScrollEase.EaseInQuint: return Pow(t, 5);
                case ScrollEase.EaseOutQuint: return 1f - Pow(1f - t, 5);
                case ScrollEase.EaseInOutQuint: return t < 0.5f ? 16f * Pow(t, 5) : 1f - Pow(-2f * t + 2f, 5) * 0.5f;

                case ScrollEase.EaseInExpo: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case ScrollEase.EaseOutExpo: return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case ScrollEase.EaseInOutExpo:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f
                        ? Mathf.Pow(2f, 20f * t - 10f) * 0.5f
                        : (2f - Mathf.Pow(2f, -20f * t + 10f)) * 0.5f;

                case ScrollEase.EaseInCirc: return 1f - Mathf.Sqrt(1f - t * t);
                case ScrollEase.EaseOutCirc: return Mathf.Sqrt(1f - Pow(t - 1f, 2));
                case ScrollEase.EaseInOutCirc:
                    return t < 0.5f
                        ? (1f - Mathf.Sqrt(1f - Pow(2f * t, 2))) * 0.5f
                        : (Mathf.Sqrt(1f - Pow(-2f * t + 2f, 2)) + 1f) * 0.5f;

                case ScrollEase.EaseInBack: return BACK_C3 * t * t * t - BACK_C1 * t * t;
                case ScrollEase.EaseOutBack: return 1f + BACK_C3 * Pow(t - 1f, 3) + BACK_C1 * Pow(t - 1f, 2);
                case ScrollEase.EaseInOutBack:
                    return t < 0.5f
                        ? Pow(2f * t, 2) * ((BACK_C2 + 1f) * 2f * t - BACK_C2) * 0.5f
                        : (Pow(2f * t - 2f, 2) * ((BACK_C2 + 1f) * (2f * t - 2f) + BACK_C2) + 2f) * 0.5f;

                case ScrollEase.EaseInElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((10f * t - 10.75f) * ELASTIC_C4);
                case ScrollEase.EaseOutElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((10f * t - 0.75f) * ELASTIC_C4) + 1f;
                case ScrollEase.EaseInOutElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f
                        ? -(Mathf.Pow(2f, 20f * t - 10f) * Mathf.Sin((20f * t - 11.125f) * ELASTIC_C5)) * 0.5f
                        : Mathf.Pow(2f, -20f * t + 10f) * Mathf.Sin((20f * t - 11.125f) * ELASTIC_C5) * 0.5f + 1f;

                case ScrollEase.EaseInBounce: return 1f - BounceOut(1f - t);
                case ScrollEase.EaseOutBounce: return BounceOut(t);
                case ScrollEase.EaseInOutBounce:
                    return t < 0.5f
                        ? (1f - BounceOut(1f - 2f * t)) * 0.5f
                        : (1f + BounceOut(2f * t - 1f)) * 0.5f;

                case ScrollEase.Custom: return customCurve != null ? customCurve.Evaluate(t) : t;

                default: return t;
            }
        }

        /// <summary>
        /// 곡선이 1을 넘거나 되돌아가지 않고 끊김 없이 1에 닿는지. 트윈 중 목표가 바뀔 때 남은 거리를 곡선의 남은 진행에 나눠 실어도 되는지 판단한다.
        /// Back·Elastic·Bounce(되돌아옴), EaseOutExpo·EaseInOutExpo(1 직전에서 1로 뜀), Custom(알 수 없음)은 false.
        /// </summary>
        internal static bool ConvergesMonotonically(ScrollEase type)
        {
            switch (type)
            {
                case ScrollEase.Linear:
                case ScrollEase.EaseInSine:
                case ScrollEase.EaseOutSine:
                case ScrollEase.EaseInOutSine:
                case ScrollEase.EaseInQuad:
                case ScrollEase.EaseOutQuad:
                case ScrollEase.EaseInOutQuad:
                case ScrollEase.EaseInCubic:
                case ScrollEase.EaseOutCubic:
                case ScrollEase.EaseInOutCubic:
                case ScrollEase.EaseInQuart:
                case ScrollEase.EaseOutQuart:
                case ScrollEase.EaseInOutQuart:
                case ScrollEase.EaseInQuint:
                case ScrollEase.EaseOutQuint:
                case ScrollEase.EaseInOutQuint:
                case ScrollEase.EaseInExpo:
                case ScrollEase.EaseInCirc:
                case ScrollEase.EaseOutCirc:
                case ScrollEase.EaseInOutCirc:
                    return true;
                default:
                    return false;
            }
        }

        private static float BounceOut(float t)
        {
            if (t < 1f / BOUNCE_D1)
            {
                return BOUNCE_N1 * t * t;
            }

            if (t < 2f / BOUNCE_D1)
            {
                t -= 1.5f / BOUNCE_D1;
                return BOUNCE_N1 * t * t + 0.75f;
            }

            if (t < 2.5f / BOUNCE_D1)
            {
                t -= 2.25f / BOUNCE_D1;
                return BOUNCE_N1 * t * t + 0.9375f;
            }

            t -= 2.625f / BOUNCE_D1;
            return BOUNCE_N1 * t * t + 0.984375f;
        }

        private static float Pow(float value, int power)
        {
            float result = 1f;
            for (int i = 0; i < power; i++)
            {
                result *= value;
            }

            return result;
        }
    }
}
