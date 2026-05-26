using UnityEngine;

public class SkyboxFollower : MonoBehaviour
{
    [SerializeField] private Transform targetCamera;

    public static bool iAm939;

    private void Start()
    {
        // Automatically find the Main Camera if it wasn't assigned manually
        if (targetCamera == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                targetCamera = mainCam.transform;
            }
            else
            {
                Debug.LogWarning("[SkyboxFollower] Main Camera not found in the scene! Please ensure your camera has the 'MainCamera' tag.");
            }
        }
    }

    private void Update()
    {
        // Safety check to prevent NullReferenceException if no camera exists
        if (targetCamera == null) return;

        if (iAm939 || targetCamera.position.y < 800f)
        {
            base.transform.position = Vector3.down * 12345f;
        }
        else
        {
            base.transform.position = targetCamera.position;
        }
    }
}