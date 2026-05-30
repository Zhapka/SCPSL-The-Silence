using UnityEngine;

[RequireComponent(typeof(AudioLowPassFilter))]
[RequireComponent(typeof(AudioSource))]
public class VolumetricAudio : MonoBehaviour
{
    private Transform playerCamera;
    private AudioLowPassFilter lowPass;
    private AudioSource audioSource;

    [Header("Wall Occlusion Settings")]
    public float openFrequency = 22000f;
    public float muffledFrequency = 600f;
    public float openVolume = 1f;
    public float muffledVolume = 0.4f;

    [Header("Fade Speed")]
    public float fadeSpeed = 8f;

    private float targetFrequency;
    private float targetVolume;
    private readonly Vector3[] offsets = { Vector3.zero, new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f) };

    void Start()
    {
        lowPass = GetComponent<AudioLowPassFilter>();
        audioSource = GetComponent<AudioSource>();

        targetFrequency = openFrequency;
        targetVolume = openVolume;

        AudioListener listener = FindObjectOfType<AudioListener>();
        if (listener != null)
        {
            playerCamera = listener.transform;
        }
        else
        {
            Debug.LogError("[VolumetricAudio] Audio Listener not found in the scene!");
        }
    }

    void Update()
    {
        if (playerCamera == null) return;

        CheckOcclusion();

        float deltaTimeFade = Time.deltaTime * fadeSpeed;
        lowPass.cutoffFrequency = Mathf.Lerp(lowPass.cutoffFrequency, targetFrequency, deltaTimeFade);
        audioSource.volume = Mathf.Lerp(audioSource.volume, targetVolume, deltaTimeFade);
    }

    void CheckOcclusion()
    {
        Vector3 cameraPos = playerCamera.position;
        Vector3 soundPos = transform.position;
        Vector3 toCamera = cameraPos - soundPos;
        float sqrDistance = toCamera.sqrMagnitude;
        float distance = Mathf.Sqrt(sqrDistance);

        int hitsCount = 0;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector3 targetPos = cameraPos + offsets[i];
            Vector3 dir = targetPos - soundPos;

            if (Physics.Raycast(soundPos, dir, out RaycastHit hit, distance))
            {
                if (hit.transform != playerCamera)
                {
                    hitsCount++;
                }
            }
        }

        if (hitsCount == offsets.Length)
        {
            targetFrequency = muffledFrequency;
            targetVolume = muffledVolume;
        }
        else if (hitsCount > 0)
        {
            targetFrequency = Mathf.Lerp(openFrequency, muffledFrequency, 0.4f);
            targetVolume = Mathf.Lerp(openVolume, muffledVolume, 0.4f);
        }
        else
        {
            targetFrequency = openFrequency;
            targetVolume = openVolume;
        }
    }
}
