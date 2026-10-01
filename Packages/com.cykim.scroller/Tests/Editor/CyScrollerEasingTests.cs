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
    }
}
