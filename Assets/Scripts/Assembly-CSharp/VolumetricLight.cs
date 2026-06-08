using System;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Light))]
public class VolumetricLight : MonoBehaviour
{
    private Light _light;
    private Material _material;
    private CommandBuffer _commandBuffer;
    private CommandBuffer _cascadeShadowCommandBuffer;
    private Vector4[] _frustumCorners = new Vector4[4];
    private bool _reversedZ;
    private Camera _cachedCamera;

    private static readonly int PropCameraForward = Shader.PropertyToID("_CameraForward");
    private static readonly int PropSampleCount = Shader.PropertyToID("_SampleCount");
    private static readonly int PropNoiseVelocity = Shader.PropertyToID("_NoiseVelocity");
    private static readonly int PropNoiseData = Shader.PropertyToID("_NoiseData");
    private static readonly int PropMieG = Shader.PropertyToID("_MieG");
    private static readonly int PropVolumetricLight = Shader.PropertyToID("_VolumetricLight");
    private static readonly int PropCameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
    private static readonly int PropZTest = Shader.PropertyToID("_ZTest");
    private static readonly int PropHeightFog = Shader.PropertyToID("_HeightFog");
    private static readonly int PropWorldViewProj = Shader.PropertyToID("_WorldViewProj");
    private static readonly int PropWorldView = Shader.PropertyToID("_WorldView");
    private static readonly int PropLightPos = Shader.PropertyToID("_LightPos");
    private static readonly int PropLightColor = Shader.PropertyToID("_LightColor");
    private static readonly int PropMyLightMatrix0 = Shader.PropertyToID("_MyLightMatrix0");
    private static readonly int PropPlaneD = Shader.PropertyToID("_PlaneD");
    private static readonly int PropCosAngle = Shader.PropertyToID("_CosAngle");
    private static readonly int PropConeApex = Shader.PropertyToID("_ConeApex");
    private static readonly int PropConeAxis = Shader.PropertyToID("_ConeAxis");
    private static readonly int PropMyWorld2Shadow = Shader.PropertyToID("_MyWorld2Shadow");
    private static readonly int PropLightDir = Shader.PropertyToID("_LightDir");
    private static readonly int PropMaxRayLength = Shader.PropertyToID("_MaxRayLength");

    [Header("Performance & Quality")]
    [Range(4, 128)]
    public int sampleCount = 16;
    [Range(0f, 1000f)]
    public float maxRayLength = 200f;

    [Header("Atmosphere Settings")]
    [Range(0f, 1f)]
    public float scatteringCoefficient = 0.05f;
    [Range(0f, 0.1f)]
    public float extinctionCoefficient = 0.005f;
    [Range(0f, 1f)]
    public float skyboxExtinctionIntensity = 0.8f;
    [Range(0f, 0.99f)]
    public float mieAnisotropyG = 0.2f;

    [Header("Height Fog")]
    public bool enableHeightFog;
    [Range(0f, 2f)]
    public float heightFogScale = 0.05f;
    public float heightFogGroundLevel = 0f;

    [Header("3D Noise Texturing")]
    public bool enableNoise = true;
    public float noiseScale = 0.02f;
    [Range(0f, 5f)]
    public float noiseIntensity = 1.0f;
    [Range(0f, 1f)]
    public float noiseOffset = 0.2f;
    public Vector2 noiseScrollVelocity = new Vector2(1f, 1f);

    public Light Light => _light;
    public Material VolumetricMaterial => _material;

    public event Action<VolumetricLightRenderer, VolumetricLight, CommandBuffer, Matrix4x4> CustomRenderEvent;

    private void Start()
    {
        _cachedCamera = Camera.main;

        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 ||
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 ||
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal ||
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.PlayStation4 ||
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan ||
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.XboxOne)
        {
            _reversedZ = true;
        }

