using Assets.Shared.Systems.InteractionSystem;
using System;
using UnityEngine;

namespace Assets.Scripts.Interactions.Abstract
{
    public class InteractionSource<InteractionT> : MonoBehaviour, IInteractionSource<InteractionT>, IDisposable
        where InteractionT : IInteraction
    {
        Func<InteractionT, bool> Callback { get; set; }

        public void Subscribe(Func<InteractionT, bool> callback)
        {
            Callback = callback;
        }

        public void Unsubscribe()
        {
            Callback = null;
        }

        public bool HandleCallBack(InteractionT interaction)
        {
            return Callback?.Invoke(interaction) == true;
        }

        public void Dispose()
        {
            Unsubscribe();
        }
    }
}
