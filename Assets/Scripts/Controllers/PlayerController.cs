using Assets.Scripts.Interactions;
using UnityEngine;

[RequireComponent(typeof(DamageHandler))]
[RequireComponent(typeof(DamageSource))]
public class PlayerController : MonoBehaviour
{
    private void Awake()
    {
        var damageTaker = GetComponent<DamageHandler>();
        damageTaker.Subscribe(TakeDamage);

        var damageDealer = GetComponent<DamageSource>();
        damageDealer.Subscribe(DamageCallback);
    }

    public bool TakeDamage(DamageInteraction damageInteraction)
    {
        Debug.Log("Ранен!");
        return true;
    }

    public bool DamageCallback(DamageInteraction damageInteraction)
    {

        return true;
    }
}
