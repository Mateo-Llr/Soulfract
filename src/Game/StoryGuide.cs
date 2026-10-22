// Program_StoryGuide.cs
#nullable enable

namespace Soulfract
{
    public static partial class Program
    {
        // Dialogue du Veilleur (PNJ du campement de départ, voir World.TryBuildStoryCamp).
        // La séquence reste dans une seule bulle ; Échap l'interrompt.

        private static Entity? _storyGuideNpc;
        private static bool _storyGuideCancelled;
        private static bool _storyGuideHasMetPlayer; // pour cette session de jeu

        private static readonly string[] StoryGuideIntroPages =
        {
            "Ah… enfin une âme sur le socle. Approche, n'aie pas peur. Je m'appelle Orwen, on me surnomme le Veilleur. Je me suis installé au bout de ce vieux sentier pour guetter ton arrivée.",
            "Tu viens de t'incarner, n'est-ce pas ? Ce corps te semble étranger et tes souvenirs ne sont plus que des éclats. C'est le lot de toute âme descendue des Vents Solaires. Ça passera… en partie.",
            "Le socle d'où tu viens est un seuil. Si ce corps venait à périr, ton âme s'y relèverait, dans un corps semblable au dernier. Ce n'est pas un hasard : certains disent que les dieux ont besoin de toi.",
            "Car le monde change. Les étoiles s'éteignent plus vite qu'avant, les Ombres s'agitent, et de plus en plus d'âmes se retrouvent fusionnées dans un même corps. Personne ne sait encore pourquoi.",
            "Je ne suis qu'un vieux Gardien du Seuil : ma tâche est d'accueillir les âmes nouvelles. Il me reste quelques vivres et un peu de bois dans mes caisses : sers-toi, et range-y ce que tu veux. Explore, trouve les villages, et reviens me voir si tu te sens perdu."
        };

        /// <summary>
        /// Démarre la conversation si <paramref name="npc"/> est le Veilleur. Retourne false pour
        /// laisser le dialogue de villageois habituel gérer ce PNJ.
        /// </summary>
        private static bool TryStartStoryGuideDialog(Entity npc)
        {
            if (!npc.IsStoryGuide) return false;

            npc.WakeFromInteraction();
            if (_storyGuideHasMetPlayer)
                return OpenVillagerChoiceDialog(npc);

            _storyGuideNpc = npc;
            _storyGuideCancelled = false;
            QuestDialogUI.OpenSequence(npc, StoryGuideIntroPages);
            return true;
        }

        /// <summary>Nettoie l'état d'une séquence interrompue ou dont le PNJ a disparu.</summary>
        private static void UpdateStoryGuideDialog()
        {
            if (_storyGuideNpc == null || QuestDialogUI.IsOpen) return;
            if (!_storyGuideCancelled && _storyGuideNpc.IsAlive) return;
            _storyGuideNpc = null;
            _storyGuideCancelled = false;
        }

        private static void CompleteStoryGuideDialog()
        {
            Entity? npc = _storyGuideNpc;
            _storyGuideNpc = null;
            _storyGuideCancelled = false;
            if (npc == null || !npc.IsAlive) return;

            _storyGuideHasMetPlayer = true;
            OpenVillagerChoiceDialog(npc);
        }

        /// <summary>L'utilisateur a quitté la bulle avec Échap : on abandonne la suite de la conversation.</summary>
        private static void CancelStoryGuideDialog()
        {
            if (_storyGuideNpc != null)
                _storyGuideCancelled = true;
        }
    }
}
