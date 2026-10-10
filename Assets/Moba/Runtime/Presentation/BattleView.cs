using System.Collections.Generic;
using Moba.Core;
using UnityEngine;

namespace Moba.Presentation
{
    public sealed class BattleView : MonoBehaviour
    {
        public Camera BattleCamera;
        public EntityView HeroPrefab, RobotHeroPrefab, MinionPrefab, TowerPrefab;
        public uint SelectedId = 1;
        private readonly Dictionary<uint, EntityView> views = new Dictionary<uint, EntityView>();
        private CombatWorld world;
        private float age;

        public static Vector3 Logical(Int2 point)
            => new Vector3(point.X / 1000f, 0, point.Z / 1000f);

        public bool TryGetEntityView(uint id, out EntityView view)
            => views.TryGetValue(id, out view);

        public void Bind(CombatWorld value)
        {
            foreach (var item in views.Values) Destroy(item.gameObject);
            views.Clear();
            world = value;
            if (world != null) Refresh();
        }

        public void Refresh()
        {
            if (world == null) return;
            var present = new HashSet<uint>();
            foreach (var entity in world.State.Entities)
            {
                present.Add(entity.EntityId);
                if (!views.TryGetValue(entity.EntityId, out var view))
                {
                    var prefab = entity.Kind == EntityKind.Tower ? TowerPrefab :
                        entity.Kind == EntityKind.Minion ? MinionPrefab :
                        entity.Kind == EntityKind.RobotHero ? RobotHeroPrefab : HeroPrefab;
                    view = Instantiate(prefab, transform);
                    views.Add(entity.EntityId, view);
                }
                view.Snapshot(entity);
            }
            var removed = new List<uint>();
            foreach (var pair in views)
                if (!present.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var id in removed)
            {
                Destroy(views[id].gameObject);
                views.Remove(id);
            }
            age = 0;
        }

        private void LateUpdate()
        {
            age = Mathf.Min(age + Time.unscaledDeltaTime, 1f / BattleConfig.LogicHz);
            float alpha = age * BattleConfig.LogicHz;
            foreach (var view in views.Values) view.Render(alpha);
        }
    }
}