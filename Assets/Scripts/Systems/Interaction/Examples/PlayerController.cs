using Assets.Scripts.Interactions;
using Assets.Scripts.Managers;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(DamageHandler))]
[RequireComponent(typeof(DamageSource))]
public class PlayerController : MonoBehaviour, IPointerClickHandler
{
    private DamageHandler damageHandler;
    private DamageSource damageSource;

    private void Awake()
    {
        damageHandler = GetComponent<DamageHandler>();
        damageHandler.Subscribe(TakeDamage);

        damageSource = GetComponent<DamageSource>();
        damageSource.Subscribe(DamageCallback);
    }

    public void OnDestroy()
    {
        damageHandler.Unsubscribe();
        damageSource.Unsubscribe();
    }

    public bool TakeDamage(DamageInteraction damageInteraction)
    {
        Debug.Log($"Ранен! Объем: {damageInteraction.Damage}");
        return true;
    }

    public bool DamageCallback(DamageInteraction damageInteraction)
    {
        Debug.Log($"Я ударил! Объем: {damageInteraction.Damage}");
        return true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        InteractionsPipe.Instance.AddInteraction(new DamageInteraction()
        {
            Damage = 5,
            Source = damageSource,
            Target = damageHandler
        });
    }
}
