using UnityEngine;
using UnityEngine.Networking;

public class RoundStartSound : NetworkBehaviour
{
    [Header("Components")]
    public AudioSource audioSource;
    public AudioClip eventClip;

    [Header("Separate Timing Settings")]
    [Tooltip("Delay in seconds after the round starts before the SOUND plays.")]
    public float soundStartDelay = 5f;

    [Tooltip("Delay in seconds after the round starts before the lights FLICKER and turn OFF.")]
    public float lightStartDelay = 8f;

    private bool hasSoundStarted = false;
    private bool hasLightStarted = false;
    private bool initializedBrightLights = false;
    private float timer = 0f;
    private CharacterClassManager ccm;

    private void Start()
    {
        ccm = FindObjectOfType<CharacterClassManager>();
    }

    private void Update()
    {
        // Ensure this logic executes only on the server side
        if (!NetworkServer.active)
            return;

        // Check if the round has successfully started
        if (ccm != null && ccm.roundStarted)
        {
            // Trigger bright phase instantly at 0:00 of the round start
            if (!initializedBrightLights)
            {
                initializedBrightLights = true;
                RpcMakeLightsBright();
            }

            timer += Time.deltaTime;

            // Trigger sound playback at its own custom delay
            if (!hasSoundStarted && timer >= soundStartDelay)
            {
                hasSoundStarted = true;
                RpcPlaySound();
            }

            // Trigger the darkness drop exactly at lightStartDelay (8 seconds)
            if (!hasLightStarted && timer >= lightStartDelay)
            {
                hasLightStarted = true;
                RpcTriggerLightsOff();
            }
        }
    }

    [ClientRpc]
    private void RpcMakeLightsBright()
    {
        RoundStartLightController globalController = FindObjectOfType<RoundStartLightController>();
        if (globalController != null)
        {
            globalController.TurnOnBrightLights();
        }
    }

    [ClientRpc]
    private void RpcPlaySound()
    {
        if (audioSource != null && eventClip != null)
        {
            audioSource.clip = eventClip;
            audioSource.Play();
        }
    }

    [ClientRpc]
    private void RpcTriggerLightsOff()
    {
        RoundStartLightController globalController = FindObjectOfType<RoundStartLightController>();
        if (globalController != null)
        {
            globalController.TriggerFlickerEvent();
        }
    }
}