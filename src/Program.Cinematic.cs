using System.Numerics;
using Raylib_cs;

namespace Soulfract
{
	public static partial class Program
	{
		private const int SoulAnchorTileId = 7001;
		private const float SoulArrivalFadeDuration = 1.25f;
		private const float SoulArrivalNarrationDelay = 1.6f;
		private const float SoulArrivalSceneTransitionDuration = 0.75f;
		private const float SoulArrivalLineDuration = 4.8f;
		private const float SoulMaterializationDuration = 4.2f;
		public const float SoulMaterializationRevealStart = 1.9f;
		public const float SoulMaterializationColorStart = 2.75f;
		private static readonly string[] SoulArrivalLines =
		{
			"Before time, there was only silence.",
			"Yet even the void can give birth to a soul.",
			"Pure and nameless, you cross the stars.",
			"The planet awaits. Your story begins here."
		};

		private static bool _isSoulArrivalCinematic;
		private static int _soulArrivalLineIndex;
		private static int _soulArrivalPreviousLineIndex = -1;
		private static float _soulArrivalSceneTimer;
		private static float _soulArrivalSceneTransitionTimer;
		private static float _soulArrivalLineTimer;
		private static int _soulArrivalProcessedCharacters;
		private static RenderTexture2D _soulArrivalSceneTexture;
		private static RenderTexture2D _soulArrivalPreviousSceneTexture;
		private static bool _isSoulMaterializing;
		private static float _soulMaterializationTimer;
		private static Shader _soulMaterializationShader;
		private static bool _soulMaterializationShaderInitialized;
		private static int _soulMaterializationColorProgressLocation = -1;
		private static int _soulMaterializationAlphaLocation = -1;
		private static bool _soulArrivalSpaceTexturesInitialized;
		private static Texture2D _soulArrivalWaterTexture;
		private static Texture2D _soulArrivalNarratorStarTexture;
		private static Texture2D[] _soulArrivalGardenLandTextures = Array.Empty<Texture2D>();
		private static Texture2D _soulArrivalGasGiantTexture;
		private static Texture2D _soulArrivalPlanetShadowTexture;
		private static SoulArrivalBackdropPlanet[] _soulArrivalBackdropPlanets = Array.Empty<SoulArrivalBackdropPlanet>();
		private static Random _soulArrivalPlanetRandom = new();
		private static SoulArrivalStar[] _soulArrivalStars = Array.Empty<SoulArrivalStar>();
		private static int _soulArrivalStarsWidth;
		private static int _soulArrivalStarsHeight;
		private static Shader _soulArrivalSkyShader;
		private static bool _soulArrivalSkyShaderInitialized;
		private static int _soulArrivalSkyTimeLocation = -1;
		private static int _soulArrivalSkyResolutionLocation = -1;
		private static readonly Dictionary<string, Color[]> SoulArrivalBiomeCircleColors = new(StringComparer.OrdinalIgnoreCase)
		{
			["alien"] = [new(147, 60, 132, 255), new(171, 78, 143, 255), new(190, 100, 157, 255)],
			["arctic"] = [new(203, 220, 222, 255), new(227, 235, 245, 255), new(245, 249, 250, 255)],
			["barren"] = [new(100, 62, 45, 255), new(140, 100, 60, 255), new(185, 134, 94, 255)],
			["desert"] = [new(138, 119, 79, 255), new(179, 166, 102, 255), new(215, 204, 137, 255)],
			["forest"] = [new(21, 68, 36, 255), new(29, 89, 36, 255), new(42, 114, 39, 255)],
			["garden"] = [new(59, 123, 43, 255), new(91, 146, 57, 255), new(121, 166, 72, 255), new(148, 195, 84, 255), new(148, 195, 84, 255)],
			["jungle"] = [new(107, 74, 34, 255), new(122, 91, 45, 255), new(141, 109, 61, 255)],
			["magma"] = [new(28, 28, 28, 255), new(79, 45, 45, 255), new(86, 52, 52, 255)],
			["midnight"] = [new(0, 0, 0, 255), new(21, 21, 21, 255), new(56, 56, 56, 255)],
			["moon"] = [new(94, 81, 74, 255), new(131, 120, 108, 255), new(165, 155, 148, 255)],
			["ocean"] = [new(107, 147, 61, 255), new(148, 171, 79, 255), new(190, 179, 101, 255)],
			["savannah"] = [new(109, 76, 59, 255), new(130, 98, 77, 255), new(155, 123, 91, 255), new(178, 154, 123, 255)],
			["scorchedcity"] = [new(82, 59, 48, 255), new(117, 92, 67, 255), new(156, 123, 98, 255)],
			["snow"] = [new(245, 253, 255, 255), new(221, 240, 240, 255), new(195, 236, 225, 255), new(151, 212, 171, 255)],
			["tentacles"] = [new(60, 40, 64, 255), new(69, 53, 78, 255), new(107, 82, 121, 255)],
			["toxic"] = [new(55, 107, 34, 255), new(91, 123, 36, 255), new(128, 160, 53, 255), new(172, 181, 70, 255)],
			["tundra"] = [new(203, 220, 222, 255), new(227, 235, 245, 255), new(245, 249, 250, 255)],
			["volcanic"] = [new(28, 28, 28, 255), new(80, 53, 53, 255), new(103, 72, 72, 255)]
		};
		private static readonly Dictionary<string, Color> SoulArrivalFlatCircleColors = new(StringComparer.OrdinalIgnoreCase)
		{
			["assets/space/terrestrial/liquids/fuel.png"] = new(156, 51, 162, 255),
			["assets/space/terrestrial/liquids/lava.png"] = new(166, 50, 25, 255),
			["assets/space/terrestrial/liquids/poison.png"] = new(58, 143, 44, 255),
			["assets/space/terrestrial/liquids/swampwater.png"] = new(146, 136, 26, 255),
			["assets/space/terrestrial/liquids/tarliquid.png"] = new(51, 42, 38, 255),
			["assets/space/terrestrial/liquids/water.png"] = new(51, 90, 162, 255),
			["assets/space/gas_giant/gas_giant_base.png"] = new(127, 68, 68, 255),
			["assets/space/gas_giant/gas_giant_clouds_0.png"] = new(86, 46, 46, 255),
			["assets/space/gas_giant/gas_giant_clouds_1.png"] = new(107, 57, 57, 255)
		};

		private readonly record struct SoulArrivalStar(
			float X,
			float Y,
			float Radius,
			float Brightness,
			float TwinklePhase,
			float TwinkleSpeed,
			float TwinkleAmount);

