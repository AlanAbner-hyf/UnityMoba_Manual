using Moba.Core;
using UnityEngine;

namespace Moba.Presentation
{
    public sealed class EntityView : MonoBehaviour
    {
        public Renderer Body;
        public uint EntityId => Entity.EntityId;
        public EntityState Entity { get; private set; }
        private Vector3 from, to;
        private bool initialized;
        private MaterialPropertyBlock colors;

        public void Snapshot(EntityState entity)
        {
            Entity = entity;
            var point = new Vector3(entity.Position.X / 1000f, 0,
                                    entity.Position.Z / 1000f);
            from = initialized ? to : point;
            to = point;
            initialized = true;
            if (Body != null)
            {
                // Create native-backed Unity objects during runtime, not field initialization.
                if (colors == null) colors = new MaterialPropertyBlock();
                colors.SetColor("_BaseColor",
                    entity.Team == Team.Blue ? Color.cyan : Color.red);
                Body.SetPropertyBlock(colors);
                Body.enabled = entity.Alive;
            }
            foreach (var collider in GetComponentsInChildren<Collider>())
                collider.enabled = entity.Alive;
        }

        public void Render(float alpha)
        {
            transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(alpha));
        }
    }
}
