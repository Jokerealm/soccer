using System;
using System.IO;
using System.Linq;
using Soccer.Core;
using Soccer.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Soccer.Editor
{
    /// <summary>可复现的初始资源导入、场景组装及构建入口。已有数值资产不会被覆盖。</summary>
    public static class ProjectSetup
    {
        private const string Root = "Assets/Soccer/";
        private const string ScenePath = "Assets/Soccer/Scenes/Prototype.unity";
        private static readonly string[] Ids = { "wind-shuttle", "iron-guard", "arc-light" };
        [Serializable] private sealed class ImportPlan { public ImportCharacter[] characters; }
        [Serializable] private sealed class ImportCharacter { public string id; public ImportClip[] clips; }
        [Serializable] private sealed class ImportClip { public string action; public string texture; public float ppu; public float fps; public ImportFrame[] frames; }
        [Serializable] private sealed class ImportFrame { public string name; public float[] rect; public float[] pivot; }
        [Serializable] private sealed class BuildSummary { public string target; public string result; public string editor; public string output; public int errors; public int warnings; public string[] warningMessages; public long bytes; }

        [MenuItem("技能足球/初始化资源与场景（保留已有配置）")]
        public static void Initialize()
        {
            Directory.CreateDirectory(Root + "Data");
            Directory.CreateDirectory(Root + "Scenes");
            AssetDatabase.Refresh();
            ImportAudio();
            ImportFrontEnd();
            ImportTurf();
            CreateSceneMaterials();
            ImportAnimations();
            CreateChineseFont();
            CreateChineseBoldFont();
            AssetDatabase.ImportAsset(Root + "UI/Game.uss", ImportAssetOptions.ForceUpdate);
            var rules = LoadOrCreate<MatchRulesAsset>(Root + "Data/PrototypeRules.asset");
            if (rules.Rules.RuleVersion != "star-strikers-prototype-0.22.0")
            {
                rules.Rules.EnableCombat = true;
                rules.Rules.EnableOutOfPlay = true; rules.Rules.BallRadius = .27f;
                rules.Rules.RuleVersion = "star-strikers-prototype-0.22.0";
                rules.Rules.MovementSpeedScale = 1.6f;
                rules.Rules.PossessionSpeed = .8f; rules.Rules.PossessionContactLock = .25f;
                rules.Rules.KickCooldown = 6; rules.Rules.TackleCooldown = 5; rules.Rules.TackleHealthCostFactor = 10;
                rules.Assumptions = "参考用户提供的星际足球表格，先实现枪兵与雷神。统一 100 生命、100 能量；V 键踢球冷却 6 秒，空格铲球冷却 5 秒，启动扣血 10/推力（原型公式）。常规二维规则与三维美术保留，5 秒原地复活。雷神驱动时长、突进距离、炸弹范围与武器数值为可调原型值。";
                EditorUtility.SetDirty(rules);
            }
            var definitions = CreateDefinitions();
            ConfigureProject();
            if (!File.Exists(ScenePath)) CreateScene(rules, definitions);
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var controller = UnityEngine.Object.FindObjectOfType<AppController>();
            controller.Characters = definitions;
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("SOCCER_PROJECT_INITIALIZED");
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void ImportAudio()
        {
            foreach (string path in Directory.GetFiles(Root + "Resources/SoccerAudio", "*.wav"))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                bool music = Path.GetFileName(path) == "menu-loop.wav" || Path.GetFileName(path) == "match-loop.wav";
                var settings = importer.defaultSampleSettings;
                var load = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                var format = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
                if (settings.loadType == load && settings.compressionFormat == format &&
                    settings.preloadAudioData == !music && importer.loadInBackground == music) continue;
                settings.loadType = load; settings.compressionFormat = format; settings.quality = 0.8f;
                settings.preloadAudioData = !music;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = music;
                importer.SaveAndReimport();
            }
        }

        private static void ImportFrontEnd()
        {
            foreach (string path in Directory.GetFiles(Root + "Resources/FrontEnd", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer.textureType == TextureImporterType.Default && !importer.mipmapEnabled &&
                    importer.maxTextureSize == 4096 && importer.textureCompression == TextureImporterCompression.Uncompressed) continue;
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.maxTextureSize = 4096; importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        private static void ImportTurf()
        {
            var path=Root+"Resources/SceneSurface/turf-detail-v4.png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;
            importer.wrapMode=TextureWrapMode.Repeat; importer.mipmapEnabled=true;
            importer.npotScale=TextureImporterNPOTScale.None; importer.maxTextureSize=2048;
            importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=8;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void ImportAnimations()
        {
            var plan = JsonUtility.FromJson<ImportPlan>(File.ReadAllText(Root + "Editor/animation-import.json"));
            foreach (var character in plan.characters)
            {
                var bank = LoadOrCreate<CharacterAnimations>(Root + $"Data/{character.id}-animations.asset");
                foreach (var clip in character.clips)
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(clip.texture);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Multiple;
                    importer.spritePixelsPerUnit = clip.ppu;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.filterMode = FilterMode.Bilinear;
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                    var factory = new SpriteDataProviderFactories();
                    factory.Init();
                    var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
                    provider.InitSpriteEditorDataProvider();
                    var oldRects = provider.GetSpriteRects();
                    var rects = clip.frames.Select(f => new SpriteRect
                    {
                        name = f.name, rect = new Rect(f.rect[0], f.rect[1], f.rect[2], f.rect[3]),
                        alignment = SpriteAlignment.Custom, pivot = new Vector2(f.pivot[0], f.pivot[1]),
                        spriteID = oldRects.FirstOrDefault(r => r.name == f.name)?.spriteID ?? GUID.Generate()
                    }).ToArray();
                    provider.SetSpriteRects(rects);
                    provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(
                        rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
                    provider.Apply();
                    importer.SaveAndReimport();
                    var sprites = AssetDatabase.LoadAllAssetsAtPath(clip.texture).OfType<Sprite>().OrderBy(s => s.name).ToArray();
                    if (sprites.Length != clip.frames.Length) throw new Exception($"{clip.texture} 精灵数不匹配");
                    if (clip.action == "run") { bank.Run = sprites; bank.RunFps = clip.fps; }
                    if (clip.action == "turn") bank.Turn = sprites;
                    if (clip.action == "kick") { bank.Kick = sprites; bank.KickFps = clip.fps; }
                }
                EditorUtility.SetDirty(bank);
            }
        }

        private static void CreateSceneMaterials()
        {
            string folder = Root + "Resources/SceneSurface";
            Directory.CreateDirectory(folder);
            var sparkShader=Shader.Find("Legacy Shaders/Particles/Additive");
            var sparkPath=folder+"/Combat-Sparks.mat";
            var spark=AssetDatabase.LoadAssetAtPath<Material>(sparkPath);
            if(spark==null) {spark=new Material(sparkShader); AssetDatabase.CreateAsset(spark,sparkPath);}
            spark.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Resources/FrontEnd/fx-spark-v2.png");
            spark.SetColor("_TintColor",Color.white*.5f); EditorUtility.SetDirty(spark);
            var shader = Shader.Find("Standard");
            string[] names = { "Floor", "Ball-Ivory", "Ball-Navy", "Ball-Seam", "Goal-Cyan", "Goal-Amber" };
            Color[] colors = { Color.white, new Color(0.97f,0.97f,0.9f), new Color(0.06f,0.14f,0.18f),
                new Color(0.035f,0.065f,0.075f), new Color(0.68f,0.91f,0.85f), new Color(1,0.86f,0.58f) };
            for (int i = 0; i < names.Length; i++)
            {
                string path = folder + "/" + names[i] + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material,path); }
                material.color = colors[i]; material.SetFloat("_Metallic", i >= 4 ? 0.25f : 0.03f);
                material.SetFloat("_Glossiness", i == 0 ? 0.12f : 0.33f);
                if (i == 0)
                {
                    material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Resources/FrontEnd/arena-v3.png");
                    material.SetTexture("_EmissionMap",material.mainTexture);
                    material.SetColor("_EmissionColor",Color.white*0.12f); material.EnableKeyword("_EMISSION");
                }
                EditorUtility.SetDirty(material);
            }
        }

        private static void CreateChineseFont()
        {
            string path = Root + "UI/ChineseFont.asset";
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(path) != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "Art/Fonts/NotoSansCJKsc-Regular.otf");
            var asset = FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            asset.name = "Noto Sans CJK SC Dynamic";
            // 预烘焙 UI 与角色说明中的字形，运行时仍可动态补充新增文本。
            string characters = File.ReadAllText(Root + "UI/Game.uxml") + string.Join("", Directory.GetFiles(Root + "Runtime", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            characters += "风梭铁卫弧光接力稳重远距脚法顺风带侧滑护板扎根弧线准备重炮准备有效时间青队橙队训练完成双方平局0123456789○◇±×→";
            asset.TryAddCharacters(characters);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var atlas in asset.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
        }

        private static void CreateChineseBoldFont()
        {
            string path = Root + "UI/ChineseBold.asset";
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(path) != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "Art/Fonts/NotoSansCJKsc-Bold.otf");
            var asset = FontAsset.CreateFontAsset(font, 96, 8, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic, true);
            asset.name = "Noto Sans CJK SC Bold HD";
            string characters = File.ReadAllText(Root + "UI/Game.uxml") + string.Join("", Directory.GetFiles(Root + "Runtime", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            asset.TryAddCharacters(characters);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var atlas in asset.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static CharacterDefinition[] CreateDefinitions()
        {
            var definitions = new CharacterDefinition[2];
            string[] referenceIds = { "marine", "thor" };
            for (int i = 0; i < definitions.Length; i++)
            {
                string path = Root + $"Data/{referenceIds[i]}.asset";
                bool existing = File.Exists(path);
                var d = LoadOrCreate<CharacterDefinition>(path);
                if (!existing)
                {
                    d.Stats = StarStrikerLoadouts.Create(i == 0 ? CharacterId.Marine : CharacterId.Thor);
                    d.Accent = i == 0 ? new Color(0.26f, 0.82f, 0.78f) : i == 1 ? new Color(0.96f, 0.66f, 0.27f) : new Color(0.68f, 0.47f, 0.84f);
                }
                d.EnsureCombatPrototype();
                d.Portrait = null;
                d.Animations = AssetDatabase.LoadAssetAtPath<CharacterAnimations>(Root + $"Data/{Ids[i]}-animations.asset");
                EditorUtility.SetDirty(d);
                definitions[i] = d;
            }
            return definitions;
        }

        private static Sprite ImportSprite(string relative, float ppu)
        {
            string path = Root + relative;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void CreateScene(MatchRulesAsset rules, CharacterDefinition[] characters)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("World Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 11.25f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.08f, 0.12f);
            camera.gameObject.AddComponent<ArenaCamera>();
            camera.gameObject.AddComponent<AudioListener>();
            var arena = new GameObject("Arena (art only; Core owns geometry)").transform;
            CreateArenaLayer(arena, "Floor", ImportSprite("Art/Arena/standard-floor-v2.png", 1672f / 40), -100);
            CreateArenaLayer(arena, "Field Lines", ImportSprite("Art/Arena/arena-lines-v2.png", 48), -90);
            CreateArenaLayer(arena, "Goal Illustration", ImportSprite("Art/Arena/arena-goals-v2.png", 48), 100);
            var shadow = ImportSprite("Art/Arena/shadow-soft-v2.png", 128);
            var circle = ImportSprite("Art/Arena/team-cyan-ground-v2.png", 128);
            var diamond = ImportSprite("Art/Arena/team-amber-ground-v2.png", 128);
            var ball = ImportSprite("Art/Arena/ball-v2.png", 64);
            var matchView = new GameObject("Match Presentation").AddComponent<MatchView>();
            matchView.ActorRoot = new GameObject("Actors and Effects").transform;
            matchView.ActorRoot.SetParent(matchView.transform, false);
            matchView.BallSprite = ball; matchView.ShadowSprite = shadow;
            matchView.CyanMarker = circle; matchView.AmberMarker = diamond;
            var panel = LoadOrCreate<PanelSettings>(Root + "UI/RuntimePanel.asset");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "UI/RuntimeTheme.tss");
            EditorUtility.SetDirty(panel);
            var uiGo = new GameObject("UI (UXML + USS)");
            var document = uiGo.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "UI/Game.uxml");
            var ui = uiGo.AddComponent<GameUI>();
            ui.Styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "UI/Game.uss");
            var application = new GameObject("Application");
            var audio = application.AddComponent<AudioFeedback>();
            audio.Click = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/click-a.ogg");
            audio.Kick = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/tap-a.ogg");
            audio.Goal = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/switch-a.ogg");
            var app = application.AddComponent<AppController>();
            app.RulesAsset = rules; app.Characters = characters; app.WorldCamera = camera;
            app.View = matchView; app.UI = ui; app.Audio = audio;
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new Exception("场景保存失败");
        }

        private static void CreateArenaLayer(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = order;
        }

        private static void ConfigureProject()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            PlayerSettings.companyName = "Future City League";
            PlayerSettings.productName = "技能足球";
            PlayerSettings.bundleVersion = "0.23.0";
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.futurecity.skillsoccer");
            PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 4;
            QualitySettings.pixelLightCount = 4;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 60;
            Time.fixedDeltaTime = MatchSimulation.TickSeconds;
        }

        [MenuItem("技能足球/构建 macOS 原型")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/macOS/SkillSoccer.app");
        [MenuItem("技能足球/构建 Windows 原型")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/SkillSoccer.exe");

        [MenuItem("技能足球/构建 macOS 调试版")]
        public static void BuildMacDevelopment() => Build(BuildTarget.StandaloneOSX, "Builds/macOS-dev/SkillSoccer.app", BuildOptions.Development);
        [MenuItem("技能足球/构建 Windows 调试版")]
        public static void BuildWindowsDevelopment() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows-dev/SkillSoccer.exe", BuildOptions.Development);

        private static void Build(BuildTarget target, string output, BuildOptions options = BuildOptions.None)
        {
            Initialize();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = target, locationPathName = output,
                options = options
            });
            var summary = new BuildSummary
            {
                target = target.ToString(), result = report.summary.result.ToString(), editor = Application.unityVersion,
                output = Path.GetFullPath(output), errors = (int)report.summary.totalErrors,
                warnings = (int)report.summary.totalWarnings,
                warningMessages = report.steps.SelectMany(step => step.messages).Where(message => message.type == LogType.Warning).Select(message => message.content).ToArray(),
                bytes = (long)report.summary.totalSize
            };
            string reportPath = Path.GetFullPath("../reports/project-build-" + (target == BuildTarget.StandaloneOSX ? "macos" : "windows") + ".json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(summary, true));
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("游戏构建失败");
            Debug.Log("SOCCER_BUILD_PASSED " + target);
        }
    }
}