		private readonly record struct SoulArrivalBackdropPlanet(
			bool IsGasGiant,
			Texture2D[] LandLayers,
			Texture2D BaseTexture,
			Texture2D DetailTexture,
			Texture2D ShadowTexture,
			Vector2 Position,
			float Size,
			float Rotation,
			float RotationSpeed,
			Color Tint);

		private const float SoulTrailSampleInterval = 1f / 30f;
		private const float SoulTrailLifetime = 0.55f;
		private static readonly Dictionary<string, List<SoulTrailSample>> SoulTrailSamples = new();

		private readonly record struct SoulTrailSample(Vector2 Position, float Time);

		private const string SoulMaterializationFragmentShader = @"
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;
uniform sampler2D texture0;
uniform float colorProgress;
uniform float silhouetteAlpha;

void main()
{
	vec4 source = texture(texture0, fragTexCoord) * fragColor;
	finalColor = vec4(mix(vec3(1.0), source.rgb, colorProgress), source.a * silhouetteAlpha);
}";

		private const string SoulArrivalSkyFragmentShader = @"
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;
uniform float time;
uniform vec2 resolution;

float hash21(vec2 p)
{
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p)
{
	vec2 cell = floor(p);
	vec2 local = fract(p);
	local = local * local * (3.0 - 2.0 * local);
	float a = hash21(cell);
	float b = hash21(cell + vec2(1.0, 0.0));
	float c = hash21(cell + vec2(0.0, 1.0));
	float d = hash21(cell + vec2(1.0, 1.0));
	return mix(mix(a, b, local.x), mix(c, d, local.x), local.y);
}

float fbm(vec2 p)
{
	float value = 0.0;
	float amplitude = 0.5;
	for (int octave = 0; octave < 4; octave++)
	{
		value += noise(p) * amplitude;
		p = p * 2.03 + vec2(17.1, 9.2);
		amplitude *= 0.5;
	}
	return value;
}

void main()
{
	vec2 uv = gl_FragCoord.xy / resolution;
	vec2 flow = vec2(time * 0.006, time * -0.0035);
	float warp = fbm(uv * vec2(3.2, 2.1) + flow);
	vec2 warpedUv = uv + (vec2(warp, fbm(uv * 2.4 - flow)) - 0.5) * 0.22;
	float clouds = fbm(warpedUv * vec2(5.0, 3.1) + flow);
	float filaments = fbm(warpedUv * vec2(11.0, 6.5) - flow * 1.8);
	float nebula = smoothstep(0.38, 0.68, clouds * 0.72 + filaments * 0.28);
	float blueClouds = smoothstep(0.38, 0.68, fbm(warpedUv * vec2(3.8, 2.6) + vec2(4.7, -2.3) - flow));
	float vertical = smoothstep(0.0, 1.0, uv.y);

	vec3 color = mix(vec3(0.008, 0.012, 0.035), vec3(0.018, 0.014, 0.055), vertical);
	color += vec3(0.15, 0.055, 0.3) * nebula;
	color += vec3(0.035, 0.14, 0.38) * blueClouds * smoothstep(0.32, 0.65, clouds);
	color += vec3(0.018, 0.009, 0.045) * (1.0 - nebula);
	finalColor = vec4(color, 1.0) * fragColor;
}";

		public static bool IsSoulMaterializing => _isSoulMaterializing;
		public static float SoulMaterializationTime => _soulMaterializationTimer;

		private static void BeginSoulMaterialization()
		{
			_isSoulMaterializing = true;
			_soulMaterializationTimer = 0f;
			_isDancing = false;
			_currentEmote = null;
			_animState = "idle";
			_animFrame = 0;
			_animProg = 0f;
			_animRawFrame = 0;
			_animRawProg = 0f;
			ClearSoulTrail("materialization");
		}

		private static void UpdateSoulMaterialization(float dt)
		{
			if (!_isSoulMaterializing) return;
			_soulMaterializationTimer += dt;
			if (_soulMaterializationTimer >= SoulMaterializationDuration)
			{
				_soulMaterializationTimer = SoulMaterializationDuration;
				_isSoulMaterializing = false;
			}
		}

		public static float GetSoulMaterializationColorProgress()
		{
			return SmoothStep((_soulMaterializationTimer - SoulMaterializationColorStart)
				/ (SoulMaterializationDuration - SoulMaterializationColorStart));
		}

		public static bool BeginSoulMaterializationRender()
		{
			if (!_soulMaterializationShaderInitialized)
			{
				_soulMaterializationShaderInitialized = true;
				_soulMaterializationShader = Raylib.LoadShaderFromMemory(null, SoulMaterializationFragmentShader);
				if (_soulMaterializationShader.Id == 0)
				{
					Console.WriteLine("[Soul] Impossible de charger le shader de matérialisation.");
					return false;
				}

				_soulMaterializationColorProgressLocation = Raylib.GetShaderLocation(_soulMaterializationShader, "colorProgress");
				_soulMaterializationAlphaLocation = Raylib.GetShaderLocation(_soulMaterializationShader, "silhouetteAlpha");
			}

			if (_soulMaterializationShader.Id == 0
				|| _soulMaterializationColorProgressLocation < 0
				|| _soulMaterializationAlphaLocation < 0)
				return false;

			float colorProgress = GetSoulMaterializationColorProgress();
			float silhouetteAlpha = SmoothStep(
				(_soulMaterializationTimer - SoulMaterializationRevealStart) / 0.5f);
			Raylib.SetShaderValue(_soulMaterializationShader, _soulMaterializationColorProgressLocation, colorProgress, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_soulMaterializationShader, _soulMaterializationAlphaLocation, silhouetteAlpha, ShaderUniformDataType.Float);
			Raylib.BeginShaderMode(_soulMaterializationShader);
			return true;
		}

		public static void EndSoulMaterializationRender()
		{
			Raylib.EndShaderMode();
		}

		public static void DrawSoulMaterializationOrb(Vector2 playerPosition)
		{
			if (!_isSoulMaterializing) return;
			float time = _soulMaterializationTimer;
			float fade = 1f - SmoothStep((time - 2.45f) / 0.85f);
			if (fade <= 0f) return;

			Vector2 soulPosition = GetSoulMaterializationOrbPosition(playerPosition, time);
			DrawSoulTrail("materialization", soulPosition, fade, time);
			DrawSoulLight(soulPosition, fade);
		}

