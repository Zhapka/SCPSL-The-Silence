using UnityEngine;

public class Scp457ReScaler : MonoBehaviour
{
    void Start()
    {
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);

        foreach (GameObject obj in allObjects)
        {
            if (obj.name == "SCP-457")
            {
                Transform bodyChild = obj.transform.Find("Body");
                if (bodyChild != null)
                {
                    obj.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                }
            }
        }
    }
}
