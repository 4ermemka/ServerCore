using Assets.Scripts.Interactions.Abstract;
using Assets.Shared.Systems.InteractionSystem;

namespace Assets.Scripts.Interactions
{
    public class DamageInteraction : Interaction<DamageInteraction, DamageSource, DamageHandler>
    {
        public int Damage { get; set; }

        public DamageInteraction()
        {
        }
    }
} 