using Moba.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Moba.Presentation
{
    public sealed class PlayerCommandInput : MonoBehaviour
    {
        public InputActionAsset ActionsAsset;
        public BattleView View;
        public bool EnableCombat;
        public CombatWorld World { get; set; }
        public uint ControlledId { get; set; } = 1;
        public bool CanControl { get; set; } = true;
        private InputActionAsset actions;
        private InputAction point, order, select, skill;
        private CombatCommand pending;
        private bool pendingSkill;

        private void Awake()
        {
            actions = Instantiate(ActionsAsset);
            point = actions.FindAction("Moba/Point", true);
            order = actions.FindAction("Moba/Order", true);
            select = actions.FindAction("Moba/Select", true);
            skill = actions.FindAction("Moba/Skill", true);
            actions.Enable();
        }

        private void Update()
        {
            if (World == null || View == null || View.BattleCamera == null) return;
            Vector2 mouse = point.ReadValue<Vector2>();
            if (CanControl && EnableCombat && skill.WasPressedThisFrame())
                pendingSkill = true;
            if (!View.BattleCamera.pixelRect.Contains(mouse)) return;
            if (!order.WasPressedThisFrame() && !select.WasPressedThisFrame()) return;
            var ray = View.BattleCamera.ScreenPointToRay(mouse);
            EntityView picked = Physics.Raycast(ray, out var hit, 250)
                ? hit.collider.GetComponentInParent<EntityView>() : null;
            if (select.WasPressedThisFrame() && picked != null)
                View.SelectedId = picked.EntityId;
            if (!CanControl || !order.WasPressedThisFrame()) return;
            var actor = World.FindEntity(ControlledId);
            if (actor == null || !actor.Alive) return;
            if (EnableCombat && picked != null && picked.Entity.Alive &&
                picked.Entity.Team != actor.Team)
                pending = new CombatCommand(CommandKind.Attack, default, picked.EntityId);
            else if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
            {
                Vector3 p = ray.GetPoint(distance);
                pending = CombatCommand.MoveTo(new Int2(
                    Mathf.Clamp(Mathf.RoundToInt(p.x * 1000), World.Config.MinX, World.Config.MaxX),
                    Mathf.Clamp(Mathf.RoundToInt(p.z * 1000), World.Config.MinZ, World.Config.MaxZ)));
            }
        }

        public CombatCommand Sample()
        {
            if (!CanControl) { ClearPending(); return CombatCommand.NoOp; }
            var result = pendingSkill ? new CombatCommand(CommandKind.CastArea) : pending;
            ClearPending();
            return result;
        }

        public void ClearPending() { pending = default; pendingSkill = false; }
        private void OnDestroy()
        {
            if (actions == null) return;
            actions.Disable();
            Destroy(actions);
        }
    }
}