		private static Vector2 GetSoulMaterializationOrbPosition(Vector2 playerPosition, float time)
		{
			const float fallDuration = 1.1f;
			const float orbitDuration = 1.4f;
			float chestY = playerPosition.Y - 35f;
			float orbitStartRadius = 34f;
			float x;
			float y;
			if (time < fallDuration)
			{
				float fall = SmoothStep(time / fallDuration);
				x = float.Lerp(playerPosition.X - 45f, playerPosition.X + orbitStartRadius, fall);
				y = chestY - 520f * (1f - fall);
			}
			else if (time < fallDuration + orbitDuration)
			{
				float orbitTime = time - fallDuration;
				float angle = orbitTime * 5.5f;
				float radius = 34f + MathF.Sin(orbitTime * 4f) * 5f;
				x = playerPosition.X + MathF.Cos(angle) * radius;
				y = chestY + MathF.Sin(angle) * radius * 0.55f;
			}
			else
			{
				float merge = SmoothStep((time - fallDuration - orbitDuration) / 0.75f);
				float angle = orbitDuration * 5.5f;
				float radius = 34f + MathF.Sin(orbitDuration * 4f) * 5f;
				float orbitX = playerPosition.X + MathF.Cos(angle) * radius;
				float orbitY = chestY + MathF.Sin(angle) * radius * 0.55f;
				x = float.Lerp(orbitX, playerPosition.X, merge);
				y = float.Lerp(orbitY, chestY, merge);
			}

			return new Vector2(x, y);
		}

		private static void ClearSoulTrail(string trailId)
		{
			SoulTrailSamples.Remove(trailId);
		}

		private static void BeginSoulArrivalCinematic()
		{
			if (_soulArrivalSpaceTexturesInitialized)
				UnloadSoulArrivalSpaceTextures();
			_soulArrivalPlanetRandom = new Random();
			_isSoulArrivalCinematic = true;
			_soulArrivalLineIndex = 0;
			_soulArrivalPreviousLineIndex = -1;
			_soulArrivalSceneTimer = 0f;
			_soulArrivalSceneTransitionTimer = SoulArrivalSceneTransitionDuration;
			_soulArrivalLineTimer = 0f;
			_soulArrivalProcessedCharacters = 0;
			_fadeOutOverlayActive = false;
			ProceduralNpcVoice.Stop();
		}

		private static void UpdateSoulArrivalCinematic(float dt)
		{
			if (Raylib.IsKeyPressed(KeyboardKey.Escape))
			{
				EndSoulArrivalCinematic();
				return;
			}

			string line = GetSoulArrivalLine(_soulArrivalLineIndex);
			_soulArrivalSceneTimer += dt;
			_soulArrivalSceneTransitionTimer = Math.Min(
				SoulArrivalSceneTransitionDuration,
				_soulArrivalSceneTransitionTimer + dt);
			if (_soulArrivalSceneTimer >= SoulArrivalNarrationDelay)
			{
				_soulArrivalLineTimer += dt;
				int visibleCharacters = Math.Min(line.Length, (int)(_soulArrivalLineTimer * 30f));
				if (visibleCharacters > _soulArrivalProcessedCharacters)
					ProceduralNpcVoice.Update(_playerEntity, line, visibleCharacters, ref _soulArrivalProcessedCharacters);

				if (Raylib.IsKeyPressed(KeyboardKey.Enter)
					|| Raylib.IsKeyPressed(KeyboardKey.Space)
					|| Raylib.IsMouseButtonPressed(MouseButton.Left)
					|| _soulArrivalLineTimer >= SoulArrivalLineDuration)
				{
					AdvanceSoulArrivalLine();
				}
			}
		}

		private static void AdvanceSoulArrivalLine()
		{
			ProceduralNpcVoice.Stop();
			if (_soulArrivalLineIndex + 1 >= SoulArrivalLines.Length)
			{
				EndSoulArrivalCinematic();
				return;
			}

			_soulArrivalLineIndex++;
			_soulArrivalPreviousLineIndex = _soulArrivalLineIndex - 1;
			_soulArrivalSceneTimer = 0f;
			_soulArrivalSceneTransitionTimer = 0f;
			_soulArrivalLineTimer = 0f;
			_soulArrivalProcessedCharacters = 0;
		}

		private static void EndSoulArrivalCinematic()
		{
			ProceduralNpcVoice.Stop();
			_isSoulArrivalCinematic = false;
			BeginSoulMaterialization();
			_fadeOutOverlayActive = true;
			_worldRevealAlpha = 0f;
			_loadingSpinnerTime = 0f;
		}

		private static string GetSoulArrivalLine(int index)
		{
			string fallback = SoulArrivalLines[index];
			return Localization.GetOrDefault($"soularrival.line.{index + 1}", fallback);
		}

