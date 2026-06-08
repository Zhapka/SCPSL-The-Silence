using UnityEngine;
using UnityEngine.Networking;

public class FireZone : MonoBehaviour
{
    // Урон в секунду
    public float damagePerSecond = 2f;
    private float timer = 0f;

    private void OnTriggerStay(Collider other)
    {
        if (!NetworkServer.active) return;

        timer += Time.deltaTime;
        if (timer >= 1f)
        {
            PlayerStats stats = other.GetComponentInParent<PlayerStats>();
            CharacterClassManager ccm = other.GetComponentInParent<CharacterClassManager>();
            if (stats != null && ccm != null && ccm.klasy[ccm.curClass].team != Team.SCP)
            {
                stats.HurtPlayer(new PlayerStats.HitInfo(damagePerSecond, "SCP-457_Aura", DamageTypes.Scp457, 0), other.gameObject);
            }

            timer = 0f;
        }
    }
}