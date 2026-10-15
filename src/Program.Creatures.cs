#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static partial class Program
    {
        public static void RequestImmediateCameraCenter() => _cameraNeedsImmediateCenter = true;

        /// <summary>
        /// Repose côté client la créature portée à la position indiquée ; l'hôte revalide la demande.
        /// </summary>
        public static bool TryDropCreatureClient(Vector2 targetPos)
        {
            if (_clientCarriedNetId == null) return false;
            if (!Guid.TryParse(_clientCarriedNetId, out var netId)) return false;
            if (World.IsCollidingEntity(targetPos, destroyedObjects, _clientCarriedSpecies))
            {
                activeNotifications.Add(new Notification("Impossible de reposer la créature ici !", new Color(255, 100, 100, 255), 1.5f));
                return false;
            }
            NetworkManager.RequestDropCreature(netId, targetPos);
            _clientCarriedNetId = null;
            _clientCarriedSpecies = "";
            activeNotifications.Add(new Notification("Créature reposée", new Color(0, 255, 200, 255), 1.5f));
            return true;
        }

        private const float TAMED_ANIMAL_INTERACTION_RANGE = 120f;

        private static Entity? FindNearbyTamedAnimal()
        {
            if (NetworkManager.IsClient)
            {
                EntityDto? closestSlime = null;
                float closestSlimeDistanceSq = TAMED_ANIMAL_INTERACTION_RANGE * TAMED_ANIMAL_INTERACTION_RANGE;
                foreach (var dto in NetworkManager.GetRemoteEntities())
                {
                    if (!dto.IsTamed || dto.IsHostilePet || dto.IsCarried
                        || !string.Equals(dto.Species, "slime", StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(dto.OwnerName, LocalPlayerName, StringComparison.OrdinalIgnoreCase)) continue;
                    float distanceSq = Vector2.DistanceSquared(_playerPos, new Vector2(dto.PosX, dto.PosY));
                    if (distanceSq >= closestSlimeDistanceSq) continue;
                    closestSlimeDistanceSq = distanceSq;
                    closestSlime = dto;
                }
                return closestSlime != null ? CreateSlimeProxyFromSnapshot(closestSlime) : null;
            }

            float bestDistSq = TAMED_ANIMAL_INTERACTION_RANGE * TAMED_ANIMAL_INTERACTION_RANGE;
            Entity? closest = null;

            foreach (var entity in entities)
            {
                if (!entity.IsAlive || !entity.IsTamed) continue;
                // Un animal offert à un PNJ ne doit plus être proposé comme compagnon du joueur.
                if (!string.Equals(entity.OwnerName, _currentSaveName, StringComparison.Ordinal)) continue;

                float distSq = Vector2.DistanceSquared(_playerPos, entity.WorldPos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    closest = entity;
                }
            }
            return closest;
        }
    }
}
