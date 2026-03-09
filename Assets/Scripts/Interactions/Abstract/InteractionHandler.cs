using Assets.Shared.Systems.InteractionSystem;
using System;
using UnityEngine;

namespace Assets.Scripts.Interactions.Abstract
{
    public abstract class InteractionHandler<InteractionT> : MonoBehaviour, IInteractionHandler<InteractionT>, IDisposable
        where InteractionT : IInteraction
    {
        Func<InteractionT, bool> Execute { get; set; }

        public void Subscribe(Func<InteractionT, bool> executor)
        {
            Execute = executor;
        }

        public void Unsubscribe()
        {
            Execute = null;
        }

        public bool HandleInteraction(InteractionT interaction)
        {
            return Execute?.Invoke(interaction) == true;
        }

        public void Dispose()
        {
            Execute = null;
        }
    }
}
