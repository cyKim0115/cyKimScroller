using System;
using NUnit.Framework;
using UnityEngine;

namespace CyKim.Scroller.Tests
{
    public class CyScrollerEasingTests
    {
        private const float EPSILON = 0.001f;

        private static TweenType[] CurveTypes()
        {
            var values = (TweenType[])Enum.GetValues(typeof(TweenType));
            return Array.FindAll(values, t => t != TweenType.Immediate && t != TweenType.Custom);
        }

        [TestCaseSource(nameof(CurveTypes))]
        public void Curve_StartsAtZeroAndEndsAtOne(TweenType type)
        {
            Assert.AreEqual(0f, CyScrollerEasing.Evaluate(type, 0f), EPSILON, $"{type} f(0)");
            Assert.AreEqual(1f, CyScrollerEasing.Evaluate(type, 1f), EPSILON, $"{type} f(1)");
        }

        [TestCaseSource(nameof(CurveTypes))]
        public void Curve_ClampsInput(TweenType type)
        {
            Assert.AreEqual(CyScrollerEasing.Evaluate(type, 0f), CyScrollerEasing.Evaluate(type, -3f), EPSILON);
            Assert.AreEqual(CyScrollerEasing.Evaluate(type, 1f), CyScrollerEasing.Evaluate(type, 5f), EPSILON);
        }

        [Test]
        public void InOutCurves_PassThroughHalf()
        {
            Assert.AreEqual(0.5f, CyScrollerEasing.Evaluate(TweenType.EaseInOutSine, 0.5f), EPSILON);
            Assert.AreEqual(0.5f, CyScrollerEasing.Evaluate(TweenType.EaseInOutQuad, 0.5f), EPSILON);
            Assert.AreEqual(0.5f, CyScrollerEasing.Evaluate(TweenType.EaseInOutCubic, 0.5f), EPSILON);
            Assert.AreEqual(0.5f, CyScrollerEasing.Evaluate(TweenType.EaseInOutBounce, 0.5f), EPSILON);
        }

        [Test]
        public void Immediate_IsAlwaysDone()
        {
            Assert.AreEqual(1f, CyScrollerEasing.Evaluate(TweenType.Immediate, 0f), EPSILON);
        }

        [Test]
        public void Custom_UsesCurveOrFallsBackToLinear()
        {
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 2f));
            Assert.AreEqual(2f, CyScrollerEasing.Evaluate(TweenType.Custom, 1f, curve), EPSILON);
            Assert.AreEqual(0.25f, CyScrollerEasing.Evaluate(TweenType.Custom, 0.25f, null), EPSILON);
        }

        [Test]
        public void OutBack_Overshoots()
        {
            float max = 0f;
            for (int i = 0; i <= 100; i++)
            {
                max = Mathf.Max(max, CyScrollerEasing.Evaluate(TweenType.EaseOutBack, i / 100f));
            }

            Assert.Greater(max, 1f);
        }

        [TestCaseSource(nameof(CurveTypes))]
        public void ConvergesMonotonically_MatchesSampledCurve(TweenType type)
        {
            const int SAMPLES = 2000;
            const float SAMPLE_TOLERANCE = 0.000001f;

            // 0 → 1 사이에서 줄거나 1을 넘으면 되돌아오는 곡선이다.
            bool monotonic = true;
            float previous = CyScrollerEasing.Evaluate(type, 0f);
            for (int i = 1; i <= SAMPLES && monotonic; i++)
            {
                float value = CyScrollerEasing.Evaluate(type, (float)i / SAMPLES);
                monotonic = value >= previous - SAMPLE_TOLERANCE && value <= 1f + SAMPLE_TOLERANCE;
                previous = value;
            }

            // 1 직전에서 끝값과의 차이가 계속 줄어야 끝에서 뛰지 않는다 (EaseOutExpo는 약 0.001에 머물다가 1로 뛴다).
            float near = 1f - CyScrollerEasing.Evaluate(type, 1f - 0.001f);
            float nearer = 1f - CyScrollerEasing.Evaluate(type, 1f - 0.00001f);
            bool continuousEnd = nearer <= near * 0.5f;

            Assert.AreEqual(monotonic && continuousEnd, CyScrollerEasing.ConvergesMonotonically(type), type.ToString());
        }

        [Test]
        public void ConvergesMonotonically_IsFalseForCustom()
        {
            // 곡선 모양을 알 수 없으므로 남은 거리를 곡선에 싣지 않는다.
            Assert.IsFalse(CyScrollerEasing.ConvergesMonotonically(TweenType.Custom));
        }
    }
}
