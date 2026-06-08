using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoundStartLightController : MonoBehaviour
{
    [Header("Intensity Settings")]
    public float brightIntensity = 1.5f;

    [Header("Timing Settings")]
    [Tooltip("How long the lights will stay turned off/flickering after the bright phase.")]
    public float flickerDuration = 4f;

    private Dictionary<FlickerableLight, float> originalIntensities = new Dictionary<FlickerableLight, float>();
    private bool isBrightPhaseActive = false;

    /// <summary>
    /// Instantly saves original intensities and forces all lights to maximum brightness.
    /// </summary>
    public void TurnOnBrightLights()
    {
        if (isBrightPhaseActive) return;
        isBrightPhaseActive = true;

        FlickerableLight[] allFlickers = FindObjectsOfType<FlickerableLight>();
        originalIntensities.Clear();

        foreach (var flicker in allFlickers)
        {
            Light l = flicker.GetComponentInChildren<Light>();
            if (l != null)
            {
                // Save original scene intensity
                originalIntensities[flicker] = l.intensity;

                // Override light intensity directly
                l.intensity = brightIntensity;
            }
        }
    }

    /// <summary>
    /// Triggers the flicker event (darkness drop) and cleans up custom overrides.
    /// </summary>
    public void TriggerFlickerEvent()
    {
        StartCoroutine(FlickerSequence(flickerDuration));
    }

    private IEnumerator FlickerSequence(float flickerTime)
    {
        FlickerableLight[] allFlickers = FindObjectsOfType<FlickerableLight>();

        // Step 1: Force FlickerableLight to start its own animation sequence (Disable -> Enable)
        foreach (var flicker in allFlickers)
        {
            flicker.EnableFlickering(flickerTime);
        }

        // Wait for the darkness/flicker animation phase to finish
        yield return new WaitForSeconds(flickerTime);

        // Step 2: Restore original scene intensities exactly as they were configured in Unity
        foreach (var flicker in allFlickers)
        {
            Light l = flicker.GetComponentInChildren<Light>();
            if (l != null && originalIntensities.ContainsKey(flicker))
            {
                l.intensity = originalIntensities[flicker];
            }
        }

        isBrightPhaseActive = false;
    }
}