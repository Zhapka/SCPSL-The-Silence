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
    [SerializeField] private float creditsFadeSpeed = 5f; // Faster fade out for credits
    [SerializeField] private string connectionObjectName = "ConnectionInfo";
    [SerializeField] private string creditsObjectName = "Credits";

    private bool isIntenseActive = false;
    private float targetNormalVolume = 1f;
    private float targetIntenseVolume = 0f;
    private float maxVolume = 1f;

    private float lastNormalTime = 0f;
    private bool loopsStarted = false;

    private GameObject connectionGameObject;
    private GameObject creditsGameObject;

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

        StartCoroutine(WaitForTargetObjects());
    }

    private void Update()
    {
        bool isConnectionActive = (connectionGameObject != null && connectionGameObject.activeInHierarchy);
        bool isCreditsActive = (creditsGameObject != null && creditsGameObject.activeInHierarchy);

        float currentFadeSpeed = fadeSpeed;

        if (isCreditsActive)
        {
            // If credits are open, force mute all background music quickly
            targetNormalVolume = 0f;
            targetIntenseVolume = 0f;
            currentFadeSpeed = creditsFadeSpeed;
        }
        else
        {
            // Standard dynamic music logic when credits are closed
            if (isConnectionActive)
            {
                targetNormalVolume = 0f;
                targetIntenseVolume = maxVolume;
                isIntenseActive = true;
            }
            else
            {
                targetNormalVolume = maxVolume;
                targetIntenseVolume = 0f;
                isIntenseActive = false;
            }
        }

        FadeVolume(loopNormalSource, targetNormalVolume, currentFadeSpeed);
        FadeVolume(loopIntenseSource, targetIntenseVolume, currentFadeSpeed);
        FadeVolume(introSource, isCreditsActive ? 0f : maxVolume, currentFadeSpeed);

        SynchronizeTracks();
    }

    private IEnumerator WaitForTargetObjects()
    {
        while (connectionGameObject == null || creditsGameObject == null)
        {
            Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform t in allTransforms)
            {
                if (connectionGameObject == null && t.gameObject.name == connectionObjectName)
                {
                    connectionGameObject = t.gameObject;
                    Debug.Log("[Music] ConnectionInfo GameObject found!");
                }

                if (creditsGameObject == null && t.gameObject.name == creditsObjectName)
                {
                    creditsGameObject = t.gameObject;
                    Debug.Log("[Music] Credits GameObject found!");
                }
            }
            yield return new WaitForSeconds(0.2f);
        }
    }

    private void FadeVolume(AudioSource source, float targetVolume, float speed)
    {
        if (source.volume != targetVolume)
        {
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, speed * Time.deltaTime);
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
}