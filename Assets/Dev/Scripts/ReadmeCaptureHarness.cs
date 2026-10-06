using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller.Dev
{
    /// <summary>
    /// README 캡처 도구. Screen Space 캔버스를 RenderTexture 카메라로 돌려 Game 뷰 크기와 상관없이 고정 해상도로 찍는다.
    /// 1) 정지 화면 한 장(still), 2) 버튼 라벨 순서대로 누르며 일정 간격 프레임(frames/)을 PNG로 저장한다.
    /// Play 중 <see cref="Begin"/>으로 시작하고 <see cref="IsRunning"/>이 false가 되면 Play를 끝낸다 (캔버스를 카메라 모드로 바꾼 채 두므로 캡처 전용 세션에서만 쓴다).
    /// </summary>
    public class ReadmeCaptureHarness : MonoBehaviour
    {
        public static bool IsRunning;
        public static string Status = "idle";

        private Canvas _canvas;
        private string _outputDir;
        private string[] _steps;
        private int _stillWidth;
        private int _stillHeight;
        private int _width;
        private int _height;
        private float _fps;
        private float _settleSeconds;

        public static ReadmeCaptureHarness Begin(Canvas canvas, string outputDir, string[] steps,
            int stillWidth, int stillHeight, int width, int height, float fps, float settleSeconds)
        {
            Application.runInBackground = true;
            var go = new GameObject("ReadmeCaptureHarness");
            var harness = go.AddComponent<ReadmeCaptureHarness>();
            harness._canvas = canvas;
            harness._outputDir = outputDir;
            harness._steps = steps;
            harness._stillWidth = stillWidth;
            harness._stillHeight = stillHeight;
            harness._width = width;
            harness._height = height;
            harness._fps = fps;
            harness._settleSeconds = settleSeconds;
            IsRunning = true;
            Status = "starting";
            harness.StartCoroutine(harness.Run());
            return harness;
        }

        private IEnumerator Run()
        {
            Directory.CreateDirectory(_outputDir);
            string framesDir = Path.Combine(_outputDir, "frames");
            Directory.CreateDirectory(framesDir);

            var camGo = new GameObject("ReadmeCaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
            cam.orthographic = true;

            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = 10f;

            // 1) still
            var stillRt = new RenderTexture(_stillWidth, _stillHeight, 24);
            cam.targetTexture = stillRt;
            Status = "settling still";
            yield return WaitRealtime(_settleSeconds);
            yield return new WaitForEndOfFrame();
            Save(stillRt, Path.Combine(_outputDir, "still.png"));

            // 2) frames
            var rt = new RenderTexture(_width, _height, 24);
            cam.targetTexture = rt;
            stillRt.Release();
            Status = "settling frames";
            yield return WaitRealtime(0.5f);

            float interval = 1f / _fps;
            int frame = 0;
            float next = Time.realtimeSinceStartup;
            for (int s = 0; s < _steps.Length; s++)
            {
                string[] parts = _steps[s].Split('|');
                string label = parts[0];
                float seconds = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                if (label.Length > 0 && label != "-")
                {
                    if (!Click(label))
                    {
                        Debug.LogWarning($"[ReadmeCaptureHarness] 버튼을 찾지 못했습니다: {label}");
                    }
                }

                Status = $"step {s + 1}/{_steps.Length} {label}";
                float end = Time.realtimeSinceStartup + seconds;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return new WaitForEndOfFrame();
                    if (Time.realtimeSinceStartup >= next)
                    {
                        Save(rt, Path.Combine(framesDir, $"f{frame:0000}.png"));
                        frame++;
                        next += interval;
                        if (Time.realtimeSinceStartup > next + interval)
                        {
                            // 저장이 느려 밀렸으면 따라잡지 말고 현재 시각부터 다시 센다(프레임 시각 기록은 파일 수로 대신한다).
                            next = Time.realtimeSinceStartup + interval;
                        }
                    }

                    yield return null;
                }
            }

            Status = $"done frames={frame}";
            IsRunning = false;
        }

        private bool Click(string label)
        {
            Button[] buttons = _canvas.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Text text = buttons[i].GetComponentInChildren<Text>(true);
                if (text != null && text.text == label)
                {
                    buttons[i].onClick.Invoke();
                    return true;
                }
            }

            return false;
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        private static void Save(RenderTexture rt, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
