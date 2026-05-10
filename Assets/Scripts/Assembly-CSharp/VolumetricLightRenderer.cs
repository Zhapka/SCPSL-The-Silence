using System;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class VolumetricLightRenderer : MonoBehaviour
{
    public enum VolumtericResolution
    {
        Full = 0,
        Half = 1,
        Quarter = 2
    }

    private static Mesh _pointLightMesh;
    private static Mesh _spotLightMesh;
    private static Material _lightMaterial;

    private Camera _camera;
    private CommandBuffer _preLightPass;
    private Matrix4x4 _viewProj;
    private Material _blitAddMaterial;
    private Material _bilateralBlurMaterial;
    private RenderTexture _volumeLightTexture;
    private RenderTexture _halfVolumeLightTexture;
    private RenderTexture _quarterVolumeLightTexture;
    private static Texture _defaultSpotCookie;
    private RenderTexture _halfDepthBuffer;
    private RenderTexture _quarterDepthBuffer;
    private VolumtericResolution _currentResolution;
    private Texture2D _ditheringTexture;
    private Texture3D _noiseTexture;

    public VolumtericResolution Resolution = VolumtericResolution.Half;
    public Texture DefaultSpotCookie;

    public CommandBuffer GlobalCommandBuffer
    {
        get { return _preLightPass; }
    }

    public static event Action<VolumetricLightRenderer, Matrix4x4> PreRenderEvent;

    public static Material GetLightMaterial() { return _lightMaterial; }
    public static Mesh GetPointLightMesh() { return _pointLightMesh; }
    public static Mesh GetSpotLightMesh() { return _spotLightMesh; }

    public RenderTexture GetVolumeLightBuffer()
    {
        if (Resolution == VolumtericResolution.Quarter) return _quarterVolumeLightTexture;
        if (Resolution == VolumtericResolution.Half) return _halfVolumeLightTexture;
        return _volumeLightTexture;
    }

    public RenderTexture GetVolumeLightDepthBuffer()
    {
        if (Resolution == VolumtericResolution.Quarter) return _quarterDepthBuffer;
        if (Resolution == VolumtericResolution.Half) return _halfDepthBuffer;
        return null;
    }

    public static Texture GetDefaultSpotCookie() { return _defaultSpotCookie; }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_camera.actualRenderingPath == RenderingPath.Forward)
        {
            _camera.depthTextureMode |= DepthTextureMode.Depth;
        }
        _currentResolution = Resolution;

        Shader shader = Shader.Find("Hidden/BlitAdd");
        if (shader == null) throw new Exception("Critical Error: \"Hidden/BlitAdd\" shader is missing.");
        _blitAddMaterial = new Material(shader);

        shader = Shader.Find("Hidden/BilateralBlur");
        if (shader == null) throw new Exception("Critical Error: \"Hidden/BilateralBlur\" shader is missing.");
        _bilateralBlurMaterial = new Material(shader);

        _preLightPass = new CommandBuffer();
        _preLightPass.name = "PreLight";
        ChangeResolution();

        if (_pointLightMesh == null)
        {
            GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _pointLightMesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(gameObject);
        }
        if (_spotLightMesh == null)
        {
            _spotLightMesh = CreateSpotLightMesh();
        }
        if (_lightMaterial == null)
        {
            shader = Shader.Find("Sandbox/VolumetricLight");
            if (shader == null) throw new Exception("Critical Error: \"Sandbox/VolumetricLight\" shader is missing.");
            _lightMaterial = new Material(shader);
        }
        if (_defaultSpotCookie == null)
        {
            _defaultSpotCookie = DefaultSpotCookie;
        }
        LoadNoise3dTexture();
        GenerateDitherTexture();
    }

    private void OnEnable()
    {
        _camera.AddCommandBuffer((_camera.actualRenderingPath == RenderingPath.Forward) ? CameraEvent.AfterDepthTexture : CameraEvent.BeforeLighting, _preLightPass);
    }

    private void OnDisable()
    {
        _camera.RemoveCommandBuffer((_camera.actualRenderingPath == RenderingPath.Forward) ? CameraEvent.AfterDepthTexture : CameraEvent.BeforeLighting, _preLightPass);
    }

    private void ChangeResolution()
    {
        int pixelWidth = Mathf.Max(1, _camera.pixelWidth);
        int pixelHeight = Mathf.Max(1, _camera.pixelHeight);

        if (_volumeLightTexture != null) UnityEngine.Object.Destroy(_volumeLightTexture);
        _volumeLightTexture = new RenderTexture(pixelWidth, pixelHeight, 0, RenderTextureFormat.ARGBHalf);
        _volumeLightTexture.name = "VolumeLightBuffer";
        _volumeLightTexture.filterMode = FilterMode.Bilinear;

        if (_halfDepthBuffer != null) UnityEngine.Object.Destroy(_halfDepthBuffer);
        if (_halfVolumeLightTexture != null) UnityEngine.Object.Destroy(_halfVolumeLightTexture);
        if (Resolution == VolumtericResolution.Half || Resolution == VolumtericResolution.Quarter)
        {
            _halfVolumeLightTexture = new RenderTexture(pixelWidth / 2, pixelHeight / 2, 0, RenderTextureFormat.ARGBHalf);
            _halfVolumeLightTexture.name = "VolumeLightBufferHalf";
            _halfVolumeLightTexture.filterMode = FilterMode.Bilinear;
            _halfDepthBuffer = new RenderTexture(pixelWidth / 2, pixelHeight / 2, 0, RenderTextureFormat.RFloat);
            _halfDepthBuffer.name = "VolumeLightHalfDepth";
            _halfDepthBuffer.filterMode = FilterMode.Point;
            _halfDepthBuffer.Create();
        }

        if (_quarterVolumeLightTexture != null) UnityEngine.Object.Destroy(_quarterVolumeLightTexture);
        if (_quarterDepthBuffer != null) UnityEngine.Object.Destroy(_quarterDepthBuffer);
        if (Resolution == VolumtericResolution.Quarter)
        {
            _quarterVolumeLightTexture = new RenderTexture(pixelWidth / 4, pixelHeight / 4, 0, RenderTextureFormat.ARGBHalf);
            _quarterVolumeLightTexture.name = "VolumeLightBufferQuarter";
            _quarterVolumeLightTexture.filterMode = FilterMode.Bilinear;
            _quarterDepthBuffer = new RenderTexture(pixelWidth / 4, pixelHeight / 4, 0, RenderTextureFormat.RFloat);
            _quarterDepthBuffer.name = "VolumeLightQuarterDepth";
            _quarterDepthBuffer.filterMode = FilterMode.Point;
            _quarterDepthBuffer.Create();
        }
    }

    public void OnPreRender()
    {
        Matrix4x4 proj = Matrix4x4.Perspective(_camera.fieldOfView, _camera.aspect, 0.01f, _camera.farClipPlane);
        proj = GL.GetGPUProjectionMatrix(proj, true);
        _viewProj = proj * _camera.worldToCameraMatrix;
        _preLightPass.Clear();
        bool flag = SystemInfo.graphicsShaderLevel > 40;
        RenderTargetIdentifier srcDepth = new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive);

        if (Resolution == VolumtericResolution.Quarter)
        {
            _preLightPass.Blit(srcDepth, _halfDepthBuffer, _bilateralBlurMaterial, (!flag) ? 10 : 4);
            _preLightPass.Blit(srcDepth, _quarterDepthBuffer, _bilateralBlurMaterial, (!flag) ? 11 : 6);
            _preLightPass.SetRenderTarget(_quarterVolumeLightTexture);
        }
        else if (Resolution == VolumtericResolution.Half)
        {
            _preLightPass.Blit(srcDepth, _halfDepthBuffer, _bilateralBlurMaterial, (!flag) ? 10 : 4);
            _preLightPass.SetRenderTarget(_halfVolumeLightTexture);
        }
        else
        {
            _preLightPass.SetRenderTarget(_volumeLightTexture);
        }
        _preLightPass.ClearRenderTarget(false, true, new Color(0f, 0f, 0f, 1f));
        UpdateMaterialParameters();
        if (VolumetricLightRenderer.PreRenderEvent != null)
        {
            VolumetricLightRenderer.PreRenderEvent(this, _viewProj);
        }
    }

    [ImageEffectOpaque]
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (Resolution == VolumtericResolution.Quarter)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(_quarterDepthBuffer.width, _quarterDepthBuffer.height, 0, RenderTextureFormat.ARGBHalf);
            temporary.filterMode = FilterMode.Bilinear;
            Graphics.Blit(_quarterVolumeLightTexture, temporary, _bilateralBlurMaterial, 8);
            Graphics.Blit(temporary, _quarterVolumeLightTexture, _bilateralBlurMaterial, 9);
            Graphics.Blit(_quarterVolumeLightTexture, _volumeLightTexture, _bilateralBlurMaterial, 7);
            RenderTexture.ReleaseTemporary(temporary);
        }
        else if (Resolution == VolumtericResolution.Half)
        {
            RenderTexture temporary2 = RenderTexture.GetTemporary(_halfVolumeLightTexture.width, _halfVolumeLightTexture.height, 0, RenderTextureFormat.ARGBHalf);
            temporary2.filterMode = FilterMode.Bilinear;
            Graphics.Blit(_halfVolumeLightTexture, temporary2, _bilateralBlurMaterial, 2);
            Graphics.Blit(temporary2, _halfVolumeLightTexture, _bilateralBlurMaterial, 3);
            Graphics.Blit(_halfVolumeLightTexture, _volumeLightTexture, _bilateralBlurMaterial, 5);
            RenderTexture.ReleaseTemporary(temporary2);
        }
        else
        {
            RenderTexture temporary3 = RenderTexture.GetTemporary(_volumeLightTexture.width, _volumeLightTexture.height, 0, RenderTextureFormat.ARGBHalf);
            temporary3.filterMode = FilterMode.Bilinear;
            Graphics.Blit(_volumeLightTexture, temporary3, _bilateralBlurMaterial, 0);
            Graphics.Blit(temporary3, _volumeLightTexture, _bilateralBlurMaterial, 1);
            RenderTexture.ReleaseTemporary(temporary3);
        }
        _blitAddMaterial.SetTexture("_Source", source);
        Graphics.Blit(_volumeLightTexture, destination, _blitAddMaterial, 0);
    }

    private void UpdateMaterialParameters()
    {
        _bilateralBlurMaterial.SetTexture("_HalfResDepthBuffer", _halfDepthBuffer);
        _bilateralBlurMaterial.SetTexture("_HalfResColor", _halfVolumeLightTexture);
        _bilateralBlurMaterial.SetTexture("_QuarterResDepthBuffer", _quarterDepthBuffer);
        _bilateralBlurMaterial.SetTexture("_QuarterResColor", _quarterVolumeLightTexture);
        Shader.SetGlobalTexture("_DitherTexture", _ditheringTexture);
        Shader.SetGlobalTexture("_NoiseTexture", _noiseTexture);
    }

    private void Update()
    {
        if (_currentResolution != Resolution)
        {
            _currentResolution = Resolution;
            ChangeResolution();
        }
        if (_volumeLightTexture.width != _camera.pixelWidth || _volumeLightTexture.height != _camera.pixelHeight)
        {
            ChangeResolution();
        }
    }

    private void LoadNoise3dTexture()
    {
        TextAsset textAsset = Resources.Load("NoiseVolume") as TextAsset;
        if (textAsset == null) return;
        byte[] bytes = textAsset.bytes;
        if (bytes.Length < 92) return;
        uint num = BitConverter.ToUInt32(bytes, 12);
        uint num2 = BitConverter.ToUInt32(bytes, 16);
        uint num3 = BitConverter.ToUInt32(bytes, 20);
        uint num4 = BitConverter.ToUInt32(bytes, 24);
        uint num5 = BitConverter.ToUInt32(bytes, 80);
        uint num6 = BitConverter.ToUInt32(bytes, 88);
        if (num6 == 0)
        {
            num6 = num3 / num2 * 8;
        }
        _noiseTexture = new Texture3D((int)num2, (int)num, (int)num4, TextureFormat.RGBA32, false);
        _noiseTexture.name = "3D Noise";
        Color[] array = new Color[num2 * num * num4];
        uint num7 = 128u;
        if (bytes[84] == 68 && bytes[85] == 88 && bytes[86] == 49 && bytes[87] == 48 && (num5 & 4) != 0)
        {
            uint num8 = BitConverter.ToUInt32(bytes, (int)num7);
            if (num8 >= 60 && num8 <= 65) num6 = 8u;
            else if (num8 >= 48 && num8 <= 52) num6 = 16u;
            else if (num8 >= 27 && num8 <= 32) num6 = 32u;
            num7 += 20;
        }
        uint num9 = num6 / 8;
        num3 = (num2 * num6 + 7) / 8;
        for (int i = 0; i < num4; i++)
        {
            for (int j = 0; j < num; j++)
            {
                for (int k = 0; k < num2; k++)
                {
                    float num10 = (float)(int)bytes[num7 + k * num9] / 255f;
                    array[k + j * num2 + i * num2 * num] = new Color(num10, num10, num10, num10);
                }
                num7 += num3;
            }
        }
        _noiseTexture.SetPixels(array);
        _noiseTexture.Apply();
    }

    private void GenerateDitherTexture()
    {
        if (_ditheringTexture != null) return;
        int num = 8;
        _ditheringTexture = new Texture2D(num, num, TextureFormat.Alpha8, false, true);
        _ditheringTexture.filterMode = FilterMode.Point;
        Color32[] array = new Color32[64] {
            new Color32(3,3,3,3), new Color32(192,192,192,192), new Color32(51,51,51,51), new Color32(239,239,239,239),
            new Color32(15,15,15,15), new Color32(204,204,204,204), new Color32(62,62,62,62), new Color32(251,251,251,251),
            new Color32(129,129,129,129), new Color32(66,66,66,66), new Color32(176,176,176,176), new Color32(113,113,113,113),
            new Color32(141,141,141,141), new Color32(78,78,78,78), new Color32(188,188,188,188), new Color32(125,125,125,125),
            new Color32(35,35,35,35), new Color32(223,223,223,223), new Color32(19,19,19,19), new Color32(207,207,207,207),
            new Color32(47,47,47,47), new Color32(235,235,235,235), new Color32(31,31,31,31), new Color32(219,219,219,219),
            new Color32(160,160,160,160), new Color32(98,98,98,98), new Color32(145,145,145,145), new Color32(82,82,82,82),
            new Color32(172,172,172,172), new Color32(109,109,109,109), new Color32(156,156,156,156), new Color32(94,94,94,94),
            new Color32(11,11,11,11), new Color32(200,200,200,200), new Color32(58,58,58,58), new Color32(247,247,247,247),
            new Color32(7,7,7,7), new Color32(196,196,196,196), new Color32(54,54,54,54), new Color32(243,243,243,243),
            new Color32(137,137,137,137), new Color32(74,74,74,74), new Color32(184,184,184,184), new Color32(121,121,121,121),
            new Color32(133,133,133,133), new Color32(70,70,70,70), new Color32(180,180,180,180), new Color32(117,117,117,117),
            new Color32(43,43,43,43), new Color32(231,231,231,231), new Color32(27,27,27,27), new Color32(215,215,215,215),
            new Color32(39,39,39,39), new Color32(227,227,227,227), new Color32(23,23,23,23), new Color32(211,211,211,211),
            new Color32(168,168,168,168), new Color32(105,105,105,105), new Color32(153,153,153,153), new Color32(90,90,90,90),
            new Color32(164,164,164,164), new Color32(102,102,102,102), new Color32(149,149,149,149), new Color32(86,86,86,86)
        };
        _ditheringTexture.SetPixels32(array);
        _ditheringTexture.Apply();
    }

    private Mesh CreateSpotLightMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] array = new Vector3[50];
        Color32[] array2 = new Color32[50];
        array[0] = new Vector3(0f, 0f, 0f);
        array[1] = new Vector3(0f, 0f, 1f);
        float num = 0f;
        float num2 = (float)Math.PI / 8f;
        float num3 = 0.9f;
        for (int i = 0; i < 16; i++)
        {
            array[i + 2] = new Vector3((0f - Mathf.Cos(num)) * num3, Mathf.Sin(num) * num3, num3);
            array2[i + 2] = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
            array[i + 2 + 16] = new Vector3(0f - Mathf.Cos(num), Mathf.Sin(num), 1f);
            array2[i + 2 + 16] = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, 0);
            array[i + 2 + 32] = new Vector3((0f - Mathf.Cos(num)) * num3, Mathf.Sin(num) * num3, 1f);
            array2[i + 2 + 32] = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
            num += num2;
        }
        mesh.vertices = array;
        mesh.colors32 = array2;
        int[] array3 = new int[288];
        int num4 = 0;
        for (int j = 2; j < 17; j++)
        {
            array3[num4++] = 0;
            array3[num4++] = j;
            array3[num4++] = j + 1;
        }
        array3[num4++] = 0;
        array3[num4++] = 17;
        array3[num4++] = 2;
        for (int k = 2; k < 17; k++)
        {
            array3[num4++] = k;
            array3[num4++] = k + 16;
            array3[num4++] = k + 1;
            array3[num4++] = k + 1;
            array3[num4++] = k + 16;
            array3[num4++] = k + 16 + 1;
        }
        array3[num4++] = 2;
        array3[num4++] = 17;
        array3[num4++] = 18;
        array3[num4++] = 18;
        array3[num4++] = 17;
        array3[num4++] = 33;
        for (int l = 18; l < 33; l++)
        {
            array3[num4++] = l;
            array3[num4++] = l + 16;
            array3[num4++] = l + 1;
            array3[num4++] = l + 1;
            array3[num4++] = l + 16;
            array3[num4++] = l + 16 + 1;
        }
        array3[num4++] = 18;
        array3[num4++] = 33;
        array3[num4++] = 34;
        array3[num4++] = 34;
        array3[num4++] = 33;
        array3[num4++] = 49;
        for (int m = 34; m < 49; m++)
        {
            array3[num4++] = 1;
            array3[num4++] = m + 1;
            array3[num4++] = m;
        }
        array3[num4++] = 1;
        array3[num4++] = 34;
        array3[num4++] = 49;
        mesh.triangles = array3;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }
}
