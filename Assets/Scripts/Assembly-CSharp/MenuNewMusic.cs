using System;
using System.Collections;
using UnityEngine;

public class MenuNewMusic : MonoBehaviour
{
    [Header("Audio Sources")]
    [SerializeField] private AudioSource introSource;
    [SerializeField] private AudioSource loopNormalSource;
    [SerializeField] private AudioSource loopIntenseSource;

    [Header("Audio Clips")]
    [SerializeField] private AudioClip startClip;
    [SerializeField] private AudioClip loopNormalClip;
    [SerializeField] private AudioClip loopIntenseClip;

    [Header("Settings")]
    [SerializeField] private float fadeSpeed = 2f;

    private bool isIntenseActive = false;
    private float targetNormalVolume = 1f;
    private float targetIntenseVolume = 0f;
    private float maxVolume = 1f;

    private float lastNormalTime = 0f;
    private bool loopsStarted = false;

    private GameObject targetGameObject;
    private string targetComponentName = "ConnectionInfo";

    private void Start()
    {
        if (introSource == null || loopNormalSource == null || loopIntenseSource == null)
        {
            Debug.LogWarning("Please assign all 3 Audio Sources in the Inspector!");
            return;
        }

        if (startClip == null || loopNormalClip == null || loopIntenseClip == null)
        {
            Debug.LogWarning("Please assign all 3 Audio Clips in the Inspector!");
            return;
        }

        ConfigureAudioSources();
        PlayIntroAndScheduleLoops();

        StartCoroutine(WaitForConnectionInfo());
    }

    private void Update()
    {
        bool isObjectActive = false;

        if (targetGameObject != null)
        {
            isObjectActive = targetGameObject.activeInHierarchy;
        }

        bool shouldBeIntense = (targetGameObject != null && isObjectActive);

        if (shouldBeIntense != isIntenseActive)
        {
            if (shouldBeIntense)
                ActivateIntenseMusic();
            else
                DeactivateIntenseMusic();
        }

        FadeVolume(loopNormalSource, targetNormalVolume);
        FadeVolume(loopIntenseSource, targetIntenseVolume);

        SynchronizeTracks();
    }

    private IEnumerator WaitForConnectionInfo()
    {
        while (targetGameObject == null)
        {
            Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform t in allTransforms)
            {
                if (t.gameObject.name == "ConnectionInfo")
                {
                    targetGameObject = t.gameObject;
                    Debug.Log("[Music] ConnectionInfo GameObject found via deep scan!");
                    break;
                }
            }
            yield return new WaitForSeconds(0.2f);
        }
    }

    private void FadeVolume(AudioSource source, float targetVolume)
    {
        if (source.volume != targetVolume)
        {
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, fadeSpeed * Time.deltaTime);
        }
    }

    private void ConfigureAudioSources()
    {
        introSource.clip = startClip;
        introSource.loop = false;
        introSource.playOnAwake = false;
        introSource.volume = maxVolume;

        loopNormalSource.clip = loopNormalClip;
        loopNormalSource.loop = true;
        loopNormalSource.playOnAwake = false;
        loopNormalSource.volume = maxVolume;

        loopIntenseSource.clip = loopIntenseClip;
        loopIntenseSource.loop = true;
        loopIntenseSource.playOnAwake = false;
        loopIntenseSource.volume = 0f;

        targetNormalVolume = maxVolume;
        targetIntenseVolume = 0f;
    }

    private void PlayIntroAndScheduleLoops()
    {
        double duration = (double)startClip.samples / startClip.frequency;
        double startTime = AudioSettings.dspTime + 0.1;

        introSource.Play();

        double loopStartTime = startTime + duration;
        loopNormalSource.PlayScheduled(loopStartTime);
        loopIntenseSource.PlayScheduled(loopStartTime);

        StartCoroutine(SetLoopsStartedActive((float)(duration + 0.1)));
    }

    private IEnumerator SetLoopsStartedActive(float delay)
    {
        yield return new WaitForSeconds(delay);
        loopsStarted = true;
    }

    private void SynchronizeTracks()
    {
        if (!loopsStarted || !loopNormalSource.isPlaying) return;

        if (loopNormalSource.time < lastNormalTime)
        {
            loopIntenseSource.time = loopNormalSource.time;
        }

        lastNormalTime = loopNormalSource.time;
    }

    public void ActivateIntenseMusic()
    {
        Debug.Log("[Music] Switching to Intense version.");
        isIntenseActive = true;
        targetNormalVolume = 0f;
        targetIntenseVolume = maxVolume;
    }

    public void DeactivateIntenseMusic()
    {
        Debug.Log("[Music] Switching back to Normal version.");
        isIntenseActive = false;
        targetNormalVolume = maxVolume;
        targetIntenseVolume = 0f;
    }
}