using Lidgren.Network;

public static partial class Tutorial
{
    // What the script engine asks of the tutorial session (Scripting\MapScriptRunner.cs): the units go to this player,
    // the gates are the crate walls of the session, the exit carrier is the tutorial's.
    private sealed class Host : IScriptHost
    {
        private readonly Session sess;

        public Host(Session sess)
        {
            this.sess = sess;
        }

        public NetServer Server => sess.Server;
        public NetConnection Connection => sess.Connection;
        public string Role => Tutorial.Role;
        public Maps.Map Map => sess.Map;

        public void Log(string text) => mapLog("TUTORIAL", "[tutorial] " + text);

        public void Chat(string text) => WorldObjects.SendChat(sess.Server, sess.Connection, text);

        public void PlaySound(string name) => Sounds.Play(sess.Server, sess.Connection, name);

        public void LivePlayers(List<(float X, float Y)> into)
        {
            into.Clear();
            if (Game.GetPlane(sess.Peer, out float x, out float y, out bool alive, out _) && alive) into.Add((x, y));
        }

        public int CreateUnit(string type, float a, float b, float rotation, string group, Ground.Rect? area, bool? invulnerable, double now)
        {
            Ground.ToWorld(a, b, out float x, out float y);
            return Enemies.CreateFromScript(sess.Server, sess.Connection, Role, type, x, y, rotation, group, area, invulnerable, now, mapLog);
        }

        // Dialog: portrait, bubble and chat line; bubble: only the bubble over the unit.
        public void Speak(int shortId, string key, string portrait, bool dialog)
        {
            WorldObjects.SendSpeech(sess.Server, sess.Connection, shortId, key, 4f, dialog ? WorldObjects.SpeechDialog : WorldObjects.SpeechNoChat,
                portrait, Rng());
        }

        public bool GateOpen(string gate) => FindGate(sess, gate)?.Open ?? false;

        public bool GateDamageable(string gate) => FindGate(sess, gate)?.Damageable ?? false;

        public float GateB(string gate)
        {
            Gate? g = FindGate(sess, gate);
            return g == null ? 0f : g.Sj / (float)Ground.WallsPerCell;
        }

        public void GateUnlock(string gate, double now)
        {
            Gate? g = FindGate(sess, gate);
            if (g != null) UnlockGate(sess, g, now, mapLog);
        }

        public void GateOpenNow(string gate, bool explode, double now)
        {
            Gate? g = FindGate(sess, gate);
            if (g != null) OpenGate(sess, g, explode, now);
        }

        // The crate in the middle of the wall (it is the one that says "Shoot this to proceed!").
        public int GateSpeaker(string gate)
        {
            Gate? g = FindGate(sess, gate);
            if (g == null) return 0;
            List<int> alive = g.Pieces.Where(p => p.ShortId != 0 && Enemies.IsAlive(sess.Connection, p.ShortId)).Select(p => p.ShortId).ToList();
            return alive.Count > 0 ? alive[alive.Count / 2] : 0;
        }

        public void ExitPortal(string point, float autoExitSeconds, double now)
        {
            Maps.Point p = sess.Map.GetPoint(point);
            CreatePortal(sess, p.A, p.B, now, mapLog, autoExitSeconds > 0f ? autoExitSeconds : AutoExitAt);
        }

        public bool RoomCleared(string room) => false;

        public bool TypeDead(string type) => false;
    }
}
