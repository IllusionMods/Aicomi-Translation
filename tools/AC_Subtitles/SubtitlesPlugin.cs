using System.Reflection;
using System.Text.Json;
using AC_Subtitles;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using H;
using H.Sound.Voice;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Localize.Translate;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Input = UnityEngine.Input;

[assembly: AssemblyTitle(SubtitlesPlugin.DisplayName)]
[assembly: AssemblyProduct(SubtitlesPlugin.DisplayName)]
[assembly: AssemblyVersion(SubtitlesPlugin.Version)]
[assembly: AssemblyDescription("Show subtitles in H scenes.")]
[assembly: AssemblyCompany("https://github.com/IllusionMods/Aicomi-Translation")]

namespace AC_Subtitles;

[BepInPlugin(GUID, DisplayName, Version)]
[BepInDependency("gravydevsupreme.xunity.resourceredirector", BepInDependency.DependencyFlags.SoftDependency)]
public class SubtitlesPlugin : BasePlugin
{
    public const string Version = "0.0.1";
    public const string GUID = "AC_Subtitles";
    public const string DisplayName = "AC Subtitles";

    private const string SubtitlesFilename = "AC_Subtitles.json";

    private static ConfigEntry<bool> _enableConfig = null!;
    private static ConfigEntry<string> _languageConfig = null!;

    internal static new ManualLogSource Log = null!;
    internal static Dictionary<string, string> SubtitleMap = null!;

    private static GameObject? _canvasObject;

    public override void Load()
    {
        Log = base.Log;

        _enableConfig = Config.Bind("General", "Enable Subtitles", true, "Reload the game to Enable/Disable.");
        _languageConfig = Config.Bind("General", "Subtitle Language", "auto",
                                      $"Language of the subtitles.\nThe subtitles are loaded from 'BepInEx/Translation/<this setting>/{SubtitlesFilename}'.\nIf set to 'auto' or empty, AutoTranslator's Destination Language is used.");

        if (!_enableConfig.Value) return;

        // TODO: Use new autotranslatorstatus loaded event whenever it's merged into AT
        var languageCode = GetTranslationLanguageCode();

        ThreadPool.QueueUserWorkItem(state => Initialize(state as string), languageCode);
    }
    private static void Initialize(string? languageCode)
    {
        if (!TryReadSubtitleMap(languageCode)) return;

        ClassInjector.RegisterTypeInIl2Cpp<SubtitlesCanvas>();

        // Only activate the plugin if initialization succeeded
        Harmony.CreateAndPatchAll(typeof(Hooks));
    }

    internal static string? GetTranslationLanguageCode()
    {
        var languageCode = _languageConfig.Value.Trim();
        if (languageCode == string.Empty || languageCode == "auto")
            languageCode = TranslationHelper.TryGetAutoTranslatorLanguage();
        return languageCode;
    }

