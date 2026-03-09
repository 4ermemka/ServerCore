using Assets.Shared.Systems.InteractionSystem;

namespace Assets.Scripts.Interactions.Abstract
{
    public abstract class Interaction<TSelf, TSource, TTarget> : IInteraction<TSource, TTarget>
    where TSelf : Interaction<TSelf, TSource, TTarget>
    where TSource : IInteractionSource<TSelf>
    where TTarget : IInteractionHandler<TSelf>
    {
        public TSource Source { get; set; }
        public TTarget Target { get; set; }

        public string Trace { get => $"[{this.GetType().Name}] Source: [{Source.Name}], Target: [{Target.Name}]"; }

        public virtual (bool isCorrect, string description) Execute()
        {
            var result = TargetHandle();
            if (!result)
            {
                return (result, $"{Trace} TargetHandle result is false!");
            }

            result = SourceCallback();
            if (!result)
            {
                return (result, $"{Trace} SourceCallback result is false!");
            }

            return (result, "Success");
        }

        public virtual bool TargetHandle()
        {
            if (Target != null)
            {
                return Target.HandleInteraction((TSelf)this);
            }
            return false;
        }

        public virtual bool SourceCallback()
        {
            if (Source != null)
            {
                return Source.HandleCallBack((TSelf)this);
            }
            return false;
        }

        public override string ToString() => Trace;
    }
}
