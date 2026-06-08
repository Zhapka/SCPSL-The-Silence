using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using MEC;

public class MeshBlinking : NetworkBehaviour
{
    public Renderer targetRenderer;
    public int materialIndex = 0;
    public Material closedEyesMaterial;
    public float blinkDuration = 0.15f;
    public float minTimeBetweenBlinks = 2.5f;
    public float maxTimeBetweenBlinks = 5f;

    private CoroutineHandle _blinkRoutine;
    private Material _originalMaterial;
    private Material[] _materialsCache;

    private void Start()
    {
        if (targetRenderer != null && targetRenderer.sharedMaterials.Length > materialIndex)
        {
            _materialsCache = targetRenderer.sharedMaterials;
            _originalMaterial = _materialsCache[materialIndex];
        }

        if (isServer)
        {
            _blinkRoutine = Timing.RunCoroutine(_BlinkLoop(), Segment.Update);
        }
        else if (!NetworkServer.active && !NetworkClient.active)
        {
            _blinkRoutine = Timing.RunCoroutine(_BlinkLoopLocal(), Segment.Update);
        }
    }

    private void OnDestroy()
    {
        Timing.KillCoroutines(_blinkRoutine);
    }

    private IEnumerator<float> _BlinkLoop()
    {
        while (true)
        {
            float delay = Random.Range(minTimeBetweenBlinks, maxTimeBetweenBlinks);
            yield return Timing.WaitForSeconds(delay);

            RpcDoBlink();
        }
    }

    private IEnumerator<float> _BlinkLoopLocal()
    {
        while (true)
        {
            float delay = Random.Range(minTimeBetweenBlinks, maxTimeBetweenBlinks);
            yield return Timing.WaitForSeconds(delay);

            Timing.RunCoroutine(_AnimateBlink(), Segment.Update);
        }
    }

    [ClientRpc]
    private void RpcDoBlink()
    {
        Timing.RunCoroutine(_AnimateBlink(), Segment.Update);
    }

    private IEnumerator<float> _AnimateBlink()
    {
        if (targetRenderer == null || closedEyesMaterial == null || _originalMaterial == null) yield break;

        _materialsCache[materialIndex] = closedEyesMaterial;
        targetRenderer.sharedMaterials = _materialsCache;

        yield return Timing.WaitForSeconds(blinkDuration);

        _materialsCache[materialIndex] = _originalMaterial;
        targetRenderer.sharedMaterials = _materialsCache;
    }
}
