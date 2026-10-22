// Program.cs - Version complète avec système d'assise fonctionnel
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

public static class MathHelper
{
    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0f, 1f);
    }
}

namespace Soulfract
{

    public enum GameState { MainMenu, Options, Playing, Paused, Map, Achievements, Loading }

    public enum LoadingPhase { FadeIn, Working, FadeOutDelay, FadeOut }

    public enum InputType
    {
        Keyboard,
            Mouse,
        GamepadButton,
        GamepadAxis
    }

    public enum GamepadAxisDirection
    {
        LeftXNegative,
        LeftXPositive,
        LeftYNegative,
        LeftYPositive,
        RightXNegative,
        RightXPositive,
        RightYNegative,
        RightYPositive
    }

    public struct InputBinding
    {
        public InputType Type;
        public int Code; // KeyCode, MouseButton, GamepadButton ou GamepadAxisDirection

        public InputBinding(InputType type, int code)
        {
            Type = type;
            Code = code;
        }

        public override string ToString()
        {
            return $"{Type}:{Code}";
        }
    }

        public static partial class Program
    {
        // Le code a été réparti dans plusieurs fichiers (voir Program.*.cs) pour la lisibilité.
    }
}
