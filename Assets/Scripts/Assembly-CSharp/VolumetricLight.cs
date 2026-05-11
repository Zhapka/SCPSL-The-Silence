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

    [Header("Quality Settings")]
    [Range(1f, 64f)]
    [Tooltip("Number of raymarching samples. Higher = smoother fog, lower = better performance.")]
    public int SampleCount = 8;
    [Range(0f, 50f)]
    [Tooltip("How much light scatters inside the fog volume.")]
    public float ScatteringCoef = 5f;
    [Range(0f, 5f)]
    [Tooltip("How fast light dims as it travels through the fog.")]
    public float ExtinctionCoef = 0.2f;
    [Range(0f, 1f)]
    [Tooltip("Fog thickness at skybox level.")]
    public float SkyboxExtinctionCoef = 0f;
    [Range(0f, 0.999f)]
    [Tooltip("Anisotropy parameter for Mie scattering. Simulates realistic light glow around source.")]
    public float MieG = 0.1f;
    [Tooltip("Maximum distance for volumetric calculation.")]
    public float MaxRayLength = 100f;

    [Header("Height Fog")]
    public bool HeightFog;
    [Range(0f, 0.5f)] public float HeightScale = 0.1f;
    public float GroundLevel;

    [Header("Noise Settings")]
    public bool Noise;
    public float NoiseScale = 0.015f;
    public float NoiseIntensity = 1f;
    public float NoiseIntensityOffset = 0.3f;
    public Vector2 NoiseVelocity = new Vector2(3f, 3f);

    private Vector4[] _frustumCorners = new Vector4[4];
    private bool _reversedZ;

    // Cache Shader Property IDs for high performance
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
    private static readonly int PropLightTexture0 = Shader.PropertyToID("_LightTexture0");
    private static readonly int PropShadowMapTexture = Shader.PropertyToID("_ShadowMapTexture");
    private static readonly int PropPlaneD = Shader.PropertyToID("_PlaneD");
    private static readonly int PropCosAngle = Shader.PropertyToID("_CosAngle");
    private static readonly int PropConeApex = Shader.PropertyToID("_ConeApex");
    private static readonly int PropConeAxis = Shader.PropertyToID("_ConeAxis");
    private static readonly int PropMyWorld2Shadow = Shader.PropertyToID("_MyWorld2Shadow");
    private static readonly int PropLightDir = Shader.PropertyToID("_LightDir");
    private static readonly int PropMaxRayLength = Shader.PropertyToID("_MaxRayLength");
    private static readonly int PropFrustumCorners = Shader.PropertyToID("_FrustumCorners");

    public Light Light { get { return _light; } }
    public Material VolumetricMaterial { get { return _material; } }

    public event Action<VolumetricLightRenderer, VolumetricLight, CommandBuffer, Matrix4x4> CustomRenderEvent;

    private void Start()
    {
        GraphicsDeviceType devType = SystemInfo.graphicsDeviceType;
        if (devType == GraphicsDeviceType.Direct3D11 || devType == GraphicsDeviceType.Direct3D12 ||
            devType == GraphicsDeviceType.Metal || devType == GraphicsDeviceType.PlayStation4 ||
            devType == GraphicsDeviceType.Vulkan || devType == GraphicsDeviceType.XboxOne)
        {
            _reversedZ = true;
        }

        _commandBuffer = new CommandBuffer { name = "Light Command Buffer" };
        _cascadeShadowCommandBuffer = new CommandBuffer { name = "Dir Light Command Buffer" };
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
            throw new Exception("Critical Error: \"Sandbox/VolumetricLight\" shader is missing.");
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
        if (_commandBuffer != null)
        {
            if (_light != null)
            {
                if (_light.type == LightType.Directional)
                    _light.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, _commandBuffer);
                else
                    _light.RemoveCommandBuffer(LightEvent.AfterShadowMap, _commandBuffer);
            }
            _commandBuffer.Release();
        }
        if (_cascadeShadowCommandBuffer != null)
        {
            if (_light != null && _light.type == LightType.Directional)
            {
                _light.RemoveCommandBuffer(LightEvent.AfterShadowMap, _cascadeShadowCommandBuffer);
            }
            _cascadeShadowCommandBuffer.Release();
        }
        if (_material != null)
        {
            Destroy(_material);
        }
    }

    private void Update()
    {
        if (_commandBuffer != null && _light != null && _light.enabled)
        {
            _commandBuffer.Clear();
        }
    }

    private void VolumetricLightRenderer_PreRenderEvent(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
    {
        if (_light.gameObject.activeInHierarchy && _light.enabled)
        {
            if (Camera.current != null)
            {
                _material.SetVector(PropCameraForward, Camera.current.transform.forward);
            }

            _material.SetInt(PropSampleCount, SampleCount);
            _material.SetVector(PropNoiseVelocity, new Vector4(NoiseVelocity.x, NoiseVelocity.y) * NoiseScale);
            _material.SetVector(PropNoiseData, new Vector4(NoiseScale, NoiseIntensity, NoiseIntensityOffset));
            _material.SetVector(PropMieG, new Vector4(1f - MieG * MieG, 1f + MieG * MieG, 2f * MieG, 1f / (4f * Mathf.PI)));
            _material.SetVector(PropVolumetricLight, new Vector4(ScatteringCoef, ExtinctionCoef, Mathf.Min(_light.range, MaxRayLength), 1f - SkyboxExtinctionCoef));
            _material.SetTexture(PropCameraDepthTexture, renderer.GetVolumeLightDepthBuffer());
            _material.SetFloat(PropZTest, 8f);

            if (HeightFog)
            {
                _material.EnableKeyword("HEIGHT_FOG");
                _material.SetVector(PropHeightFog, new Vector4(GroundLevel, HeightScale));
            }
            else
            {
                _material.DisableKeyword("HEIGHT_FOG");
            }

            if (_light.type == LightType.Point)
            {
                SetupPointLight(renderer, viewProj);
            }
            else if (_light.type == LightType.Spot)
            {
                SetupSpotLight(renderer, viewProj);
            }
            else if (_light.type == LightType.Directional)
            {
                SetupDirectionalLight(renderer, viewProj);
            }
        }
    }

    private void SetupPointLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
    {
        int passIndex = IsCameraInPointLightBounds() ? 0 : 2;
        _material.SetPass(passIndex);

        Mesh pointLightMesh = VolumetricLightRenderer.GetPointLightMesh();
        float doubleRange = _light.range * 2f;

        Transform currentTransform = base.transform;
        Vector3 lightPos = currentTransform.position;
        Quaternion lightRot = currentTransform.rotation;

        Matrix4x4 matrix4x = Matrix4x4.TRS(lightPos, lightRot, new Vector3(doubleRange, doubleRange, doubleRange));
        _material.SetMatrix(PropWorldViewProj, viewProj * matrix4x);

        if (Camera.current != null)
        {
            _material.SetMatrix(PropWorldView, Camera.current.worldToCameraMatrix * matrix4x);
        }

        if (Noise) _material.EnableKeyword("NOISE");
        else _material.DisableKeyword("NOISE");

        _material.SetVector(PropLightPos, new Vector4(lightPos.x, lightPos.y, lightPos.z, 1f / (_light.range * _light.range)));
        _material.SetColor(PropLightColor, _light.color * _light.intensity);

        if (_light.cookie == null)
        {
            _material.EnableKeyword("POINT");
            _material.DisableKeyword("POINT_COOKIE");
        }
        else
        {
            Matrix4x4 inverse = Matrix4x4.TRS(lightPos, lightRot, Vector3.one).inverse;
            _material.SetMatrix(PropMyLightMatrix0, inverse);
            _material.EnableKeyword("POINT_COOKIE");
            _material.DisableKeyword("POINT");
            _material.SetTexture(PropLightTexture0, _light.cookie);
        }

        bool isTooFar = Camera.current != null && (lightPos - Camera.current.transform.position).sqrMagnitude >= (QualitySettings.shadowDistance * QualitySettings.shadowDistance);

        if (_light.shadows != LightShadows.None && !isTooFar)
        {
            _material.EnableKeyword("SHADOWS_CUBE");
            _commandBuffer.SetGlobalTexture(PropShadowMapTexture, BuiltinRenderTextureType.CurrentActive);
            _commandBuffer.SetRenderTarget(renderer.GetVolumeLightBuffer());
            _commandBuffer.DrawMesh(pointLightMesh, matrix4x, _material, 0, passIndex);

            CustomRenderEvent?.Invoke(renderer, this, _commandBuffer, viewProj);
        }
        else
        {
            _material.DisableKeyword("SHADOWS_CUBE");
            renderer.GlobalCommandBuffer.DrawMesh(pointLightMesh, matrix4x, _material, 0, passIndex);

            CustomRenderEvent?.Invoke(renderer, this, renderer.GlobalCommandBuffer, viewProj);
        }
    }

    private void SetupSpotLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
    {
        int shaderPass = IsCameraInSpotLightBounds() ? 1 : 3;

        Mesh spotLightMesh = VolumetricLightRenderer.GetSpotLightMesh();
        float range = _light.range;
        float halfAngleRad = (_light.spotAngle + 1f) * 0.5f * Mathf.Deg2Rad;
        float tanNum = Mathf.Tan(halfAngleRad) * range;

        Transform currentTransform = base.transform;
        Vector3 lightPos = currentTransform.position;
        Quaternion lightRot = currentTransform.rotation;
        Vector3 lightForward = currentTransform.forward;

        Matrix4x4 matrix4x = Matrix4x4.TRS(lightPos, lightRot, new Vector3(tanNum, tanNum, range));
        Matrix4x4 inverse = Matrix4x4.TRS(lightPos, lightRot, Vector3.one).inverse;
        Matrix4x4 matrix4x2 = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0f), Quaternion.identity, new Vector3(-0.5f, -0.5f, 1f));
        Matrix4x4 matrix4x3 = Matrix4x4.Perspective(_light.spotAngle, 1f, 0f, 1f);

        _material.SetMatrix(PropMyLightMatrix0, matrix4x2 * matrix4x3 * inverse);
        _material.SetMatrix(PropWorldViewProj, viewProj * matrix4x);
        _material.SetVector(PropLightPos, new Vector4(lightPos.x, lightPos.y, lightPos.z, 1f / (range * range)));
        _material.SetVector(PropLightColor, _light.color * _light.intensity);

        Vector3 lhs = lightPos + lightForward * range;
        float planeDValue = -Vector3.Dot(lhs, lightForward);

        _material.SetFloat(PropPlaneD, planeDValue);
        _material.SetFloat(PropCosAngle, Mathf.Cos(halfAngleRad));
        _material.SetVector(PropConeApex, new Vector4(lightPos.x, lightPos.y, lightPos.z));
        _material.SetVector(PropConeAxis, new Vector4(lightForward.x, lightForward.y, lightForward.z));
        _material.EnableKeyword("SPOT");

        if (Noise) _material.EnableKeyword("NOISE");
        else _material.DisableKeyword("NOISE");

        _material.SetTexture(PropLightTexture0, (_light.cookie != null) ? _light.cookie : VolumetricLightRenderer.GetDefaultSpotCookie());

        bool isTooFar = Camera.current != null && (lightPos - Camera.current.transform.position).sqrMagnitude >= (QualitySettings.shadowDistance * QualitySettings.shadowDistance);

        if (_light.shadows != LightShadows.None && !isTooFar)
        {
            matrix4x2 = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));
            matrix4x3 = (!_reversedZ) ?
                Matrix4x4.Perspective(_light.spotAngle, 1f, _light.shadowNearPlane, range) :
                Matrix4x4.Perspective(_light.spotAngle, 1f, range, _light.shadowNearPlane);

            Matrix4x4 matrix4x4 = matrix4x2 * matrix4x3;
            matrix4x4[0, 2] *= -1f;
            matrix4x4[1, 2] *= -1f;
            matrix4x4[2, 2] *= -1f;
            matrix4x4[3, 2] *= -1f;

            _material.SetMatrix(PropMyWorld2Shadow, matrix4x4 * inverse);
            _material.SetMatrix(PropWorldView, matrix4x4 * inverse);
            _material.EnableKeyword("SHADOWS_DEPTH");
            _commandBuffer.SetGlobalTexture(PropShadowMapTexture, BuiltinRenderTextureType.CurrentActive);
            _commandBuffer.SetRenderTarget(renderer.GetVolumeLightBuffer());
            _commandBuffer.DrawMesh(spotLightMesh, matrix4x, _material, 0, shaderPass);

            CustomRenderEvent?.Invoke(renderer, this, _commandBuffer, viewProj);
        }
        else
        {
            _material.DisableKeyword("SHADOWS_DEPTH");
            renderer.GlobalCommandBuffer.DrawMesh(spotLightMesh, matrix4x, _material, 0, shaderPass);

            CustomRenderEvent?.Invoke(renderer, this, renderer.GlobalCommandBuffer, viewProj);
        }
    }

    private void SetupDirectionalLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
    {
        int pass = 4;
        _material.SetPass(pass);

        if (Noise) _material.EnableKeyword("NOISE");
        else _material.DisableKeyword("NOISE");

        Transform lightTransform = _light.transform;
        Vector3 lightForward = lightTransform.forward;
        _material.SetVector(PropLightDir, new Vector4(lightForward.x, lightForward.y, lightForward.z, 1f / (_light.range * _light.range)));
        _material.SetVector(PropLightColor, _light.color * _light.intensity);
        _material.SetFloat(PropMaxRayLength, MaxRayLength);

        if (_light.cookie == null)
        {
            _material.EnableKeyword("DIRECTIONAL");
            _material.DisableKeyword("DIRECTIONAL_COOKIE");
        }
        else
        {
            _material.EnableKeyword("DIRECTIONAL_COOKIE");
            _material.DisableKeyword("DIRECTIONAL");
            _material.SetTexture(PropLightTexture0, _light.cookie);
        }

        Camera cam = Camera.current;
        if (cam != null)
        {
            Matrix4x4 viewProjInv = (cam.projectionMatrix * cam.worldToCameraMatrix).inverse;
            _frustumCorners[0] = viewProjInv.MultiplyPoint(new Vector3(-1f, -1f, 1f));
            _frustumCorners[2] = viewProjInv.MultiplyPoint(new Vector3(-1f, 1f, 1f));
            _frustumCorners[3] = viewProjInv.MultiplyPoint(new Vector3(1f, 1f, 1f));
            _frustumCorners[1] = viewProjInv.MultiplyPoint(new Vector3(1f, -1f, 1f));
            _material.SetVectorArray(PropFrustumCorners, _frustumCorners);
        }

        RenderTargetIdentifier currentActive = new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive);
        RenderTargetIdentifier targetVolumeBuffer = renderer.GetVolumeLightBuffer();

        if (_light.shadows != LightShadows.None)
        {
            _material.EnableKeyword("SHADOWS_DEPTH");
            _commandBuffer.Blit(currentActive, targetVolumeBuffer, _material, pass);
            CustomRenderEvent?.Invoke(renderer, this, _commandBuffer, viewProj);
        }
        else
        {
            _material.DisableKeyword("SHADOWS_DEPTH");
            renderer.GlobalCommandBuffer.Blit(currentActive, targetVolumeBuffer, _material, pass);
            CustomRenderEvent?.Invoke(renderer, this, renderer.GlobalCommandBuffer, viewProj);
        }
    }

    private bool IsCameraInPointLightBounds()
    {
        Camera cam = Camera.current;
        if (cam == null) return false;

        float sqrMagnitude = (_light.transform.position - cam.transform.position).sqrMagnitude;
        float limitDist = _light.range + 1f;
        return sqrMagnitude < (limitDist * limitDist);
    }

    private bool IsCameraInSpotLightBounds()
    {
        Camera cam = Camera.current;
        if (cam == null) return false;

        Vector3 camPos = cam.transform.position;
        Vector3 lightPos = _light.transform.position;
        Vector3 lightForward = _light.transform.forward;

        float distAlongAxis = Vector3.Dot(lightForward, camPos - lightPos);
        float limitRange = _light.range + 1f;
        if (distAlongAxis > limitRange || distAlongAxis < 0f) return false;

        Vector3 dirToCam = (camPos - lightPos).normalized;
        float currentCos = Vector3.Dot(base.transform.forward, dirToCam);
        float limitCos = Mathf.Cos((_light.spotAngle + 3f) * 0.5f * Mathf.Deg2Rad);

        return currentCos > limitCos;
    }
}
