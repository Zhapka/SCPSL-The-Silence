using System;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class VolumetricLightRenderer : MonoBehaviour
{
    public enum VolumetricResolution
    {
        Full = 0,
        Half = 1,
        Quarter = 2
    }

    private static Mesh _pointLightMesh;
    private static Mesh _spotLightMesh;
    private static Material _lightMaterial;
    private static Texture _defaultSpotCookie;

    private Camera _camera;
    private CommandBuffer _preLightPass;
    private Matrix4x4 _viewProj;
    private Material _blitAddMaterial;
    private Material _bilateralBlurMaterial;

    private RenderTexture _volumeLightTexture;
    private RenderTexture _halfVolumeLightTexture;
    private RenderTexture _quarterVolumeLightTexture;
    private RenderTexture _halfDepthBuffer;
    private RenderTexture _quarterDepthBuffer;

    private VolumetricResolution _currentResolution;
    private Texture2D _ditheringTexture;
    private Texture3D _noiseTexture;

    private static readonly int PropHalfResDepthBuffer = Shader.PropertyToID("_HalfResDepthBuffer");
    private static readonly int PropHalfResColor = Shader.PropertyToID("_HalfResColor");
    private static readonly int PropQuarterResDepthBuffer = Shader.PropertyToID("_QuarterResDepthBuffer");
    private static readonly int PropQuarterResColor = Shader.PropertyToID("_QuarterResColor");
    private static readonly int PropDitherTexture = Shader.PropertyToID("_DitherTexture");
    private static readonly int PropNoiseTexture = Shader.PropertyToID("_NoiseTexture");
    private static readonly int PropSource = Shader.PropertyToID("_Source");

    [Header("Resolution & Performance")]
    public VolumetricResolution resolution = VolumetricResolution.Half;

    [Header("Fallback Assets")]
    public Texture defaultSpotCookie;

    public CommandBuffer GlobalCommandBuffer => _preLightPass;
    public static event Action<VolumetricLightRenderer, Matrix4x4> PreRenderEvent;

    public static Material GetLightMaterial() => _lightMaterial;
    public static Mesh GetPointLightMesh() => _pointLightMesh;
    public static Mesh GetSpotLightMesh() => _spotLightMesh;
    public static Texture GetDefaultSpotCookie() => _defaultSpotCookie;

    public RenderTexture GetVolumeLightBuffer()
    {
        if (resolution == VolumetricResolution.Quarter) return _quarterVolumeLightTexture;
        if (resolution == VolumetricResolution.Half) return _halfVolumeLightTexture;
        return _volumeLightTexture;
    }

    public RenderTexture GetVolumeLightDepthBuffer()
    {
        if (resolution == VolumetricResolution.Quarter) return _quarterDepthBuffer;
        if (resolution == VolumetricResolution.Half) return _halfDepthBuffer;
        return null;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_camera.actualRenderingPath == RenderingPath.Forward)
        {
            _camera.depthTextureMode |= DepthTextureMode.Depth;
        }

        _currentResolution = resolution;

        Shader blitShader = Shader.Find("Hidden/BlitAdd");
        if (blitShader == null) throw new Exception("Critical Error: 'Hidden/BlitAdd' shader missing.");
        _blitAddMaterial = new Material(blitShader);

        Shader blurShader = Shader.Find("Hidden/BilateralBlur");
        if (blurShader == null) throw new Exception("Critical Error: 'Hidden/BilateralBlur' shader missing.");
        _bilateralBlurMaterial = new Material(blurShader);

        _preLightPass = new CommandBuffer { name = "Volumetric PreLight Pass" };

        ChangeResolution();

        if (_pointLightMesh == null)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _pointLightMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(sphere);
        }
        if (_spotLightMesh == null)
        {
            _spotLightMesh = CreateSpotLightMesh();
        }
        if (_lightMaterial == null)
        {
            Shader lightShader = Shader.Find("Sandbox/VolumetricLight");
            if (lightShader != null) _lightMaterial = new Material(lightShader);
        }
        if (_defaultSpotCookie == null)
        {
            _defaultSpotCookie = defaultSpotCookie;
        }

        LoadNoise3dTexture();
        GenerateDitherTexture();
    }

    private void OnEnable()
    {
        CameraEvent evt = (_camera.actualRenderingPath == RenderingPath.Forward) ? CameraEvent.AfterDepthTexture : CameraEvent.BeforeLighting;
        _camera.AddCommandBuffer(evt, _preLightPass);
    }

    private void OnDisable()
    {
        if (_camera == null) return;
        CameraEvent evt = (_camera.actualRenderingPath == RenderingPath.Forward) ? CameraEvent.AfterDepthTexture : CameraEvent.BeforeLighting;
        _camera.RemoveCommandBuffer(evt, _preLightPass);
    }

    private void OnDestroy()
    {
        CleanupTextures();
        if (_blitAddMaterial != null) DestroyImmediate(_blitAddMaterial);
        if (_bilateralBlurMaterial != null) DestroyImmediate(_bilateralBlurMaterial);
        if (_ditheringTexture != null) DestroyImmediate(_ditheringTexture);
        if (_noiseTexture != null) DestroyImmediate(_noiseTexture);
        if (_preLightPass != null) _preLightPass.Dispose();
    }

    private void CleanupTextures()
    {
        if (_volumeLightTexture != null) { _volumeLightTexture.Release(); DestroyImmediate(_volumeLightTexture); }
        if (_halfVolumeLightTexture != null) { _halfVolumeLightTexture.Release(); DestroyImmediate(_halfVolumeLightTexture); }
        if (_quarterVolumeLightTexture != null) { _quarterVolumeLightTexture.Release(); DestroyImmediate(_quarterVolumeLightTexture); }
        if (_halfDepthBuffer != null) { _halfDepthBuffer.Release(); DestroyImmediate(_halfDepthBuffer); }
        if (_quarterDepthBuffer != null) { _quarterDepthBuffer.Release(); DestroyImmediate(_quarterDepthBuffer); }
    }

    private void ChangeResolution()
    {
        CleanupTextures();

        int width = Mathf.Max(1, _camera.pixelWidth);
        int height = Mathf.Max(1, _camera.pixelHeight);

        _volumeLightTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
        {
            name = "VolumeLightBuffer",
            filterMode = FilterMode.Bilinear
        };

        if (resolution == VolumetricResolution.Half || resolution == VolumetricResolution.Quarter)
        {
            _halfVolumeLightTexture = new RenderTexture(width / 2, height / 2, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "VolumeLightBufferHalf",
                filterMode = FilterMode.Bilinear
            };
            _halfDepthBuffer = new RenderTexture(width / 2, height / 2, 0, RenderTextureFormat.RFloat)
            {
                name = "VolumeLightHalfDepth",
                filterMode = FilterMode.Point
            };
            _halfDepthBuffer.Create();
        }

        if (resolution == VolumetricResolution.Quarter)
        {
            _quarterVolumeLightTexture = new RenderTexture(width / 4, height / 4, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "VolumeLightBufferQuarter",
                filterMode = FilterMode.Bilinear
            };
            _quarterDepthBuffer = new RenderTexture(width / 4, height / 4, 0, RenderTextureFormat.RFloat)
            {
                name = "VolumeLightQuarterDepth",
                filterMode = FilterMode.Point
            };
            _quarterDepthBuffer.Create();
        }
    }

    public void OnPreRender()
    {
        Matrix4x4 proj = Matrix4x4.Perspective(_camera.fieldOfView, _camera.aspect, 0.01f, _camera.farClipPlane);
        _viewProj = GL.GetGPUProjectionMatrix(proj, true) * _camera.worldToCameraMatrix;

        _preLightPass.Clear();
        bool highShaderLevel = SystemInfo.graphicsShaderLevel > 40;

        if (resolution == VolumetricResolution.Quarter)
        {
            _preLightPass.Blit(null, _halfDepthBuffer, _bilateralBlurMaterial, highShaderLevel ? 4 : 10);
            _preLightPass.Blit(null, _quarterDepthBuffer, _bilateralBlurMaterial, highShaderLevel ? 6 : 11);
            _preLightPass.SetRenderTarget(_quarterVolumeLightTexture);
        }
        else if (resolution == VolumetricResolution.Half)
        {
            _preLightPass.Blit(null, _halfDepthBuffer, _bilateralBlurMaterial, highShaderLevel ? 4 : 10);
            _preLightPass.SetRenderTarget(_halfVolumeLightTexture);
        }
        else
        {
            _preLightPass.SetRenderTarget(_volumeLightTexture);
        }

        _preLightPass.ClearRenderTarget(false, true, new Color(0f, 0f, 0f, 1f));
        UpdateMaterialParameters();

        PreRenderEvent?.Invoke(this, _viewProj);
    }

    [ImageEffectOpaque]
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (resolution == VolumetricResolution.Quarter)
        {
            RenderTexture temp = RenderTexture.GetTemporary(_quarterDepthBuffer.width, _quarterDepthBuffer.height, 0, RenderTextureFormat.ARGBHalf);
            temp.filterMode = FilterMode.Bilinear;
            Graphics.Blit(_quarterVolumeLightTexture, temp, _bilateralBlurMaterial, 8);
            Graphics.Blit(temp, _quarterVolumeLightTexture, _bilateralBlurMaterial, 9);
            Graphics.Blit(_quarterVolumeLightTexture, _volumeLightTexture, _bilateralBlurMaterial, 7);
            RenderTexture.ReleaseTemporary(temp);
        }
        else if (resolution == VolumetricResolution.Half)
        {
            RenderTexture temp2 = RenderTexture.GetTemporary(_halfVolumeLightTexture.width, _halfVolumeLightTexture.height, 0, RenderTextureFormat.ARGBHalf);
            temp2.filterMode = FilterMode.Bilinear;
            Graphics.Blit(_halfVolumeLightTexture, temp2, _bilateralBlurMaterial, 2);
            Graphics.Blit(temp2, _halfVolumeLightTexture, _bilateralBlurMaterial, 3);
            Graphics.Blit(_halfVolumeLightTexture, _volumeLightTexture, _bilateralBlurMaterial, 5);
            RenderTexture.ReleaseTemporary(temp2);
        }
        else
        {
            RenderTexture temp3 = RenderTexture.GetTemporary(_volumeLightTexture.width, _volumeLightTexture.height, 0, RenderTextureFormat.ARGBHalf);
            temp3.filterMode = FilterMode.Bilinear;
            Graphics.Blit(_volumeLightTexture, temp3, _bilateralBlurMaterial, 0);
            Graphics.Blit(temp3, _volumeLightTexture, _bilateralBlurMaterial, 1);
            Graphics.Blit(temp3, _volumeLightTexture);
            RenderTexture.ReleaseTemporary(temp3);
        }

        _blitAddMaterial.SetTexture(PropSource, source);
        Graphics.Blit(_volumeLightTexture, destination, _blitAddMaterial, 0);
    }

    private void UpdateMaterialParameters()
    {
        _bilateralBlurMaterial.SetTexture(PropHalfResDepthBuffer, _halfDepthBuffer);
        _bilateralBlurMaterial.SetTexture(PropHalfResColor, _halfVolumeLightTexture);
        _bilateralBlurMaterial.SetTexture(PropQuarterResDepthBuffer, _quarterDepthBuffer);
        _bilateralBlurMaterial.SetTexture(PropQuarterResColor, _quarterVolumeLightTexture);

        Shader.SetGlobalTexture(PropDitherTexture, _ditheringTexture);
        Shader.SetGlobalTexture(PropNoiseTexture, _noiseTexture);
    }

    private void Update()
    {
        if (_currentResolution != resolution)
        {
            _currentResolution = resolution;
            ChangeResolution();
        }
        if (_volumeLightTexture != null && (_volumeLightTexture.width != _camera.pixelWidth || _volumeLightTexture.height != _camera.pixelHeight))
        {
            ChangeResolution();
        }
    }

    private void LoadNoise3dTexture()
    {
        TextAsset asset = Resources.Load("NoiseVolume") as TextAsset;
        if (asset == null) return;

        byte[] bytes = asset.bytes;
        int width = (int)BitConverter.ToUInt32(bytes, 16);
        int height = (int)BitConverter.ToUInt32(bytes, 12);
        int depth = (int)BitConverter.ToUInt32(bytes, 24);
        uint volumeSize = BitConverter.ToUInt32(bytes, 20);
        uint bitCount = BitConverter.ToUInt32(bytes, 88);

        _noiseTexture = new Texture3D(width, height, depth, TextureFormat.RGBA32, false)
        {
            name = "3D Noise Texture",
            wrapMode = TextureWrapMode.Repeat
        };

        Color[] colors = new Color[width * height * depth];
        int offset = 128;
        int step = (int)(bitCount / 8);
        int rowSize = (int)((width * bitCount + 7) / 8);

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float val = bytes[offset + x * step] / 255f;
                    colors[x + y * width + z * width * height] = new Color(val, val, val, val);
                }
                offset += rowSize;
            }
        }
        _noiseTexture.SetPixels(colors);
        _noiseTexture.Apply();
    }

    private void GenerateDitherTexture()
    {
        if (_ditheringTexture != null) return;

        _ditheringTexture = new Texture2D(8, 8, TextureFormat.Alpha8, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat
        };

        byte[] pattern = {
            3, 192, 51, 239, 15, 204, 62, 251,
            129, 66, 176, 113, 141, 78, 188, 125,
            35, 223, 19, 207, 47, 235, 31, 219,
            160, 98, 145, 82, 172, 109, 156, 94,
            11, 200, 58, 247, 7, 196, 54, 243,
            137, 74, 184, 121, 133, 70, 180, 117,
            43, 231, 27, 215, 39, 227, 23, 211,
            168, 105, 153, 90, 164, 102, 149, 86
        };

        Color32[] colors = new Color32[64];
        for (int i = 0; i < 64; i++)
        {
            colors[i] = new Color32(pattern[i], pattern[i], pattern[i], pattern[i]);
        }

        _ditheringTexture.SetPixels32(colors);
        _ditheringTexture.Apply();
    }

    private Mesh CreateSpotLightMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[50];
        Color32[] colors = new Color32[50];

        vertices[0] = Vector3.zero;
        vertices[1] = Vector3.forward;

        float angle = 0f;
        float step = Mathf.PI / 8f;
        float radiusScale = 0.9f;

        for (int i = 0; i < 16; i++)
        {
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            vertices[i + 2] = new Vector3(-cos * radiusScale, sin * radiusScale, radiusScale);
            colors[i + 2] = new Color32(255, 255, 255, 255);

            vertices[i + 2 + 16] = new Vector3(-cos, sin, 1f);
            colors[i + 2 + 16] = new Color32(255, 255, 255, 0);

            vertices[i + 2 + 32] = new Vector3(-cos * radiusScale, sin * radiusScale, 1f);
            colors[i + 2 + 32] = new Color32(255, 255, 255, 255);

            angle += step;
        }

        mesh.vertices = vertices;
        mesh.colors32 = colors;

        int[] indices = new int[288];
        int idx = 0;

        for (int j = 2; j < 17; j++)
        {
            indices[idx++] = 0; indices[idx++] = j; indices[idx++] = j + 1;
        }
        indices[idx++] = 0; indices[idx++] = 17; indices[idx++] = 2;

        for (int k = 2; k < 17; k++)
        {
            indices[idx++] = k; indices[idx++] = k + 16; indices[idx++] = k + 1;
            indices[idx++] = k + 1; indices[idx++] = k + 16; indices[idx++] = k + 16 + 1;
        }
        indices[idx++] = 2; indices[idx++] = 17; indices[idx++] = 18;
        indices[idx++] = 18; indices[idx++] = 17; indices[idx++] = 33;

        for (int l = 18; l < 33; l++)
        {
            indices[idx++] = l; indices[idx++] = l + 16; indices[idx++] = l + 1;
            indices[idx++] = l + 1; indices[idx++] = l + 16; indices[idx++] = l + 16 + 1;
        }
        indices[idx++] = 18; indices[idx++] = 33; indices[idx++] = 34;
        indices[idx++] = 34; indices[idx++] = 33; indices[idx++] = 49;

        for (int m = 34; m < 49; m++)
        {
            indices[idx++] = 1; indices[idx++] = m + 1; indices[idx++] = m;
        }
        indices[idx++] = 1; indices[idx++] = 34; indices[idx++] = 49;

        mesh.triangles = indices;
        mesh.RecalculateBounds();
        return mesh;
    }
}