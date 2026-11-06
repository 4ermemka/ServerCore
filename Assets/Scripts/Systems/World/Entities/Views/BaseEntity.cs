using UnityEngine;

namespace ServerCore.World.Entities
{
    /// <summary>Entity roots bind pure world data to their scene representation.</summary>
    public abstract class BaseEntity : WorldObjectView
    {
        // The model is a child; the root is the data pose, independent of prefab artwork scale.
        protected override void GetAuthorTransform(out Quaternion rotation, out Vector3 scale)
        {
            rotation = Quaternion.identity;
            scale = Vector3.one;
        }
    }
}
