using UnityEngine;

public class MaterialBlink : MonoBehaviour
{
    // Теперь ссылка приватная, скрипт сам найдет материал
    private Material material;

    public Color lowestColor = Color.white;

    public Color highestColor = Color.white;

    public float speed = 1f;

    public float colorMultiplier = 1f;

    private float time;

    private void Start()
    {
        // Безопасно получаем локальный экземпляр материала объекта
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            material = renderer.material;
        }
        else
        {
            Debug.LogError($"[MaterialBlink] На объекте {name} отсутствует компонент Renderer!");
            enabled = false; // Отключаем скрипт, чтобы не спамить ошибками
        }
    }

    private void Update()
    {
        // Проверка на случай, если объект уничтожен сетью
        if (material == null) return;

        time += Time.deltaTime * speed;
        if (time > 1f)
        {
            time -= 1f;
        }
        material.SetColor("_EmissionColor", Color.Lerp(lowestColor, highestColor, Mathf.Abs(Mathf.Lerp(-1f, 1f, time))) * colorMultiplier);
    }

    private void OnDisable()
    {
        if (material != null)
        {
            material.SetColor("_EmissionColor", highestColor);
        }
    }
}