		private static void DrawSoulArrivalCinematic()
		{
			int screenWidth = Raylib.GetScreenWidth();
			int screenHeight = Raylib.GetScreenHeight();
			float time = (float)Raylib.GetTime();
			float centerX = screenWidth * 0.5f;
			float centerY = screenHeight * 0.43f;

			DrawSoulArrivalSky(screenWidth, screenHeight);
			DrawSoulArrivalStars(screenWidth, screenHeight, time);
			EnsureSoulArrivalSceneTextures(screenWidth, screenHeight);
			if (_soulArrivalSceneTexture.Id != 0)
			{
				DrawSoulArrivalSceneToTexture(_soulArrivalSceneTexture, _soulArrivalLineIndex, time, centerX, centerY);
				float transitionProgress = SmoothStep(
					_soulArrivalSceneTransitionTimer / SoulArrivalSceneTransitionDuration);
				if (_soulArrivalPreviousLineIndex >= 0 && transitionProgress < 1f
					&& _soulArrivalPreviousSceneTexture.Id != 0)
				{
					DrawSoulArrivalSceneToTexture(_soulArrivalPreviousSceneTexture, _soulArrivalPreviousLineIndex, time, centerX, centerY);
					DrawSoulArrivalSceneTexture(_soulArrivalPreviousSceneTexture, screenWidth, screenHeight, 1f - transitionProgress);
				}

				DrawSoulArrivalSceneTexture(_soulArrivalSceneTexture, screenWidth, screenHeight,
					_soulArrivalLineIndex == 0 ? 1f : transitionProgress);
			}
			else
			{
				DrawSoulArrivalScene(_soulArrivalLineIndex, time, centerX, centerY, 1f);
			}

			string title = Localization.GetOrDefault("soularrival.title", "The arrival of a soul");
			int titleSize = Math.Clamp(screenWidth / 42, 20, 34);
			int titleWidth = FontManager.MeasureText(title, titleSize);
			float titleFade = _soulArrivalLineIndex == 0
				? 1f
				: SmoothStep(_soulArrivalSceneTransitionTimer / SoulArrivalSceneTransitionDuration);
			byte titleAlpha = (byte)Math.Clamp((int)MathF.Round(245f * titleFade), 0, 245);
			FontManager.DrawText(title, (screenWidth - titleWidth) / 2, (int)(screenHeight * 0.08f), titleSize, new Color(220, 225, 255, (int)titleAlpha));

			if (_soulArrivalPreviousLineIndex >= 0 && _soulArrivalSceneTransitionTimer < SoulArrivalSceneTransitionDuration)
				DrawSoulArrivalCaption(_soulArrivalPreviousLineIndex, SoulArrivalLineDuration, 1f - SmoothStep(_soulArrivalSceneTransitionTimer / SoulArrivalSceneTransitionDuration), screenWidth, screenHeight);

			if (_soulArrivalSceneTimer >= SoulArrivalNarrationDelay)
			{
				float narrationFade = Math.Clamp((_soulArrivalSceneTimer - SoulArrivalNarrationDelay) / 0.3f, 0f, 1f);
				float transitionFade = _soulArrivalLineIndex == 0
					? SmoothStep(_soulArrivalSceneTimer / SoulArrivalFadeDuration)
					: SmoothStep(_soulArrivalSceneTransitionTimer / SoulArrivalSceneTransitionDuration);
				DrawSoulArrivalCaption(_soulArrivalLineIndex, _soulArrivalLineTimer, narrationFade * transitionFade, screenWidth, screenHeight);
			}

			if (_soulArrivalLineIndex == 0)
			{
				byte fadeAlpha = (byte)Math.Clamp(
					(int)MathF.Round((1f - SmoothStep(_soulArrivalSceneTimer / SoulArrivalFadeDuration)) * 255f),
					0,
					255);
				if (fadeAlpha > 0)
					Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(0, 0, 0, (int)fadeAlpha));
			}
		}

		private static void DrawSoulArrivalSceneToTexture(RenderTexture2D target, int sceneIndex, float time, float centerX, float centerY)
		{
			Raylib.BeginTextureMode(target);
			Raylib.ClearBackground(new Color(0, 0, 0, 0));
			DrawSoulArrivalScene(sceneIndex, time, centerX, centerY, 1f);
			Raylib.EndTextureMode();
		}

		private static void DrawSoulArrivalScene(int sceneIndex, float time, float centerX, float centerY, float alpha)
		{
			DrawSoulArrivalSpaceBackdrop(time);
			if (sceneIndex == 1)
				DrawSoulArrivalTemperatePlanet(new Vector2(centerX, Raylib.GetScreenHeight() * 0.56f), Math.Min(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()) * 0.62f, time);
			if (sceneIndex == 2)
			{
				float customScale = 2.4f;
				Vector2 characterPosition = new(centerX, centerY + Raylib.GetScreenHeight() * 0.18f);
				DrawSoulArrivalCharacter(characterPosition, closeEyes: true);
				DrawSoulInsideCharacter(characterPosition, customScale, alpha);
			}
			else if (sceneIndex == 0 || sceneIndex == 3)
			{
				float bob = MathF.Sin(time * 1.8f) * 10f;
				Vector2 soulPosition = new(centerX, centerY + bob);
				DrawSoulTrail("arrival-space", soulPosition, alpha, time);
				DrawSoulGlow((int)soulPosition.X, (int)soulPosition.Y, Math.Min(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()) * 0.14f);
				if (_soulArrivalNarratorStarTexture.Id != 0)
				{
					int frameWidth = _soulArrivalNarratorStarTexture.Width / 4;
					int frame = (int)(time * 8f) % 4;
					float spriteSize = Math.Min(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()) * 0.075f;
					Rectangle source = new(frame * frameWidth, 0, frameWidth, _soulArrivalNarratorStarTexture.Height);
					Rectangle destination = new(
						soulPosition.X - spriteSize / 2f,
						soulPosition.Y - spriteSize / 2f,
						spriteSize,
						spriteSize);
					Raylib.DrawTexturePro(
						_soulArrivalNarratorStarTexture,
						source,
						destination,
						Vector2.Zero,
						0f,
						new Color(255, 255, 255, (int)(255f * alpha)));
				}
			}
		}

		private static void DrawSoulArrivalSceneTexture(RenderTexture2D texture, int screenWidth, int screenHeight, float alpha)
		{
			byte tintAlpha = (byte)Math.Clamp((int)MathF.Round(alpha * 255f), 0, 255);
			Raylib.DrawTexturePro(
				texture.Texture,
				new Rectangle(0, 0, texture.Texture.Width, -texture.Texture.Height),
				new Rectangle(0, 0, screenWidth, screenHeight),
				Vector2.Zero,
				0f,
				new Color(255, 255, 255, (int)tintAlpha));
		}

		private static void EnsureSoulArrivalSceneTextures(int width, int height)
		{
			if (_soulArrivalSceneTexture.Id != 0
				&& _soulArrivalSceneTexture.Texture.Width == width
				&& _soulArrivalSceneTexture.Texture.Height == height)
				return;

			if (_soulArrivalSceneTexture.Id != 0) Raylib.UnloadRenderTexture(_soulArrivalSceneTexture);
			if (_soulArrivalPreviousSceneTexture.Id != 0) Raylib.UnloadRenderTexture(_soulArrivalPreviousSceneTexture);
			_soulArrivalSceneTexture = Raylib.LoadRenderTexture(width, height);
			_soulArrivalPreviousSceneTexture = Raylib.LoadRenderTexture(width, height);
			if (_soulArrivalSceneTexture.Id == 0 || _soulArrivalPreviousSceneTexture.Id == 0)
				Console.WriteLine("Impossible de créer les textures de transition de la cinématique d'arrivée.");
		}

		private static float SmoothStep(float value)
		{
			float t = Math.Clamp(value, 0f, 1f);
			return t * t * (3f - 2f * t);
		}

