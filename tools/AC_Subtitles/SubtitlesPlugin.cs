using System.Reflection;
using System.Text.Json;
using AC_Subtitles;
using AC.Scene.Touch;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using H;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: AssemblyTitle(SubtitlesPlugin.DisplayName)]
[assembly: AssemblyProduct(SubtitlesPlugin.DisplayName)]
[assembly: AssemblyVersion(SubtitlesPlugin.Version)]
[assembly: AssemblyDescription("Show subtitles in H and Touch scenes")]
[assembly: AssemblyCompany("https://gitgoon.dev/IllusionMods/Aicomi-Translation")]

namespace AC_Subtitles;

[BepInPlugin(GUID, DisplayName, Version)]
[BepInDependency("gravydevsupreme.xunity.autotranslator", BepInDependency.DependencyFlags.SoftDependency)]
public class SubtitlesPlugin : BasePlugin
{
    public const string Version = "1.0";
    public const string GUID = "AC_Subtitles";
    public const string DisplayName = "AC Subtitles";

    private const string SubtitlesFilename = "AC_Subtitles.json";

    internal static ConfigEntry<bool> EnablePlugin = null!;
    internal static ConfigEntry<string> LanguageOverride = null!;
    internal static ConfigEntry<bool> ShowCharaName = null!;

    internal static new ManualLogSource Log = null!;
    internal static Dictionary<string, string> SubtitleMap = null!;

    private static GameObject? _canvasObject;

    public override void Load()
    {
        Log = base.Log;

        EnablePlugin = Config.Bind("General", "Enable Subtitles", true, "Reload the game to Enable/Disable.");
        LanguageOverride = Config.Bind("General", "Subtitle Language", "auto",
                                      $"Language of the subtitles.\nThe subtitles are loaded from 'BepInEx/Translation/<this setting>/{SubtitlesFilename}'.\nIf set to 'auto' or empty, AutoTranslator's Destination Language is used.");
        ShowCharaName = Config.Bind("General", "Show character name", true, "Show character's full name next to the subtitle.");

        if (!EnablePlugin.Value) return;

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
        var languageCode = LanguageOverride.Value.Trim();
        if (languageCode == string.Empty || languageCode == "auto")
            languageCode = AutoTranslatorHelper.TryGetAutoTranslatorLanguage();
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
        [HarmonyPatch(typeof(TouchController), nameof(TouchController.Setup))]
        private static void HSceneInitialize()
        {
            MakeCanvas(SceneManager.GetActiveScene());
        }
    }
}