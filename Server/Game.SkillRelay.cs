using Lidgren.Network;

// TriggerOtherPlayerSkill (65043): a skill cloud of another player touched something on THIS player's client.
//
// The cloud was made by player A and exists on every client of the room (PlayerRelay). When it triggers a skill condition on a unit,
// the client that sees it (B) is not the cloud's owner, so it cannot fire A's pilot skills (CloudScript.HandleSkillTriggers); it sends
// TriggerOtherPlayerSkill(trigger, amount, otherPlayerShortId) with the owner's unit id (the relay id B knows). The server forwards it
// to A as TriggerPlayerSkill (65038), which runs A's pilot skills for that trigger (GameClient.TriggerPlayerSkill -> Pilot.OnTrigger).
public static partial class Game
{
    private const ushort MsgTriggerOtherPlayerSkill = 65043;

    private static void HandleTriggerOtherPlayerSkill(NetIncomingMessage msg, Player j, int count, Action<string, string> log)
    {
        byte trigger = msg.ReadByte();
        float amount = msg.ReadSingle();
        int otherShortId = msg.ReadInt32();

        bool sent = PlayerRelay.ForwardSkillTrigger(msg.SenderConnection, trigger, amount, otherShortId, out string owner);
        if (count <= 3 || count % 100 == 0)
        {
            log("SKILL", j.Role + " TriggerOtherPlayerSkill " + CopperReader.Name(CopperReader.TriggerNames, trigger) + " (" +
                CopperReader.Num(amount) + ") for unit " + otherShortId + " -> " +
                (sent ? "forwarded to " + owner : "no player of this room has that unit (ignored)") + " x" + count);
        }
    }
}