		private static void DrawSoulArrivalCaption(int sceneIndex, float lineTimer, float opacity, int screenWidth, int screenHeight)
		{
			if (opacity <= 0f) return;
			float clampedOpacity = Math.Clamp(opacity, 0f, 1f);
			byte textAlpha = (byte)Math.Clamp((int)MathF.Round(255f * clampedOpacity), 0, 255);
			Rectangle captionPanel = new(24f, screenHeight - 154f, screenWidth - 48f, 126f);
			Raylib.DrawRectangleRounded(captionPanel, 0.08f, 8, new Color(9, 12, 27, (int)(225f * clampedOpacity)));
			Raylib.DrawRectangleRoundedLines(captionPanel, 0.08f, 8, 1f, new Color(130, 155, 220, (int)(100f * clampedOpacity)));

			string line = GetSoulArrivalLine(sceneIndex);
			int visibleCharacters = Math.Min(line.Length, (int)(lineTimer * 30f));
			string visibleLine = line.Substring(0, visibleCharacters);
			int lineSize = Math.Clamp(screenWidth / 58, 15, 23);
			int lineWidth = FontManager.MeasureText(visibleLine, lineSize);
			FontManager.DrawText(visibleLine, (screenWidth - lineWidth) / 2, screenHeight - 118, lineSize, new Color(255, 255, 255, (int)textAlpha));

			string hint = Localization.GetOrDefault("soularrival.hint", "ENTER / SPACE / CLICK: continue     ESC: skip");
			int hintWidth = FontManager.MeasureText(hint, 13);
			FontManager.DrawText(hint, (screenWidth - hintWidth) / 2, screenHeight - 60, 13, new Color(165, 175, 205, (int)(220f * clampedOpacity)));
		}

		private static void UnloadSoulArrivalSceneTextures()
		{
			if (_soulArrivalSceneTexture.Id != 0)
				Raylib.UnloadRenderTexture(_soulArrivalSceneTexture);
			if (_soulArrivalPreviousSceneTexture.Id != 0)
				Raylib.UnloadRenderTexture(_soulArrivalPreviousSceneTexture);
			_soulArrivalSceneTexture = default;
			_soulArrivalPreviousSceneTexture = default;
			UnloadSoulArrivalSpaceTextures();
			if (_soulArrivalSkyShader.Id != 0)
				Raylib.UnloadShader(_soulArrivalSkyShader);
			_soulArrivalSkyShader = default;
			_soulArrivalSkyShaderInitialized = false;
			_soulArrivalSkyTimeLocation = -1;
			_soulArrivalSkyResolutionLocation = -1;
		}

		private static void EnsureSoulArrivalSpaceTextures()
		{
			if (_soulArrivalSpaceTexturesInitialized) return;
			_soulArrivalSpaceTexturesInitialized = true;

			_soulArrivalWaterTexture = LoadSoulArrivalSpaceTexture("assets/space/terrestrial/liquids/water.png");
			_soulArrivalNarratorStarTexture = LoadSoulArrivalSpaceTexture("assets/space/star21pxwhite.png");
			_soulArrivalGardenLandTextures = LoadSoulArrivalBiomeLayers("garden", 5, "temperate", 31);
			_soulArrivalGasGiantTexture = LoadSoulArrivalSpaceTexture("assets/space/gas_giant/gas_giant_base.png");
			_soulArrivalPlanetShadowTexture = LoadSoulArrivalSpaceTexture($"assets/space/terrestrial/shadows/{_soulArrivalPlanetRandom.Next(10)}.png");
			_soulArrivalBackdropPlanets = CreateSoulArrivalBackdropPlanets();
		}

		private static Texture2D[] LoadSoulArrivalBiomeLayers(string biome, int layerCount, string dynamicsType, int dynamicsCount)
		{
			Texture2D[] layers = new Texture2D[layerCount];
			for (int i = 0; i < layerCount; i++)
			{
				int maskNumber = layerCount - i;
				int dynamicsNumber = _soulArrivalPlanetRandom.Next(dynamicsCount);
				string biomePath = $"assets/space/terrestrial/biomes/{biome}/maskie{maskNumber}.png";
				string dynamicsPath = $"assets/space/terrestrial/dynamics/{dynamicsType}/{dynamicsNumber}.png";
				layers[i] = LoadSoulArrivalMaskedSpaceTexture(biomePath, dynamicsPath);
			}
			return layers;
		}

		private static SoulArrivalBackdropPlanet[] CreateSoulArrivalBackdropPlanets()
		{
			(string Name, int Layers)[] biomes =
			[
				("alien", 3), ("arctic", 3), ("barren", 3), ("desert", 3),
				("forest", 3), ("garden", 5), ("jungle", 3), ("magma", 3),
				("midnight", 3), ("moon", 3), ("ocean", 3), ("savannah", 4),
				("scorchedcity", 3), ("snow", 4), ("tentacles", 3), ("toxic", 4),
				("tundra", 3), ("volcanic", 3)
			];
			(string Name, int Variants)[] dynamicsTypes =
			[
				("temperate", 31), ("ocean", 21), ("arid", 51)
			];
			Color[] gasGiantTints =
			[
				Color.White,
				new(236, 213, 178, 255),
				new(177, 202, 242, 255),
				new(209, 181, 226, 255),
				new(203, 228, 214, 255)
			];
			Vector2[] positions =
			[
				new(0.10f, 0.24f), new(0.86f, 0.18f), new(0.13f, 0.72f),
				new(0.84f, 0.78f), new(0.47f, 0.16f), new(0.54f, 0.86f)
			];
			for (int i = positions.Length - 1; i > 0; i--)
			{
				int swapIndex = _soulArrivalPlanetRandom.Next(i + 1);
				(positions[i], positions[swapIndex]) = (positions[swapIndex], positions[i]);
			}

			SoulArrivalBackdropPlanet[] planets = new SoulArrivalBackdropPlanet[3];
			int gasGiantCount = 1 + _soulArrivalPlanetRandom.Next(2);
			for (int i = 0; i < planets.Length; i++)
			{
				Vector2 position = positions[i];
				float rotation = (float)_soulArrivalPlanetRandom.NextDouble() * MathF.Tau;
				bool isGasGiant = i < gasGiantCount;
				if (isGasGiant)
				{
					int cloudVariant = _soulArrivalPlanetRandom.Next(2);
					int dynamicVariant = _soulArrivalPlanetRandom.Next(30) + 1;
					Texture2D clouds = LoadSoulArrivalMaskedSpaceTexture(
						$"assets/space/gas_giant/gas_giant_clouds_{cloudVariant}.png",
						$"assets/space/gas_giant/gas_giant_dynamics/{dynamicVariant}.png");
					Texture2D shadow = LoadSoulArrivalSpaceTexture(
						$"assets/space/gas_giant/shadows/{_soulArrivalPlanetRandom.Next(10)}.png");
					planets[i] = new SoulArrivalBackdropPlanet(
						true,
						Array.Empty<Texture2D>(),
						_soulArrivalGasGiantTexture,
						clouds,
						shadow,
						position,
						0.24f + (float)_soulArrivalPlanetRandom.NextDouble() * 0.18f,
						rotation,
						((float)_soulArrivalPlanetRandom.NextDouble() - 0.5f) * 0.025f,
						gasGiantTints[_soulArrivalPlanetRandom.Next(gasGiantTints.Length)]);
					continue;
				}

				(string biome, int layerCount) = biomes[_soulArrivalPlanetRandom.Next(biomes.Length)];
				(string dynamicsType, int dynamicsCount) = dynamicsTypes[_soulArrivalPlanetRandom.Next(dynamicsTypes.Length)];
				Texture2D[] landLayers = LoadSoulArrivalBiomeLayers(biome, layerCount, dynamicsType, dynamicsCount);
				Texture2D terrestrialShadow = LoadSoulArrivalSpaceTexture(
					$"assets/space/terrestrial/shadows/{_soulArrivalPlanetRandom.Next(10)}.png");
				planets[i] = new SoulArrivalBackdropPlanet(
					false,
					landLayers,
					default,
					default,
					terrestrialShadow,
					position,
					0.10f + (float)_soulArrivalPlanetRandom.NextDouble() * 0.10f,
					rotation,
					((float)_soulArrivalPlanetRandom.NextDouble() - 0.5f) * 0.025f,
					Color.White);
			}

			return planets;
		}

