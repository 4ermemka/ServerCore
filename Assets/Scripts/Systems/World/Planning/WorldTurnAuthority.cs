using System;
using System.Collections.Generic;
using Assets.Scripts.Network.NetCore;
using ServerCore.World.Data;
using Shared.TurnPlanning;
using Shared.WorldObjects;

namespace ServerCore.World.Planning
{
    /// <summary>Единственный писатель мира. Исполнение сначала проверяется на копии, затем публикуется одним патчем.</summary>
    public sealed class WorldTurnAuthority
    {
        private readonly WorldState world;
        private readonly Func<IGridSpace> grid;
        public TurnPlanner Planner { get; }
        public event Action<int> TurnExecuted;

        public WorldTurnAuthority(WorldState world, Func<IGridSpace> grid, TurnPlanner planner)
        { this.world = world; this.grid = grid; Planner = planner; }

        public string Apply(Guid client, PlanCommand command)
        {
            if (!Planner.TryApply(world.Planning.Value, new WorldPlanningAdapter(world, grid()), client, command, out var next, out var error))
                return error;
            if (Planner.CanExecute(next)) return Execute(next);
            world.Planning.Value = next;
            return null;
        }

        public string SetConnected(Guid client, bool connected)
        {
            var next = Planner.SetConnected(world.Planning.Value, client, connected, true);
            // Эта сборка всегда даёт роль ведущего, независимо от Host/Client.
            string error = null;
            if (Planner.CanExecute(next))
            {
                error = Execute(next);
                if (error == null) return null;
                TurnPlanner.ResetConfirmations(next);
            }
            world.Planning.Value = next;
            return error;
        }

        public string Execute(PlanningState planning)
        {
            var copy = new WorldState();
            copy.ApplySnapshot(JsonGameSerializer.Deserialize<Dictionary<string, object>>(JsonGameSerializer.Serialize(world.CreateSnapshot())));
            var adapter = new WorldPlanningAdapter(copy, grid());
            try
            {
                foreach (var step in Planner.OrderedActions(planning))
                {
                    if (!Planner.Actions.TryGet(step.ActionId, out var action)) return "Обработчик действия недоступен: " + step.ActionId;
                    var error = action.Validate(adapter, step);
                    if (error != null) return error;
                    action.Execute(adapter, step);
                }
            }
            catch (Exception ex) { return "Ход не исполнен: " + ex.Message; }
            copy.Planning.Value = Planner.NextTurn(planning);
            world.PublishSnapshot(copy.CreateSnapshot());
            TurnExecuted?.Invoke(planning.Turn);
            return null;
        }
    }
}
