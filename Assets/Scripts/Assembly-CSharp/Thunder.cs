using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class Thunder : MonoBehaviour
{
    private AudioSource audioSource;

    [Header("Light")]
    public Light pointLight;

    [Header("Min, Max")]
    public float minDelay = 10f;
    public float maxDelay = 30f;

    [Header("Duration")]
    public float flashDuration = 0.2f;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();

        // Гасим свет в начале игры
        if (pointLight != null) pointLight.enabled = false;

        StartCoroutine(PlaySoundRoutine());
    }

    IEnumerator PlaySoundRoutine()
    {
        while (true)
        {
            float randomWait = Random.Range(minDelay, maxDelay);
            yield return new WaitForSeconds(randomWait);

            if (Random.Range(0f, 1f) <= 0.2f)
            {
                if (!audioSource.isPlaying)
                {
                    audioSource.Play();
                    StartCoroutine(LightningFlashRoutine());
                }
            }
        }
    }

    IEnumerator LightningFlashRoutine()
    {
        if (pointLight == null) yield break;

        // Вспышка молнии
        pointLight.enabled = true;
        yield return new WaitForSeconds(flashDuration);
        pointLight.enabled = false;
    }
}
