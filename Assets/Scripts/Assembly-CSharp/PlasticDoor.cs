using UnityEngine;
using UnityEngine.Networking;

public class PlasticDoor : NetworkBehaviour
{
    private Animator _animator;

    [SyncVar(hook = nameof(OnDoorStateChanged))]
    public bool isOpen = false;

    private float _nextInteractTime = 0f;
    public float cooldown = 1.0f;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public void ChangeState()
    {
        if (!NetworkServer.active) return;
        if (Time.time < _nextInteractTime) return;

        _nextInteractTime = Time.time + cooldown;
        isOpen = !isOpen;
    }

    private void OnDoorStateChanged(bool newState)
    {
        isOpen = newState;
        if (_animator != null)
        {
            _animator.SetBool("isOpen", isOpen);
        }
    }
}