using UnityEngine;
using UnityEngine.Networking;

public class Scp008ZoneTrigger : MonoBehaviour
{
    [Header("Infection Settings")]
    public float infectionAmountPerSecond = 10f;

    [Header("Required Item ID (Hazmat/Heavy Armor)")]
    public int hazmatItemId = 26;

    private void OnTriggerStay(Collider other)
    {
        if (!NetworkServer.active) return;

        if (other.CompareTag("Player"))
        {
            CharacterClassManager ccm = other.GetComponent<CharacterClassManager>();

            if (ccm != null && ccm.curClass >= 0 && ccm.curClass < ccm.klasy.Length)
            {
                Team playerTeam = ccm.klasy[ccm.curClass].team;
                if (playerTeam != Team.SCP && playerTeam != Team.RIP)
                {
                    Inventory inv = other.GetComponent<Inventory>();
                    if (inv != null && HasHazmatProtection(inv))
                    {
                        return;
                    }

                    Scp008PlayerScript scp008 = other.GetComponent<Scp008PlayerScript>();
                    if (scp008 != null && scp008.currentInfection < 100f)
                    {
                        scp008.currentInfection += infectionAmountPerSecond * Time.deltaTime;
                        scp008.CallRpcSyncInfection(other.gameObject, scp008.currentInfection);
                    }
                }
            }
        }
    }

    private bool HasHazmatProtection(Inventory inv)
    {
        for (int i = 0; i < inv.items.Count; i++)
        {
            if (inv.items[i].id == hazmatItemId)
            {
                return true;
            }
        }
        return false;
    }
}