		private static Texture2D LoadSoulArrivalSpaceTexture(string path)
		{
			if (TryGetSoulArrivalFlatCircleColor(path, out Color circleColor))
				return LoadSoulArrivalCircleTexture(circleColor);

			if (!File.Exists(path))
			{
				Console.Error.WriteLine($"[Cinematic] Texture spatiale introuvable : {path}");
				return default;
			}

			Texture2D texture = Raylib.LoadTexture(path);
			if (texture.Id == 0)
			{
				Console.Error.WriteLine($"[Cinematic] Impossible de charger la texture spatiale : {path}");
				return default;
			}

			Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
			return texture;
		}

		private static Texture2D LoadSoulArrivalCircleTexture(Color color)
		{
			Image image = CreateSoulArrivalCircleImage(color);
			Texture2D texture = Raylib.LoadTextureFromImage(image);
			Raylib.UnloadImage(image);
			if (texture.Id == 0)
			{
				Console.Error.WriteLine("[Cinematic] Impossible de créer un disque de planète.");
				return default;
			}

			Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
			return texture;
		}

		private static unsafe Image CreateSoulArrivalCircleImage(Color color)
		{
			const int size = 542;
			const float center = 270.5f;
			const float radiusSquared = 256f * 256f;
			Image image = Raylib.GenImageColor(size, size, new Color((byte)255, (byte)255, (byte)255, (byte)0));
			Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
			Color* pixels = (Color*)image.Data;

			for (int y = 15; y < 527; y++)
			{
				for (int x = 15; x < 527; x++)
				{
					int index = y * size + x;
					pixels[index] = new Color(color.R, color.G, color.B, (byte)0);
					float dx = x - center;
					float dy = y - center;
					if (dx * dx + dy * dy <= radiusSquared)
						pixels[index].A = 255;
				}
			}

			return image;
		}

		private static bool TryGetSoulArrivalFlatCircleColor(string path, out Color color)
		{
			string normalizedPath = path.Replace('\\', '/');
			const string biomePrefix = "assets/space/terrestrial/biomes/";
			if (normalizedPath.StartsWith(biomePrefix, StringComparison.OrdinalIgnoreCase))
			{
				string[] pathParts = normalizedPath[biomePrefix.Length..].Split('/');
				if (pathParts.Length == 2
					&& SoulArrivalBiomeCircleColors.TryGetValue(pathParts[0], out Color[]? layerColors))
				{
					string fileName = Path.GetFileNameWithoutExtension(pathParts[1]);
					if (fileName.StartsWith("maskie", StringComparison.OrdinalIgnoreCase)
						&& int.TryParse(fileName.AsSpan("maskie".Length), out int layerNumber)
						&& layerNumber > 0 && layerNumber <= layerColors.Length)
					{
						color = layerColors[layerNumber - 1];
						return true;
					}
				}
			}

			return SoulArrivalFlatCircleColors.TryGetValue(normalizedPath, out color);
		}

		private static unsafe Texture2D LoadSoulArrivalMaskedSpaceTexture(string imagePath, string maskPath)
		{
			bool hasFlatCircleColor = TryGetSoulArrivalFlatCircleColor(imagePath, out Color circleColor);
			if ((!hasFlatCircleColor && !File.Exists(imagePath)) || !File.Exists(maskPath))
			{
				Console.Error.WriteLine($"[Cinematic] Texture spatiale ou masque introuvable : {imagePath} / {maskPath}");
				return default;
			}

			Image image = hasFlatCircleColor
				? CreateSoulArrivalCircleImage(circleColor)
				: Raylib.LoadImage(imagePath);
			Image mask = Raylib.LoadImage(maskPath);
			if (image.Width <= 0 || image.Height <= 0 || mask.Width <= 0 || mask.Height <= 0)
			{
				Console.Error.WriteLine($"[Cinematic] Impossible de charger la texture spatiale ou le masque : {imagePath} / {maskPath}");
				if (image.Width > 0 && image.Height > 0) Raylib.UnloadImage(image);
				if (mask.Width > 0 && mask.Height > 0) Raylib.UnloadImage(mask);
				return default;
			}

			if (image.Width != mask.Width || image.Height != mask.Height)
			{
				Console.Error.WriteLine($"[Cinematic] Dimensions incompatibles entre la texture et son masque : {imagePath} / {maskPath}");
				Raylib.UnloadImage(image);
				Raylib.UnloadImage(mask);
				return default;
			}

			Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
			Raylib.ImageFormat(ref mask, PixelFormat.UncompressedR8G8B8A8);
			Color* imagePixels = (Color*)image.Data;
			Color* maskPixels = (Color*)mask.Data;
			int pixelCount = image.Width * image.Height;
			for (int i = 0; i < pixelCount; i++)
				imagePixels[i].A = Math.Min(imagePixels[i].A, maskPixels[i].A);

			Raylib.UnloadImage(mask);
			Texture2D texture = Raylib.LoadTextureFromImage(image);
			Raylib.UnloadImage(image);
			if (texture.Id == 0)
			{
				Console.Error.WriteLine($"[Cinematic] Impossible de créer la texture spatiale masquée : {imagePath}");
				return default;
			}

			Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
			return texture;
		}

