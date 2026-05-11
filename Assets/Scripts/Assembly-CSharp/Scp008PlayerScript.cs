using System;
using System.Collections.Generic;
using Dissonance.Integrations.UNet_HLAPI;
using MEC;
using RemoteAdmin;
using UnityEngine;
using UnityEngine.Networking;

public class Scp008PlayerScript : NetworkBehaviour
{
    [Header("Player Properties")]
    public Transform plyCam;

    public Animator animator;

    public bool iAm008;

    public bool sameClass;

    [Header("Attack")]
    public float distance = 2.4f;

    public int damage = 40;

    [Header("Boosts")]
    public AnimationCurve multiplier;

    [Header("SCP-008 Virus Settings")]
    [SyncVar] public float currentInfection;
    public float virusDamagePerSecond = 4f;

    private static int kCmdCmdHurtPlayer;

    private static int kCmdCmdShootAnim;

    private static int kRpcRpcShootAnim;

    private static int kRpcRpcSyncInfection;

    private void Start()
    {
        if (base.isLocalPlayer)
        {
            Timing.RunCoroutine(_UpdateInput(), Segment.FixedUpdate);
        }

        if (NetworkServer.active)
        {
            Timing.RunCoroutine(_ServerInfectionTick(), Segment.Update);
        }
    }

    public void Init(int classID, Class c)
    {
        sameClass = c.team == Team.SCP;
        iAm008 = classID == 16;
        if (animator != null && animator.gameObject != null)
        {
            animator.gameObject.SetActive(base.isLocalPlayer && iAm008);
        }
    }

    private IEnumerator<float> _UpdateInput()
    {
        while (this != null)
        {
            if (iAm008 && Input.GetKey(NewInput.GetKey("Shoot")))
            {
                float mt = multiplier.Evaluate(GetComponent<PlayerStats>().GetHealthPercent());
                CallCmdShootAnim();
                if (animator != null)
                {
                    animator.SetTrigger("Shoot");
                    animator.speed = mt;
                }
                yield return Timing.WaitForSeconds(0.65f / mt);
                Attack();
                yield return Timing.WaitForSeconds(1f / mt);
            }
            yield return 0f;
        }
    }

    private void Attack()
    {
        if (plyCam == null) return;

        RaycastHit hitInfo;
        if (Physics.Raycast(plyCam.transform.position, plyCam.transform.forward, out hitInfo, distance))
        {
            var target008 = hitInfo.transform.GetComponent<Scp008PlayerScript>();
            if (target008 == null)
            {
                target008 = hitInfo.transform.GetComponentInParent<Scp008PlayerScript>();
            }
            if (target008 != null && !target008.sameClass)
            {
                Hitmarker.Hit();
                CallCmdHurtPlayer(hitInfo.transform.gameObject, GetComponent<HlapiPlayer>().PlayerId);
            }
        }
    }

    private IEnumerator<float> _ServerInfectionTick()
    {
        while (this != null)
        {
            yield return Timing.WaitForSeconds(1f);

            if (currentInfection > 0f && !sameClass)
            {
                PlayerStats stats = GetComponent<PlayerStats>();
                CharacterClassManager ccm = GetComponent<CharacterClassManager>();

                if (stats != null && ccm != null && ccm.curClass != 2 && ccm.curClass != 16 && ccm.curClass != 10)
                {
                    stats.HurtPlayer(new PlayerStats.HitInfo(virusDamagePerSecond, "SCP-008 Infection", DamageTypes.Wall, 0), gameObject);

                    if (stats.health <= 0f)
                    {
                        currentInfection = 0f;
                        RoundSummary.changed_into_zombies++;
                        ccm.SetClassID(10);
                        stats.health = ccm.klasy[10].maxHP;
                    }
                }
            }
        }
    }

    [Command(channel = 2)]
    private void CmdHurtPlayer(GameObject ply, string id)
    {
        if (ply == null) return;

        if (Vector3.Distance(GetComponent<PlyMovementSync>().position, ply.transform.position) <= distance * 1.5f && iAm008)
        {
            Vector3 position = ply.transform.position;
            GetComponent<PlayerStats>().HurtPlayer(new PlayerStats.HitInfo(damage, GetComponent<NicknameSync>().myNick + " (SCP-008)", DamageTypes.Scp0492, GetComponent<QueryProcessor>().PlayerId), ply);
            GetComponent<CharacterClassManager>().CallRpcPlaceBlood(position, 0, (ply.GetComponent<CharacterClassManager>().curClass != 2) ? 0.5f : 1.3f);

            Scp008PlayerScript targetScript = ply.GetComponent<Scp008PlayerScript>();
            if (targetScript != null && !targetScript.sameClass)
            {
                targetScript.currentInfection = 100f;
                CallRpcSyncInfection(ply, 100f);
            }
        }
    }

    [Command(channel = 1)]
    private void CmdShootAnim()
    {
        CallRpcShootAnim();
    }

