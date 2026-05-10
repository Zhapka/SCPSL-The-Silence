using UnityEngine;

public class SkyboxFollower : MonoBehaviour
{
    [Header("Ssilky")]
    public Transform playerCamera;
    public Material normalSkybox;
    [Header("Colors")]
    public Color darkColor = Color.black;
    public static bool iAm939;

    private void Update()
    {
        if (playerCamera == null) return;
        if (iAm939 || playerCamera.position.y < 800f)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = darkColor;
        }
        else
        {
            RenderSettings.skybox = normalSkybox;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        }
    }
}
