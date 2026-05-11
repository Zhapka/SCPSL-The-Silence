using System;
using System.Collections.Generic;
using MEC;
using UnityEngine;
using UnityEngine.UI;

public class SubtitleManager : MonoBehaviour
{
    public static SubtitleManager Instance { get; private set; }

    [Serializable]
    public class EventSubtitle
    {
        public string eventID;
        public float totalDuration = 10f;
        [TextArea(3, 8)]
        public string subtitleText;
    }

    [Header("UI Reference")]
    [SerializeField] private Text uiSubtitleText;

    [Header("Settings")]
    [SerializeField] private float typeSpeed = 0.03f;
    [SerializeField] private float asteriskPauseDuration = 0.5f;

    [Header("Events Database")]
    [SerializeField] private List<EventSubtitle> eventDatabase = new List<EventSubtitle>();

    private CoroutineHandle activeCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (uiSubtitleText == null) uiSubtitleText = GetComponent<Text>();
        if (uiSubtitleText != null) uiSubtitleText.text = "";
    }

    // Обычный вызов для простых фраз (дезинфекция)
    public void TriggerSubtitleEvent(string id)
    {
        TriggerSubtitleEventFormatted(id, null);
    }

    // Продвинутый вызов с подстановкой аргументов (для МОГ)
    public void TriggerSubtitleEventFormatted(string id, params object[] args)
    {
        if (uiSubtitleText == null || string.IsNullOrEmpty(id)) return;

        for (int i = 0; i < eventDatabase.Count; i++)
        {
            if (eventDatabase[i].eventID == id)
            {
                string rawText = eventDatabase[i].subtitleText;

                // Если переданы аргументы, подставляем их в шаблон {0}, {1} и т.д.
                if (args != null && args.Length > 0)
                {
                    try { rawText = string.Format(rawText, args); }
                    catch { Debug.LogError($"[SubtitleManager] Ошибка форматирования для события {id}"); }
                }

                Timing.KillCoroutines(activeCoroutine);
                activeCoroutine = Timing.RunCoroutine(_ShowSubtitles(rawText, eventDatabase[i].totalDuration));
                break;
            }
        }
    }

    private IEnumerator<float> _ShowSubtitles(string fullText, float clipDuration)
    {
        if (string.IsNullOrEmpty(fullText)) yield break;

        string[] screens = fullText.Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
        float timePerScreen = clipDuration / screens.Length;

        foreach (string rawScreen in screens)
        {
            string screenText = rawScreen.Trim();
            uiSubtitleText.text = "";

            float timeSpent = 0f;

            for (int i = 0; i < screenText.Length; i++)
            {
                char currentChar = screenText[i];

                if (currentChar == '*')
                {
                    timeSpent += asteriskPauseDuration;
                    yield return Timing.WaitForSeconds(asteriskPauseDuration);
                    continue;
                }

                uiSubtitleText.text += currentChar;
                timeSpent += typeSpeed;
                yield return Timing.WaitForSeconds(typeSpeed);
            }

            float timeLeft = timePerScreen - timeSpent;
            if (timeLeft > 0f) yield return Timing.WaitForSeconds(timeLeft);
        }

        uiSubtitleText.text = "";
    }
}