		private static void UnloadSoulArrivalSpaceTextures()
		{
			if (_soulArrivalWaterTexture.Id != 0) Raylib.UnloadTexture(_soulArrivalWaterTexture);
			if (_soulArrivalNarratorStarTexture.Id != 0) Raylib.UnloadTexture(_soulArrivalNarratorStarTexture);
			foreach (Texture2D texture in _soulArrivalGardenLandTextures)
				if (texture.Id != 0) Raylib.UnloadTexture(texture);
			if (_soulArrivalGasGiantTexture.Id != 0) Raylib.UnloadTexture(_soulArrivalGasGiantTexture);
			if (_soulArrivalPlanetShadowTexture.Id != 0) Raylib.UnloadTexture(_soulArrivalPlanetShadowTexture);
			foreach (SoulArrivalBackdropPlanet planet in _soulArrivalBackdropPlanets)
			{
				foreach (Texture2D layer in planet.LandLayers)
					if (layer.Id != 0) Raylib.UnloadTexture(layer);
				if (planet.DetailTexture.Id != 0) Raylib.UnloadTexture(planet.DetailTexture);
				if (planet.ShadowTexture.Id != 0) Raylib.UnloadTexture(planet.ShadowTexture);
			}

			_soulArrivalWaterTexture = default;
			_soulArrivalNarratorStarTexture = default;
			_soulArrivalGardenLandTextures = Array.Empty<Texture2D>();
			_soulArrivalGasGiantTexture = default;
			_soulArrivalPlanetShadowTexture = default;
			_soulArrivalBackdropPlanets = Array.Empty<SoulArrivalBackdropPlanet>();
			_soulArrivalSpaceTexturesInitialized = false;
		}

		private static void DrawSoulArrivalSpaceBackdrop(float time)
		{
			EnsureSoulArrivalSpaceTextures();

			int screenWidth = Raylib.GetScreenWidth();
			int screenHeight = Raylib.GetScreenHeight();
			float scale = Math.Min(screenWidth, screenHeight);

			foreach (SoulArrivalBackdropPlanet planet in _soulArrivalBackdropPlanets)
			{
				Vector2 center = new(
					screenWidth * planet.Position.X + MathF.Sin(time * 0.035f + planet.Rotation) * 12f,
					screenHeight * planet.Position.Y);
				float rotation = planet.Rotation + time * planet.RotationSpeed;
				float diameter = scale * planet.Size;
				if (planet.IsGasGiant)
					DrawSoulArrivalPlanet(planet.BaseTexture, planet.DetailTexture, planet.ShadowTexture, center, diameter, rotation, planet.Tint);
				else
					DrawSoulArrivalTerrestrialPlanet(planet.LandLayers, center, diameter, rotation, planet.ShadowTexture);
			}
		}

		private static void DrawSoulArrivalPlanet(
			Texture2D baseTexture,
			Texture2D detailTexture,
			Texture2D shadowTexture,
			Vector2 center,
			float diameter,
			float rotation,
			Color tint)
		{
			if (baseTexture.Id == 0) return;

			DrawSpaceTexture(baseTexture, center, diameter, rotation, tint);
			if (detailTexture.Id != 0)
				DrawSpaceTexture(detailTexture, center, diameter, rotation, tint);
			if (shadowTexture.Id != 0)
				DrawSpaceTexture(shadowTexture, center, diameter, rotation, Color.White);
		}

		private static void DrawSoulArrivalTerrestrialPlanet(
			Texture2D[] landLayers,
			Vector2 center,
			float diameter,
			float rotation,
			Texture2D shadowTexture)
		{
			if (_soulArrivalWaterTexture.Id != 0)
				DrawSpaceTexture(_soulArrivalWaterTexture, center, diameter, rotation, Color.White);
			foreach (Texture2D layer in landLayers)
				if (layer.Id != 0) DrawSpaceTexture(layer, center, diameter, rotation, Color.White);
			if (shadowTexture.Id != 0)
				DrawSpaceTexture(shadowTexture, center, diameter, rotation, Color.White);
		}

		private static void DrawSpaceTexture(Texture2D texture, Vector2 center, float diameter, float rotation, Color tint)
		{
			Rectangle source = new(0f, 0f, texture.Width, texture.Height);
			Rectangle destination = new(center.X, center.Y, diameter, diameter);
			Vector2 origin = new(diameter * 0.5f, diameter * 0.5f);
			Raylib.DrawTexturePro(texture, source, destination, origin, rotation, tint);
		}