    public void CallRpcSyncInfection(GameObject target, float amount)
    {
        if (!NetworkServer.active) return;
        NetworkWriter networkWriter = new NetworkWriter();
        networkWriter.Write((short)0);
        networkWriter.Write((short)2);
        networkWriter.WritePackedUInt32((uint)kRpcRpcSyncInfection);
        networkWriter.Write(base.netId);
        networkWriter.Write(target);
        networkWriter.Write(amount);
        SendRPCInternal(networkWriter, 2, "RpcSyncInfection");
    }

    [ClientRpc]
    private void RpcShootAnim()
    {
        var animController = GetComponent<AnimationController>();
        if (animController != null)
        {
            animController.DoAnimation("Shoot");
        }
    }

    [ClientRpc(channel = 2)]
    private void RpcSyncInfection(GameObject target, float amount)
    {
        if (target == null) return;
        Scp008PlayerScript script = target.GetComponent<Scp008PlayerScript>();
        if (script != null)
        {
            script.currentInfection = amount;
        }
    }

    private void UNetVersion()
    {
    }

    protected static void InvokeCmdCmdHurtPlayer(NetworkBehaviour obj, NetworkReader reader)
    {
        if (!NetworkServer.active) return;
        ((Scp008PlayerScript)obj).CmdHurtPlayer(reader.ReadGameObject(), reader.ReadString());
    }

    protected static void InvokeCmdCmdShootAnim(NetworkBehaviour obj, NetworkReader reader)
    {
        if (!NetworkServer.active) return;
        ((Scp008PlayerScript)obj).CmdShootAnim();
    }

    protected static void InvokeRpcRpcSyncInfection(NetworkBehaviour obj, NetworkReader reader)
    {
        if (!NetworkClient.active) return;
        ((Scp008PlayerScript)obj).RpcSyncInfection(reader.ReadGameObject(), reader.ReadSingle());
    }

    public void CallCmdHurtPlayer(GameObject ply, string id)
    {
        if (!NetworkClient.active) return;
        if (base.isServer) { CmdHurtPlayer(ply, id); return; }
        NetworkWriter networkWriter = new NetworkWriter();
        networkWriter.Write((short)0); networkWriter.Write((short)5);
        networkWriter.WritePackedUInt32((uint)kCmdCmdHurtPlayer);
        networkWriter.Write(base.netId);
        networkWriter.Write(ply); networkWriter.Write(id);
        SendCommandInternal(networkWriter, 2, "CmdHurtPlayer");
    }

    public void CallCmdShootAnim()
    {
        if (!NetworkClient.active) return;
        if (base.isServer) { CmdShootAnim(); return; }
        NetworkWriter networkWriter = new NetworkWriter();
        networkWriter.Write((short)0); networkWriter.Write((short)5);
        networkWriter.WritePackedUInt32((uint)kCmdCmdShootAnim);
        networkWriter.Write(base.netId);
        SendCommandInternal(networkWriter, 1, "CmdShootAnim");
    }

    protected static void InvokeRpcRpcShootAnim(NetworkBehaviour obj, NetworkReader reader)
    {
        if (!NetworkClient.active) return;
        ((Scp008PlayerScript)obj).RpcShootAnim();
    }

    public void CallRpcShootAnim()
    {
        if (!NetworkServer.active) return;
        NetworkWriter networkWriter = new NetworkWriter();
        networkWriter.Write((short)0); networkWriter.Write((short)2);
        networkWriter.WritePackedUInt32((uint)kRpcRpcShootAnim);
        networkWriter.Write(base.netId);
        SendRPCInternal(networkWriter, 0, "RpcShootAnim");
    }

    static Scp008PlayerScript()
    {
        kCmdCmdHurtPlayer = 95412856;
        NetworkBehaviour.RegisterCommandDelegate(typeof(Scp008PlayerScript), kCmdCmdHurtPlayer, InvokeCmdCmdHurtPlayer);
        kCmdCmdShootAnim = -65412895;
        NetworkBehaviour.RegisterCommandDelegate(typeof(Scp008PlayerScript), kCmdCmdShootAnim, InvokeCmdCmdShootAnim);
        kRpcRpcShootAnim = 35412895;
        NetworkBehaviour.RegisterRpcDelegate(typeof(Scp008PlayerScript), kRpcRpcShootAnim, InvokeRpcRpcShootAnim);

        kRpcRpcSyncInfection = -78452149;
        NetworkBehaviour.RegisterRpcDelegate(typeof(Scp008PlayerScript), kRpcRpcSyncInfection, InvokeRpcRpcSyncInfection);

        NetworkCRC.RegisterBehaviour("Scp008PlayerScript", 0);
    }

    public override bool OnSerialize(NetworkWriter writer, bool forceAll)
    {
        return base.OnSerialize(writer, forceAll);
    }

    public override void OnDeserialize(NetworkReader reader, bool initialState)
    {
        base.OnDeserialize(reader, initialState);
    }
}
