using UnityEngine;

public class EdgePushMouseLook : MonoBehaviour
{
    [Header("Границы поворота (в градусах)")]
    [Range(0f, 45f)] public float maxHorizontalAngle = 25f;
    [Range(0f, 45f)] public float maxVerticalAngle = 15f;

    [Header("Настройки краев экрана")]
    [Tooltip("Размер мертвой зоны в центре (от 0 до 1). 0.7 значит, что реакция начнется на последних 15% у краев")]
    [Range(0.1f, 0.9f)] public float deadZoneRadius = 0.7f;

    [Tooltip("Скорость, с которой камера возвращается или наклоняется")]
    public float smoothSpeed = 4f;

    private float currentYaw = 0f;
    private float currentPitch = 0f;

    void Start()
    {
        // Курсор полностью свободен и виден
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void LateUpdate()
    {
        // 1. Переводим позицию мыши в диапазон от -1 до 1 (0 - центр экрана)
        float mouseX = (Input.mousePosition.x / Screen.width) * 2f - 1f;
        float mouseY = (Input.mousePosition.y / Screen.height) * 2f - 1f;

        float targetYaw = 0f;
        float targetPitch = 0f;

        // 2. Логика для горизонтальной оси (Лево / Право)
        if (Mathf.Abs(mouseX) > deadZoneRadius)
        {
            // Считаем, насколько сильно мышь зашла за пределы мертвой зоны
            float factor = (Mathf.Abs(mouseX) - deadZoneRadius) / (1f - deadZoneRadius);
            targetYaw = Mathf.Sign(mouseX) * factor * maxHorizontalAngle;
        }

        // 3. Логика для вертикальной оси (Верх / Низ)
        if (Mathf.Abs(mouseY) > deadZoneRadius)
        {
            // Считаем, насколько сильно мышь зашла за пределы мертвой зоны
            float factor = (Mathf.Abs(mouseY) - deadZoneRadius) / (1f - deadZoneRadius);
            targetPitch = -Mathf.Sign(mouseY) * factor * maxVerticalAngle; // Минус для инверсии
        }

        // 4. Плавно сглаживаем углы
        currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.deltaTime * smoothSpeed);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * smoothSpeed);

        // 5. Накладываем получившийся наклон поверх вашего Аниматора
        transform.localRotation = transform.localRotation * Quaternion.Euler(currentPitch, currentYaw, 0f);
    }
}