    internal static bool TryReadSubtitleMap(string? languageCode)
    {
        var subsPath = GetSubtitlesPath(languageCode);

        try
        {
            var jsonString = File.ReadAllText(subsPath);
            SubtitleMap = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString) ?? throw new FileNotFoundException("Failed to deserialize jsonString, could be an empty file");
            if (SubtitleMap.Count == 0) throw new FileNotFoundException("No subtitles found in the file");
            Log.LogInfo($"Loaded {SubtitleMap.Count} subtitles from \"{subsPath}\"");
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to load subtitles from \"{subsPath}\" - {(e is FileNotFoundException ? e.Message : e.ToString())}");
            return false;
        }

        return true;
    }

    private static string GetSubtitlesPath(string? languageCode)
    {
        if (string.IsNullOrEmpty(languageCode))
        {
            Log.LogWarning($"AutoTranslator not found or has no language set in its config. Please set the Subtitle Language setting manually or place {SubtitlesFilename} in 'BepInEx/Config'.");
        }
        else
        {
            var subsPath = Path.Combine(Paths.BepInExRootPath, "Translation", languageCode, SubtitlesFilename);
            if (File.Exists(subsPath)) return subsPath;
            Log.LogWarning($"Subtitles file not found at \"{subsPath}\". Looking in config instead...");
        }

        var overridePath = Path.Combine(Paths.ConfigPath, SubtitlesFilename);
        if (File.Exists(overridePath)) return overridePath;

        // Fall back to Japanese subtitles if they exist
        return Path.Combine(Paths.BepInExRootPath, "Translation", "ja", SubtitlesFilename);
    }

    /// <summary>
    /// Create a canvas for subtitles and attach it to a given scene so it's automatically destroyed when HScene is unloaded.
    /// </summary>
    private static void MakeCanvas(Scene scene)
    {
        Log.LogDebug($"Creating subtitle canvas in scene {scene.name}");

        UnityEngine.Object.Destroy(_canvasObject);

        _canvasObject = new GameObject("SubtitleCanvas");
        SceneManager.MoveGameObjectToScene(_canvasObject, scene);
        _canvasObject.AddComponent<SubtitlesCanvas>();
    }

    private static class Hooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HScene), nameof(HScene.Initialize), typeof(HScene.InputParameter), typeof(Transform))]
        private static void HSceneInitialize(HScene __result)
        {
            MakeCanvas(SceneManager.GetActiveScene());
        }
    }
}

public class SubtitlesCanvas : MonoBehaviour
{
    private readonly List<CharaSubtitleInfo> _currentSubtitleSources = new();
    private TextMeshProUGUI _subtitleCmp = null!;
    private GameObject _subtitleGo = null!;
    private CanvasGroup _canvasGroupCmp = null!;
    private string _currentDisplayText = "";

    private void Start()
    {
        try
        {
            // Setting canvas attributes
            var canvasScaler = gameObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(Screen.width, Screen.height);

            var canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -2; // Draw under UI
            _canvasGroupCmp = gameObject.AddComponent<CanvasGroup>();
            _canvasGroupCmp.blocksRaycasts = false;

            // Setting subtitle object
            var origTxt = HScene.Instance.transform.Find("UI/LightPanel/Layout/ACT-00-18");
            _subtitleGo = Instantiate(origTxt, transform).gameObject;

            // XUAIGNORE doesn't actually work here because AT checks for it only at Instantiation, this is too late
            _subtitleGo.name = "SubtitleText";

            DestroyImmediate(_subtitleGo.GetComponent<UIBindData>());
            DestroyImmediate(_subtitleGo.GetComponent<TMP_SpriteAnimator>());


            var subtitleRect = _subtitleGo.GetComponent<RectTransform>();
            subtitleRect.anchorMin = subtitleRect.anchorMax = Vector2.zero;
            subtitleRect.offsetMin = new Vector2(300, 100);
            subtitleRect.offsetMax = new Vector2(1620, 1000);

            _subtitleCmp = _subtitleGo.GetComponent<TextMeshProUGUI>();

            _subtitleCmp.fontSize = 31; //(int)(Screen.height / 34.0f);
            _subtitleCmp.alignment = TextAlignmentOptions.Bottom;
            _subtitleCmp.overflowMode = TextOverflowModes.Overflow;
            _subtitleCmp.enableWordWrapping = true;
            _subtitleCmp.color = Color.white;
            _subtitleCmp.characterSpacing = 4;

            _subtitleCmp.text = "";

            // Make sure AT does not try to translate the subtitle text
            TranslationHelper.TryIgnore(_subtitleCmp);
        }
        catch (Exception e)
        {
            SubtitlesPlugin.Log.LogError("Failed to create subtitle canvas! I am die, thank you forever.\n" + e);
            Destroy(gameObject);
        }
    }