		private static void DrawSoulArrivalSky(int screenWidth, int screenHeight)
		{
			if (!_soulArrivalSkyShaderInitialized)
			{
				_soulArrivalSkyShaderInitialized = true;
				_soulArrivalSkyShader = Raylib.LoadShaderFromMemory(null, SoulArrivalSkyFragmentShader);
				if (_soulArrivalSkyShader.Id != 0)
				{
					_soulArrivalSkyTimeLocation = Raylib.GetShaderLocation(_soulArrivalSkyShader, "time");
					_soulArrivalSkyResolutionLocation = Raylib.GetShaderLocation(_soulArrivalSkyShader, "resolution");
				}
				if (_soulArrivalSkyShader.Id == 0
					|| _soulArrivalSkyTimeLocation < 0
					|| _soulArrivalSkyResolutionLocation < 0)
					Console.Error.WriteLine("[Cinematic] Impossible de charger le shader du fond spatial.");
			}

			if (_soulArrivalSkyShader.Id == 0
				|| _soulArrivalSkyTimeLocation < 0
				|| _soulArrivalSkyResolutionLocation < 0)
			{
				Raylib.DrawRectangleGradientV(
					0,
					0,
					screenWidth,
					screenHeight,
					new Color(15, 11, 37, 255),
					new Color(22, 30, 69, 255));
				return;
			}

			float time = (float)Raylib.GetTime();
			Raylib.SetShaderValue(_soulArrivalSkyShader, _soulArrivalSkyResolutionLocation,
				new Vector2(screenWidth, screenHeight), ShaderUniformDataType.Vec2);
			Raylib.SetShaderValue(_soulArrivalSkyShader, _soulArrivalSkyTimeLocation, time, ShaderUniformDataType.Float);
			Raylib.BeginShaderMode(_soulArrivalSkyShader);
			Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, Color.White);
			Raylib.EndShaderMode();
		}

		private static void DrawSoulArrivalStars(int screenWidth, int screenHeight, float time)
		{
			const float cellSize = 72f;
			if (_soulArrivalStarsWidth != screenWidth || _soulArrivalStarsHeight != screenHeight)
			{
				Random random = new(0x51A7);
				List<SoulArrivalStar> stars = new();
				int cellsX = (int)MathF.Ceiling(screenWidth / cellSize);
				int cellsY = (int)MathF.Ceiling(screenHeight / cellSize);
				for (int cellY = 0; cellY < cellsY; cellY++)
				{
					for (int cellX = 0; cellX < cellsX; cellX++)
					{
						if (random.NextSingle() > 0.42f)
							continue;

						float x = (cellX + random.NextSingle()) * cellSize;
						float y = (cellY + random.NextSingle()) * cellSize;
						bool bright = random.NextSingle() < 0.07f;
						stars.Add(new SoulArrivalStar(
							x / screenWidth,
							y / screenHeight,
							bright ? 1.15f + random.NextSingle() * 0.45f : 0.55f + random.NextSingle() * 0.55f,
							bright ? 150f + random.NextSingle() * 85f : 65f + random.NextSingle() * 110f,
							random.NextSingle() * MathF.Tau,
							0.45f + random.NextSingle() * 0.9f,
							bright ? 0.14f + random.NextSingle() * 0.12f : 0.05f + random.NextSingle() * 0.08f));
					}
				}

				_soulArrivalStars = stars.ToArray();
				_soulArrivalStarsWidth = screenWidth;
				_soulArrivalStarsHeight = screenHeight;
			}

			foreach (SoulArrivalStar star in _soulArrivalStars)
			{
				float x = (star.X * screenWidth + time * 2.2f) % screenWidth;
				float y = (star.Y * screenHeight + time * 0.35f) % screenHeight;
				float twinkle = 1f - star.TwinkleAmount
					* (0.5f + 0.5f * MathF.Sin(time * star.TwinkleSpeed + star.TwinklePhase));
				int alpha = Math.Clamp((int)MathF.Round(star.Brightness * twinkle), 0, 255);
				Color color = new(220, 226, 255, alpha);
				if (star.Radius < 0.9f)
					Raylib.DrawPixel((int)x, (int)y, color);
				else
					Raylib.DrawCircle((int)x, (int)y, star.Radius, color);
			}
		}

		private static void DrawSoulArrivalTemperatePlanet(Vector2 center, float diameter, float time)
		{
			DrawSoulArrivalTerrestrialPlanet(
				_soulArrivalGardenLandTextures,
				center,
				diameter,
				time * 0.012f,
				_soulArrivalPlanetShadowTexture);
		}

		private static void DrawSoulGlow(int x, int y, float radius)
		{
			Raylib.BeginBlendMode(BlendMode.Additive);
			Raylib.DrawCircleGradient(x, y, radius, new Color(150, 210, 255, 95), new Color(40, 90, 210, 0));
			Raylib.EndBlendMode();
		}

		private static void DrawSoulTrail(string trailId, Vector2 position, float opacity, float time)
		{
			if (opacity <= 0f) return;

			if (!SoulTrailSamples.TryGetValue(trailId, out List<SoulTrailSample>? samples))
			{
				samples = new List<SoulTrailSample>();
				SoulTrailSamples.Add(trailId, samples);
			}

			if (samples.Count > 0 && time < samples[^1].Time)
				samples.Clear();

			if (samples.Count == 0 || time - samples[^1].Time >= SoulTrailSampleInterval)
				samples.Add(new SoulTrailSample(position, time));

			for (int i = samples.Count - 1; i >= 0; i--)
			{
				if (time - samples[i].Time > SoulTrailLifetime)
					samples.RemoveAt(i);
			}

			Raylib.BeginBlendMode(BlendMode.Additive);
			foreach (SoulTrailSample sample in samples)
			{
				float age = Math.Max(0f, time - sample.Time);
				float fade = MathF.Pow(1f - age / SoulTrailLifetime, 1.6f) * opacity;
				float radius = 2.5f + 3.5f * (1f - age / SoulTrailLifetime);
				Raylib.DrawCircleGradient(
					(int)sample.Position.X,
					(int)sample.Position.Y,
					radius * 2f,
					new Color(110, 185, 255, (int)(45f * fade)),
					new Color(80, 135, 255, 0));
				Raylib.DrawCircle(
					(int)sample.Position.X,
					(int)sample.Position.Y,
					radius,
					new Color(150, 215, 255, (int)(75f * fade)));
			}
			Raylib.EndBlendMode();
		}

		private static void DrawSoulArrivalCharacter(Vector2 position, bool closeEyes = false)
		{
			DrawCharacterPreviewWithRotation(position, SkinColor, PlayerHairStyle, PlayerBeardStyle, HairColor, EyeStyle, EyeColor, 1f, closeEyes, customScale: 2.4f);
		}

		private static void DrawSoulInsideCharacter(Vector2 characterPosition, float customScale, float alpha)
		{
			float time = (float)Raylib.GetTime();
			float characterTopY = EntityRenderer.GetVisualTopY("human", "idle", 0, 0f, 1f, characterPosition, customScale);
			float chestY = characterTopY + (characterPosition.Y - characterTopY) * 0.67f;
			Vector2 soulPosition = new(
				characterPosition.X + MathF.Sin(time * 1.7f) * 2f,
				chestY + MathF.Sin(time * 2.3f) * 2f);
			DrawSoulTrail("arrival-character", soulPosition, alpha, time);
			DrawSoulLight(soulPosition, alpha);
		}

		private static void DrawSoulLight(Vector2 position, float opacity)
		{
			if (opacity <= 0f) return;

			int x = (int)position.X;
			int y = (int)position.Y;
			float pulse = 0.95f + 0.05f * MathF.Sin((float)Raylib.GetTime() * 4.5f);
			float alpha = Math.Clamp(opacity, 0f, 1f) * pulse;
			Raylib.BeginBlendMode(BlendMode.Additive);
			Raylib.DrawCircleGradient(x, y, 36f, new Color(95, 165, 255, (int)(45f * alpha)), new Color(80, 150, 255, 0));
			Raylib.DrawCircleGradient(x, y, 18f, new Color(125, 200, 255, (int)(80f * alpha)), new Color(105, 175, 255, 0));
			Raylib.DrawCircleGradient(x, y, 9f, new Color(220, 245, 255, (int)(235f * alpha)), new Color(180, 225, 255, 0));
			Raylib.EndBlendMode();
		}
	}
}
