using RemoteAdmin;
using UnityEngine;
using UnityEngine.Networking;

public class Scp457PlayerScript : NetworkBehaviour
{
    [Header("Player Properties")]
    public GameObject plyCam;
    public bool iAm457;

    [Header("Attack Settings")]
    public float attackDistance = 2.0f;
    public LayerMask attackMask;
    public AudioClip[] attackSounds;

    private float cooldown;

    private void Update()
    {
        if (isLocalPlayer)
        {
            // ќбновление состо€ни€ (можно оптимизировать через событие смены класса)
            iAm457 = GetComponent<CharacterClassManager>().klasy[GetComponent<CharacterClassManager>().curClass].fullName.Contains("457");

            if (iAm457 && Input.GetKey(NewInput.GetKey("Shoot")))
            {
                CmdShoot();
            }
        }

        if (isServer && cooldown > 0f)
        {
            cooldown -= Time.deltaTime;
        }
    }

    [Command]

    public void Init(int classID, Class c)
    {
        iAm457 = c.fullName.Contains("457");

        if (base.isLocalPlayer)
        {
        }
    }
    private void CmdShoot()
    {
        // “еперь используем plyCam напр€мую, переданный через инспектор
        RaycastHit hit;
        if (Physics.Raycast(new Ray(plyCam.transform.position, plyCam.transform.forward), out hit, attackDistance, attackMask))
        {
            PlayerStats targetStats = hit.collider.GetComponentInParent<PlayerStats>();
            CharacterClassManager targetCCM = hit.collider.GetComponentInParent<CharacterClassManager>();

            if (targetStats != null && targetCCM != null && targetCCM.klasy[targetCCM.curClass].team != Team.SCP && cooldown <= 0f)
            {
                cooldown = 1f;
                targetStats.HurtPlayer(new PlayerStats.HitInfo(25f, GetComponent<NicknameSync>().myNick + " (SCP-457)", DamageTypes.Scp457, GetComponent<QueryProcessor>().PlayerId), hit.collider.gameObject);
                GetComponent<CharacterClassManager>().CallRpcPlaceBlood(hit.point, 0, 2f);
                RpcShoot();
            }
        }
    }

    [ClientRpc]
    private void RpcShoot()
    {
        Animator component = GetComponent<CharacterClassManager>().myModel.GetComponent<Animator>();
        AudioSource source = component.GetComponent<AudioSource>();
        if (source != null && attackSounds.Length > 0)
        {
            source.PlayOneShot(attackSounds[Random.Range(0, attackSounds.Length)]);
        }

        if (base.isLocalPlayer)
        {
            Hitmarker.Hit(1.5f);
        }
        else
        {
            component.SetTrigger("Attack");
        }
    }
}