    // Using Update because coroutines, onDestroy and onDisable are not working as intended
    private void Update()
    {
        if (!HScene.IsActive())
        {
            // If HScene is dead but we somehow aren't, commit sudoku
            Destroy(gameObject);
            return;
        }

#if DEBUG
        if (Input.GetKeyDown(KeyCode.R))
        {
            SubtitlesPlugin.TryReadSubtitleMap(SubtitlesPlugin.GetTranslationLanguageCode());
            _currentSubtitleSources.Clear();
        }
#endif

        var hScene = HScene._instance;
        var count = hScene.CtrlVoice.NowVoices.Count;

        // Make playingWords count the same as NowVoices count
        if (_currentSubtitleSources.Count != count)
        {
            _currentSubtitleSources.Clear();
            while (_currentSubtitleSources.Count < count)
                _currentSubtitleSources.Add(new CharaSubtitleInfo());
        }

        var changed = false;

        for (var index = 0; index < count; index++)
        {
            var voice = hScene.CtrlVoice.NowVoices[index];
            var chara = hScene._humanReceivers[index];
            var sub = _currentSubtitleSources[index];

            var voiceKey = voice?.VoiceInfo?.Asset;
            var voiceState = voice?.State ?? HVoiceCtrl.VoiceKind.None;
            if (sub.Key == voiceKey && sub.LastState == voiceState)
                continue;

            sub.Key = voiceKey;
            sub.LastState = voiceState;

            changed = true;

            if (chara == null || voiceKey == null ||
                voiceState is not HVoiceCtrl.VoiceKind.Voice and not HVoiceCtrl.VoiceKind.VoiceStart)
            {
                sub.Text = null;
                continue;
            }

            if (!SubtitlesPlugin.SubtitleMap.TryGetValue(voiceKey, out var subtitleText) || string.IsNullOrWhiteSpace(subtitleText))
            {
                SubtitlesPlugin.Log.LogWarning($"Play voice({voiceKey}): Not in subtitleMap!");
                sub.Text = null;
                continue;
            }

            var actorName = chara.fileParam.fullname ?? "???";
            if (TranslationHelper.TryTranslate(actorName, out var tlName))
                actorName = tlName;

            //  Color the text based on character's hair color. Looks bad so disabled for now.
            //    var hair = chara.hair._fileHair.parts.FirstOrDefault(x => x != null && x.baseColor != Color.white);
            //    if (hair != null)
            //    {
            //        string HexConvert(float value)
            //        {
            //            var hex = ((int)(value * 255)).ToString("X");
            //            return hex.Length == 1 ? "0" + hex : hex;
            //        }
            //        sub.Text = $"<color=#{HexConvert(hair.baseColor.r)}{HexConvert(hair.baseColor.g)}{HexConvert(hair.baseColor.b)}FF>{actorName}</color> 「{subtitleText}」";
            //    }
            //    else
            //        sub.Text = $"<color=#FFFFFF>{actorName}</color> 「{subtitleText}」";

            sub.Text = $"{actorName} 「{subtitleText}」";
        }

        if (changed)
        {
            var subtitleText = "";
            for (var i = 0; i < _currentSubtitleSources.Count; i++)
            {
                var word = _currentSubtitleSources[i];
                if (!string.IsNullOrWhiteSpace(word.Text))
                    subtitleText += (subtitleText == "" ? "" : "\n") + word.Text;
            }

            _currentDisplayText = subtitleText.Trim();

            if (_currentDisplayText.Length > 0)
                _subtitleCmp.text = _currentDisplayText;
        }

        if (_currentDisplayText.Length == 0)
        {
            if (_canvasGroupCmp.alpha > 0)
            {
                _canvasGroupCmp.alpha -= Time.deltaTime * 4;

                if (_canvasGroupCmp.alpha <= 0)
                    _subtitleCmp.text = "";
            }
        }
        else
        {
            if (_canvasGroupCmp.alpha < 1)
                _canvasGroupCmp.alpha += Time.deltaTime * 4;
        }
    }

    public record CharaSubtitleInfo
    {
        public string? Key;
        public HVoiceCtrl.VoiceKind LastState;
        public string? Text;
    }
}
