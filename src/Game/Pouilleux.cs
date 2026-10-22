#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public static partial class Program
    {
        private static string? _pendingPouilleuxInvitationId;
        private static string _pendingPouilleuxInviterName = "Joueur";
        private static float _pendingPouilleuxInvitationTime;
        private static Rectangle _acceptPouilleuxInvitationRect;
        private static Rectangle _declinePouilleuxInvitationRect;

        public static void ReceivePouilleuxInvitation(PouilleuxInvitationMsg invitation)
        {
            _pendingPouilleuxInvitationId = invitation.InvitationId;
            _pendingPouilleuxInviterName = string.IsNullOrWhiteSpace(invitation.InviterName) ? "Joueur" : invitation.InviterName;
            _pendingPouilleuxInvitationTime = 12f;
            AddNotification(new Notification(
                $"{_pendingPouilleuxInviterName} vous propose d'ouvrir les cartes. Y accepter / N refuser",
                new Color(120, 200, 255, 255),
                12f));
        }

        public static bool HasPendingPouilleuxInvitation => _pendingPouilleuxInvitationId != null;

        public static void UpdatePouilleuxInvitation(float dt)
        {
            if (_pendingPouilleuxInvitationId == null) return;
            _pendingPouilleuxInvitationTime -= dt;
            if (_pendingPouilleuxInvitationTime <= 0f)
            {
                RespondToPouilleuxInvitation(false);
                return;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Y))
                RespondToPouilleuxInvitation(true);
            else if (Raylib.IsKeyPressed(KeyboardKey.N) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                RespondToPouilleuxInvitation(false);
            else if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Vector2 mouse = Raylib.GetMousePosition();
                if (Raylib.CheckCollisionPointRec(mouse, _acceptPouilleuxInvitationRect))
                    RespondToPouilleuxInvitation(true);
                else if (Raylib.CheckCollisionPointRec(mouse, _declinePouilleuxInvitationRect))
                    RespondToPouilleuxInvitation(false);
            }
        }

        public static void DrawPouilleuxInvitation()
        {
            if (_pendingPouilleuxInvitationId == null) return;
            int sw = Raylib.GetScreenWidth();
            const int width = 390;
            const int height = 142;
            int x = sw - width - 24;
            int y = 122;
            Rectangle panel = new(x, y, width, height);
            Raylib.DrawRectangleRounded(panel, 0.08f, 8, new Color(24, 30, 42, 245));
            Raylib.DrawRectangleRoundedLines(panel, 0.08f, 8, 2, new Color(100, 190, 255, 240));
            FontManager.DrawText("Invitation Cartes", x + 18, y + 14, 21, new Color(130, 210, 255, 255));
            FontManager.DrawText($"{_pendingPouilleuxInviterName} veut jouer avec vous.", x + 18, y + 46, 16, Color.White);
            FontManager.DrawText($"Expire dans {(int)MathF.Ceiling(_pendingPouilleuxInvitationTime)} s", x + 18, y + 70, 13, new Color(180, 190, 205, 255));

            _acceptPouilleuxInvitationRect = new Rectangle(x + 18, y + 101, 150, 30);
            _declinePouilleuxInvitationRect = new Rectangle(x + 184, y + 101, 150, 30);
            bool acceptHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _acceptPouilleuxInvitationRect);
            bool declineHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _declinePouilleuxInvitationRect);
            UIManager.DrawButton(_acceptPouilleuxInvitationRect, new Color(50, 125, 88, 255), acceptHover);
            UIManager.DrawButton(_declinePouilleuxInvitationRect, new Color(125, 65, 65, 255), declineHover);
            FontManager.DrawText("Accepter (Y)", x + 43, y + 108, 14, Color.White);
            FontManager.DrawText("Refuser (N)", x + 211, y + 108, 14, Color.White);
        }

        public static void RespondToPouilleuxInvitation(bool accepted)
        {
            if (_pendingPouilleuxInvitationId == null) return;
            string invitationId = _pendingPouilleuxInvitationId;
            _pendingPouilleuxInvitationId = null;
            NetworkManager.RespondToPouilleuxInvitation(invitationId, accepted);
            AddNotification(new Notification(
                accepted ? "Invitation Cartes acceptée." : "Invitation Cartes refusée.",
                accepted ? new Color(120, 230, 150, 255) : new Color(220, 150, 150, 255),
                2.5f));
        }

        public static void ReceivePouilleuxStart(PouilleuxStartMsg start)
        {
            Entity? remoteParticipant = CreatePouilleuxRemoteParticipant(start.OtherConnectionId, start.OtherPlayerName);
            if (remoteParticipant == null)
            {
                AddNotification(new Notification("Impossible de rejoindre la partie de cartes.", new Color(230, 130, 130, 255), 3f));
                return;
            }

            SetPlayerPosition(remoteParticipant.WorldPos + new Vector2(140f, 0f));
            CartesGameUI.OpenNetwork(remoteParticipant, start.SessionId, start.OtherConnectionId, start.HostConnectionId);
        }

        private static Entity? CreatePouilleuxRemoteParticipant(int connectionId, string displayName)
        {
            Program.LocalPlayer? player = connectionId == NetworkManager.LocalConnectionId
                ? null
                : NetworkManager.GetRemotePlayersAsLocalPlayers().FirstOrDefault(candidate => candidate.Id == 1000 + connectionId);
            Vector2 position;
            string species;
            Color tint;
            Color hairColor;
            int hairStyle;
            int beardStyle;

            if (connectionId == 0)
            {
                position = GetPlayerPosition();
                species = "human";
                tint = SkinColor;
                hairColor = HairColor;
                hairStyle = PlayerHairStyle;
                beardStyle = PlayerBeardStyle;
            }
            else if (player != null)
            {
                position = player.Position;
                species = player.SpeciesOverride ?? "human";
                tint = player.MorphTintOverride ?? Color.White;
                hairColor = player.HairColorOverride ?? Color.White;
                hairStyle = player.HairStyleOverride ?? 0;
                beardStyle = player.BeardStyleOverride ?? 0;
            }
            else
            {
                return null;
            }

            Entity proxy = new(position, species);
            proxy.FirstName = string.IsNullOrWhiteSpace(displayName) ? "Joueur" : displayName;
            proxy.Tint = tint;
            proxy.HairColor = hairColor;
            proxy.HairStyle = hairStyle;
            proxy.BeardStyle = beardStyle;
            proxy.Facing = 1f;
            return proxy;
        }

        private static int? FindRemotePlayerConnectionUnderMouse(Camera2D camera)
        {
            if (!NetworkManager.IsOnline) return null;
            Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
            float bestDistSq = 55f * 55f;
            int? closest = null;
            foreach (Program.LocalPlayer player in NetworkManager.GetRemotePlayersAsLocalPlayers())
            {
                if (!player.Connected || !player.IsNetworkPlayer) continue;
                float distSq = Vector2.DistanceSquared(mouseWorld, player.Position);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    closest = player.Id - 1000;
                }
            }
            return closest;
        }

        public static bool TryInviteRemotePlayerUnderMouse(Camera2D camera)
        {
            if (!CartesGameUI.IsOpen) return false;
            int? connectionId = FindRemotePlayerConnectionUnderMouse(camera);
            if (!connectionId.HasValue) return false;
            NetworkManager.RequestPouilleuxInvitation(connectionId.Value);
            AddNotification(new Notification("Invitation Cartes envoyée.", new Color(120, 200, 255, 255), 2.5f));
            return true;
        }

        // La table s'ouvre sur le PNJ initiateur ; les autres joueurs rejoignent via invitation.
        private static void StartPouilleux(Entity npc)
        {
            CartesGameUI.Open(npc, Array.Empty<Entity>());
        }
    }
}
