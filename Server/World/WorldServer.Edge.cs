using Lidgren.Network;

// Open world: the invisible wall at the edge of the world.
//
// The client's own walls are drawn rocks (every wall tag has art), so the border is kept by the server instead: the client reports
// where its plane is, and a plane closer than edgeMargin units to the border is stopped and put back inside
// (ClientTeleportToCoordinatesAfterTime with no velocity), ten times per second. Reconstruction: the original server did this
// with its own "off map" rule (WorldMaker.IsPlayerOffMap, radius + 20), whose consequence is not in the client.
public static partial class WorldServer
{
    private const float PushBackDistance = 14f;       // how far inside the limit the plane is put
    private const double PushCooldown = 0.5;          // a plane is not pushed again before the first push reached the client
    private const double WarnEvery = 8.0;

    private static void EnforceEdge(Session s, double now)
    {
        WorldSettings settings = WorldSettingsFile.Current;
        if (!settings.EdgeWall || s.Map == null) return;
        float limit = s.Radius - settings.EdgeMargin;
        float limit2 = limit * limit;

        foreach (NetConnection connection in Connected(s))
        {
            long peer = connection.RemoteUniqueIdentifier;
            if (!Game.GetPlane(peer, out float x, out float y, out bool alive, out _) || !alive) continue;
            float d2 = x * x + y * y;
            if (d2 <= limit2) continue;
            if (s.EdgePushed.TryGetValue(peer, out double last) && now - last < PushCooldown) continue;
            s.EdgePushed[peer] = now;

            float distance = MathF.Sqrt(d2);
            float scale = Math.Max(0f, limit - PushBackDistance) / distance;
            // A rotation outside +-360 keeps the plane's own.
            WorldObjects.SendTeleport(s.Server, connection, x * scale, y * scale, 999f, 0f);

            if (!s.EdgeWarned.TryGetValue(peer, out double warned) || now - warned >= WarnEvery)
            {
                s.EdgeWarned[peer] = now;
                WorldObjects.SendChat(s.Server, connection, "You cannot fly past the edge of the world.");
            }
        }
    }
}
