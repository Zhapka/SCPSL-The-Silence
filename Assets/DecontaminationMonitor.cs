using System.Collections.Generic;
using MEC;
using UnityEngine;
using UnityEngine.UI;

public class DecontaminationMonitor : MonoBehaviour
{
    public GameObject canvasDownloading;
    public GameObject canvasCountDown;
    public Text timerText;
    public RectTransform manRunning;

    private CoroutineHandle animHandle;
    private float totalDecontTime = 704.4f;

    public void UpdateMonitorState(bool isCountdown, float currentTime)
    {
        canvasDownloading.SetActive(!isCountdown);
        canvasCountDown.SetActive(isCountdown);

        if (isCountdown)
        {
            Timing.KillCoroutines(animHandle);
            animHandle = Timing.RunCoroutine(_Animate(totalDecontTime - currentTime), Segment.Update);
        }
    }

    private void Update()
    {
        if (canvasCountDown != null && canvasCountDown.activeSelf && timerText != null)
        {
            // Здесь мы ищем главный объект очистки на сцене
            var main = FindObjectOfType<DecontaminationLCZ>();
            if (main != null)
            {
                float left = Mathf.Max(0, totalDecontTime - main.time);
                timerText.text = System.TimeSpan.FromSeconds(left).ToString(@"mm\:ss\:ff");
            }
        }
    }

    private IEnumerator<float> _Animate(float duration)
    {
        if (manRunning == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Timing.DeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            manRunning.anchoredPosition3D = new Vector3(Mathf.Lerp(919f, -586f, t), 0, 0);
            yield return Timing.WaitForOneFrame;
        }
    }
}