-- @id: npc.bandit_leader.combat_start
-- @attachment: Npc
-- @capabilities: world.read

function on_combat_started(context, self)
    context:log(self.name .. " shouts: Leave your coin and walk away!")
end