        _commandBuffer = new CommandBuffer { name = "Volumetric Light Buffer" };
        _cascadeShadowCommandBuffer = new CommandBuffer { name = "Volumetric Dir Shadow Buffer" };
        _cascadeShadowCommandBuffer.SetGlobalTexture("_CascadeShadowMapTexture", new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive));

        _light = GetComponent<Light>();
        if (_light.type == LightType.Directional)
        {
            _light.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, _commandBuffer);
            _light.AddCommandBuffer(LightEvent.AfterShadowMap, _cascadeShadowCommandBuffer);
        }
        else
        {
            _light.AddCommandBuffer(LightEvent.AfterShadowMap, _commandBuffer);
        }

        Shader shader = Shader.Find("Sandbox/VolumetricLight");
        if (shader == null)
        {
            throw new Exception("Critical Error: 'Sandbox/VolumetricLight' shader is missing. Add it to Always Included Shaders.");
        }
        _material = new Material(shader);
    }

    private void OnEnable()
    {
        VolumetricLightRenderer.PreRenderEvent += VolumetricLightRenderer_PreRenderEvent;
    }

    private void OnDisable()
    {
        VolumetricLightRenderer.PreRenderEvent -= VolumetricLightRenderer_PreRenderEvent;
    }

    public void OnDestroy()
    {
        if (_light != null)
        {
            _light.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, _commandBuffer);
            _light.RemoveCommandBuffer(LightEvent.AfterShadowMap, _cascadeShadowCommandBuffer);
            _light.RemoveCommandBuffer(LightEvent.AfterShadowMap, _commandBuffer);
        }

        if (_commandBuffer != null) _commandBuffer.Dispose();
        if (_cascadeShadowCommandBuffer != null) _cascadeShadowCommandBuffer.Dispose();
        if (_material != null) DestroyImmediate(_material);
    }

    private void VolumetricLightRenderer_PreRenderEvent(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
    {
        if (!_light.gameObject.activeInHierarchy || !_light.enabled) return;
        if (_cachedCamera == null) _cachedCamera = Camera.current ?? Camera.main;
        if (_cachedCamera == null) return;

        _material.SetVector(PropCameraForward, _cachedCamera.transform.forward);
        _material.SetInt(PropSampleCount, sampleCount);
        _material.SetVector(PropNoiseVelocity, new Vector4(noiseScrollVelocity.x, noiseScrollVelocity.y, 0f, 0f) * noiseScale);
        _material.SetVector(PropNoiseData, new Vector4(noiseScale, noiseIntensity, noiseOffset, 0f));
        _material.SetVector(PropMieG, new Vector4(1f - mieAnisotropyG * mieAnisotropyG, 1f + mieAnisotropyG * mieAnisotropyG, 2f * mieAnisotropyG, 1f / (4f * Mathf.PI)));
        _material.SetVector(PropVolumetricLight, new Vector4(scatteringCoefficient, extinctionCoefficient, _light.range, 1f - skyboxExtinctionIntensity));
        _material.SetTexture(PropCameraDepthTexture, renderer.GetVolumeLightDepthBuffer());
        _material.SetFloat(PropZTest, 8f);

        if (enableHeightFog)
        {
            _material.EnableKeyword("HEIGHT_FOG");
            _material.SetVector(PropHeightFog, new Vector4(heightFogGroundLevel, heightFogScale, 0f, 0f));
        }
        else
        {
            _material.DisableKeyword("HEIGHT_FOG");
        }

        switch (_light.type)
        {
            case LightType.Point:
                SetupPointLight(renderer, viewProj);
                break;
            case LightType.Spot:
                SetupSpotLight(renderer, viewProj);
                break;
            case LightType.Directional:
                SetupDirectionalLight(renderer, viewProj);
                break;
        }
    }

    private void Update()
    {
        _commandBuffer.Clear();
    }

    private void SetupPointLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
    {
        int pass = IsCameraInPointLightBounds() ? 0 : 2;
        _material.SetPass(pass);

        Mesh mesh = VolumetricLightRenderer.GetPointLightMesh();
        float size = _light.range * 2f;
        Matrix4x4 modelMatrix = Matrix4x4.TRS(transform.position, _light.transform.rotation, new Vector3(size, size, size));

        _material.SetMatrix(PropWorldViewProj, viewProj * modelMatrix);
        _material.SetMatrix(PropWorldView, _cachedCamera.worldToCameraMatrix * modelMatrix);

        if (enableNoise) _material.EnableKeyword("NOISE"); else _material.DisableKeyword("NOISE");

        _material.SetVector(PropLightPos, new Vector4(transform.position.x, transform.position.y, transform.position.z, 1f / (_light.range * _light.range)));
        _material.SetVector(PropLightColor, (Vector4)(_light.color * _light.intensity));

        if (_light.cookie == null)
        {
            _material.EnableKeyword("POINT");
            _material.DisableKeyword("POINT_COOKIE");
        }
        else
        {
            _material.SetMatrix(PropMyLightMatrix0, Matrix4x4.TRS(transform.position, _light.transform.rotation, Vector3.one).inverse);
            _material.EnableKeyword("POINT_COOKIE");
            _material.DisableKeyword("POINT");
            _material.SetTexture("_LightTexture0", _light.cookie);
        }

        bool outOfShadowDistance = (transform.position - _cachedCamera.transform.position).sqrMagnitude >= (QualitySettings.shadowDistance * QualitySettings.shadowDistance);
        if (_light.shadows != LightShadows.None && !outOfShadowDistance)
        {
            _material.EnableKeyword("SHADOWS_CUBE");
            _commandBuffer.SetGlobalTexture("_ShadowMapTexture", BuiltinRenderTextureType.CurrentActive);
            _commandBuffer.SetRenderTarget(renderer.GetVolumeLightBuffer());
            _commandBuffer.DrawMesh(mesh, modelMatrix, _material, 0, pass);
            CustomRenderEvent?.Invoke(renderer, this, _commandBuffer, viewProj);
        }
        else
        {
            _material.DisableKeyword("SHADOWS_CUBE");
            renderer.GlobalCommandBuffer.DrawMesh(mesh, modelMatrix, _material, 0, pass);
            CustomRenderEvent?.Invoke(renderer, this, renderer.GlobalCommandBuffer, viewProj);
        }
    }

    private void SetupSpotLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
    {
        int pass = IsCameraInSpotLightBounds() ? 1 : 3;
        Mesh mesh = VolumetricLightRenderer.GetSpotLightMesh();

        float range = _light.range;
        float scale = Mathf.Tan((_light.spotAngle + 1f) * 0.5f * Mathf.Deg2Rad) * range;
        Matrix4x4 modelMatrix = Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(scale, scale, range));
        Matrix4x4 lightInvMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;

        Matrix4x4 projMatrix = Matrix4x4.Perspective(_light.spotAngle, 1f, 0f, 1f);
        Matrix4x4 uvMatrix = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0f), Quaternion.identity, new Vector3(-0.5f, -0.5f, 1f));

        _material.SetMatrix(PropMyLightMatrix0, uvMatrix * projMatrix * lightInvMatrix);
        _material.SetMatrix(PropWorldViewProj, viewProj * modelMatrix);
        _material.SetVector(PropLightPos, new Vector4(transform.position.x, transform.position.y, transform.position.z, 1f / (_light.range * _light.range)));
        _material.SetVector(PropLightColor, (Vector4)(_light.color * _light.intensity));

        Vector3 pos = transform.position;
        Vector3 fwd = transform.forward;
        _material.SetFloat(PropPlaneD, -Vector3.Dot(pos + fwd * range, fwd));
        _material.SetFloat(PropCosAngle, Mathf.Cos((_light.spotAngle + 1f) * 0.5f * Mathf.Deg2Rad));
        _material.SetVector(PropConeApex, new Vector4(pos.x, pos.y, pos.z, 0f));
        _material.SetVector(PropConeAxis, new Vector4(fwd.x, fwd.y, fwd.z, 0f));

        _material.EnableKeyword("SPOT");
        if (enableNoise) _material.EnableKeyword("NOISE"); else _material.DisableKeyword("NOISE");

        _material.SetTexture("_LightTexture0", _light.cookie != null ? _light.cookie : VolumetricLightRenderer.GetDefaultSpotCookie());

        bool outOfShadowDistance = (transform.position - _cachedCamera.transform.position).sqrMagnitude >= (QualitySettings.shadowDistance * QualitySettings.shadowDistance);
        if (_light.shadows != LightShadows.None && !outOfShadowDistance)
        {
            Matrix4x4 shadowUV = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));
            Matrix4x4 shadowProj = _reversedZ ?
                Matrix4x4.Perspective(_light.spotAngle, 1f, _light.range, _light.shadowNearPlane) :
                Matrix4x4.Perspective(_light.spotAngle, 1f, _light.shadowNearPlane, _light.range);

            Matrix4x4 shadowMatrix = shadowUV * shadowProj;
            shadowMatrix[0, 2] *= -1f;
            shadowMatrix[1, 2] *= -1f;
            shadowMatrix[2, 2] *= -1f;
            shadowMatrix[3, 2] *= -1f;

            _material.SetMatrix(PropMyWorld2Shadow, shadowMatrix * lightInvMatrix);
            _material.SetMatrix(PropWorldView, shadowMatrix * lightInvMatrix);
            _material.EnableKeyword("SHADOWS_DEPTH");

            _commandBuffer.SetGlobalTexture("_ShadowMapTexture", BuiltinRenderTextureType.CurrentActive);
            _commandBuffer.SetRenderTarget(renderer.GetVolumeLightBuffer());
            _commandBuffer.DrawMesh(mesh, modelMatrix, _material, 0, pass);
            CustomRenderEvent?.Invoke(renderer, this, _commandBuffer, viewProj);
        }
        else
        {
            _material.DisableKeyword("SHADOWS_DEPTH");
            renderer.GlobalCommandBuffer.DrawMesh(mesh, modelMatrix, _material, 0, pass);
            CustomRenderEvent?.Invoke(renderer, this, renderer.GlobalCommandBuffer, viewProj);
        }
    }

    private void SetupDirectionalLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
    {
        const int pass = 4;
        _material.SetPass(pass);

        if (enableNoise) _material.EnableKeyword("NOISE"); else _material.DisableKeyword("NOISE");

        _material.SetVector(PropLightDir, new Vector4(transform.forward.x, transform.forward.y, transform.forward.z, 1f / (_light.range * _light.range)));
        _material.SetVector(PropLightColor, (Vector4)(_light.color * _light.intensity));
        _material.SetFloat(PropMaxRayLength, maxRayLength);

        if (_light.cookie == null)
        {
            _material.EnableKeyword("DIRECTIONAL");
            _material.DisableKeyword("DIRECTIONAL_COOKIE");
        }
        else
        {
            _material.EnableKeyword("DIRECTIONAL_COOKIE");
            _material.DisableKeyword("DIRECTIONAL");
            _material.SetTexture("_LightTexture0", _light.cookie);
        }

        float farClip = _cachedCamera.farClipPlane;
        _frustumCorners[0] = _cachedCamera.ViewportToWorldPoint(new Vector3(0f, 0f, farClip));
        _frustumCorners[1] = _cachedCamera.ViewportToWorldPoint(new Vector3(1f, 0f, farClip));
        _frustumCorners[2] = _cachedCamera.ViewportToWorldPoint(new Vector3(0f, 1f, farClip));
        _frustumCorners[3] = _cachedCamera.ViewportToWorldPoint(new Vector3(1f, 1f, farClip));
        _material.SetVectorArray("_FrustumCorners", _frustumCorners);

        Texture source = null;
        if (_light.shadows != LightShadows.None)
        {
            _material.EnableKeyword("SHADOWS_DEPTH");
            _commandBuffer.Blit(source, renderer.GetVolumeLightBuffer(), _material, pass);
            CustomRenderEvent?.Invoke(renderer, this, _commandBuffer, viewProj);
        }
        else
        {
            _material.DisableKeyword("SHADOWS_DEPTH");
            renderer.GlobalCommandBuffer.Blit(source, renderer.GetVolumeLightBuffer(), _material, pass);
            CustomRenderEvent?.Invoke(renderer, this, renderer.GlobalCommandBuffer, viewProj);
        }
    }

    private bool IsCameraInPointLightBounds()
    {
        float sqrDistance = (transform.position - _cachedCamera.transform.position).sqrMagnitude;
        float radius = _light.range + 1f;
        return sqrDistance < (radius * radius);
    }

    private bool IsCameraInSpotLightBounds()
    {
        Vector3 camDir = _cachedCamera.transform.position - transform.position;
        float distanceProjection = Vector3.Dot(transform.forward, camDir);
        if (distanceProjection > (_light.range + 1f)) return false;

        float angleCos = Vector3.Dot(transform.forward, camDir.normalized);
        return Mathf.Acos(angleCos) * Mathf.Rad2Deg <= (_light.spotAngle + 3f) * 0.5f;
    }
}