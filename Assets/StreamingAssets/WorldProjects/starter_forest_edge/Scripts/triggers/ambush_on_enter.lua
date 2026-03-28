-- @id: trigger.ambush.on_enter
-- @attachment: Trigger
-- @capabilities: encounter.control

function on_enter(context, actor)
    context:start_encounter("enc_bandit_01")
end
