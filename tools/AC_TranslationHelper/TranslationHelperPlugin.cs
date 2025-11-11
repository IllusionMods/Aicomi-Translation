using AC.Scene.Explore.UI;
using AC.Scene.FreeH.CharaStateSelect;
using AC.Scene.Home.UI;
using AC.Scene.Home.UI.Callsign;
using AC.UI;
using AC_TranslationHelper;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Character;
using HarmonyLib;
using System.Reflection;
using TMPro;
using UnityEngine.UI;
using XUnity.AutoTranslator.Plugin.Core;

[assembly: AssemblyTitle(TranslationHelperPlugin.DisplayName)]
[assembly: AssemblyProduct(TranslationHelperPlugin.DisplayName)]
[assembly: AssemblyDescription("Workaround for some things not getting translated by AutoTranslator.")]
[assembly: AssemblyVersion(TranslationHelperPlugin.Version)]
[assembly: AssemblyCompany("https://github.com/IllusionMods/Aicomi-Translation")]

namespace AC_TranslationHelper
{
    [BepInPlugin(GUID, DisplayName, Version)]
    [BepInDependency("gravydevsupreme.xunity.autotranslator", ">=5.4")]
    public class TranslationHelperPlugin : BasePlugin
    {
        public const string Version = "1.0";
        public const string GUID = "TranslationHelper";
        internal const string DisplayName = "Translation Helper";
        internal static ManualLogSource Logger = null!;
        private static ConfigEntry<FirstNameOrder> _firstNameOrder = null!;

        public enum FirstNameOrder
        {
            Auto,
            FirstnameFirst,
            LastnameFirst
        }

        public override void Load()
        {
            Logger = Log;
            _firstNameOrder = Config.Bind("Translation", "Move first names to front", FirstNameOrder.Auto,
                                          "Auto will move first name to front if translating to English. For other languages it will stay in the original order.");
            Harmony.CreateAndPatchAll(typeof(Hooks));
        }

        private static string TryTranslateName(string firstname, string lastname, char separator)
        {
            var name = new Il2CppSystem.ValueTuple<string, string>(firstname, lastname);
            TryTranslateName(ref name);
            return name.Item1 + (string.IsNullOrWhiteSpace(name.Item2) ? "" : separator + name.Item2);
        }

        private static bool TryTranslateName(ref Il2CppSystem.ValueTuple<string, string> __result)
        {
            var result = false;

            if (!string.IsNullOrWhiteSpace(__result.Item1) && AutoTranslator.Default.TryTranslate(__result.Item1, out var tl))
            {
                __result.Item1 = tl;
                result = true;
            }

            if (!string.IsNullOrWhiteSpace(__result.Item2) && AutoTranslator.Default.TryTranslate(__result.Item2, out var tl2))
            {
                __result.Item2 = tl2;
                result = true;
            }

            return result;
        }

        /// <summary>
        /// Workaround for names in roster screen and some other things not getting translated until a manual translation reload.
        /// Sometime AT misses the text update if it happens purely on the il2cpp side, this makes AT re-check the text
        /// </summary>
        private static void Touch(params TMP_Text?[] texts)
        {
            foreach (var text in texts)
            {
                if (text != null)
                    text.text = text.text;
            }
        }

        private static bool FirstNameFirst()
        {
            switch (_firstNameOrder.Value)
            {
                default:
                case FirstNameOrder.Auto:
                    var isEnglish = string.Equals(AutoTranslatorSettings.DestinationLanguage, "en", StringComparison.OrdinalIgnoreCase);
                    return isEnglish;
                case FirstNameOrder.FirstnameFirst:
                    return true;
                case FirstNameOrder.LastnameFirst:
                    return false;
            }
        }

        private static class Hooks
        {
            /// <summary>
            /// Translate full character name before it is used by the game. Takes care of most of the UI.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(typeof(HumanDataParameter), nameof(HumanDataParameter.fullname), MethodType.Getter)]
            public static void Postfix_fullname_get(HumanDataParameter __instance, ref string __result)
            {
                var newName = FirstNameFirst() ? TryTranslateName(__instance.firstname, __instance.lastname, ' ') : TryTranslateName(__instance.lastname, __instance.firstname, ' ');
                System.Diagnostics.Debug.Write($"Fullname translated: {__result} -> {newName}");
                __result = newName;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(BaseStateSelect), nameof(BaseStateSelect.GetName))]
            public static void Postfix_BaseStateSelect_GetName(HumanData data, ref Il2CppSystem.ValueTuple<string, string> __result)
            {
                System.Diagnostics.Debug.Write("Postfix_BaseStateSelect_GetName");
                TryTranslateName(ref __result);
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(BaseStateSelect), nameof(BaseStateSelect.Set))]
            public static void Postfix_BaseStateSelect_Set(BaseStateSelect __instance, HumanData? data)
            {
                System.Diagnostics.Debug.Write("Postfix_BaseStateSelect_Set");
                if (data == null) return;

                var tmp = __instance._txtName.GetTmpText();
                if (tmp == null) return;

                tmp.text = FirstNameFirst() ? TryTranslateName(data.Parameter.firstname, data.Parameter.lastname, '\n') : TryTranslateName(data.Parameter.lastname, data.Parameter.firstname, '\n');
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(Passport), nameof(Passport.SetParameter))]
            public static void Postfix_Passport_SetParameter(Passport __instance)
            {
                System.Diagnostics.Debug.Write("Postfix_Passport_SetParameter");
                Touch(__instance._txtName);
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(HumanParameterUI), nameof(HumanParameterUI.Refresh))]
            public static void Postfix_HumanParameterUI_Refresh(HumanParameterUI __instance)
            {
                System.Diagnostics.Debug.Write("Postfix_HumanParameterUI_Refresh");
                Touch(__instance._txtActivity, __instance._txtBirthday, __instance._txtBloodType, __instance._txtCallsign, __instance._txtErogenousZone, __instance._txtPersonality);
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerStatsUI), nameof(PlayerStatsUI.Open))]
            public static void Postfix_PlayerStatsUI_Open(PlayerStatsUI __instance)
            {
                System.Diagnostics.Debug.Write("Postfix_PlayerStatsUI_Open");
                Touch(__instance._txtName, __instance._txtBirthday, __instance._txtBlood, __instance._txtLocation,
                      __instance._txtHeroineName, __instance._txtHeroineBirthday, __instance._txtHeroineBlood, __instance._txtHeroineActivity);
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(CallsignEditorUI), nameof(CallsignEditorUI.Refresh))]
            public static void Postfix_CallsignEditorUI_Refresh(CallsignEditorUI __instance, int type)
            {
                System.Diagnostics.Debug.Write("Postfix_CallsignEditorUI_Refresh");
                Touch(__instance._callsignOptions[type]!.CastToEnumerable<CallsignOption>().SelectMany(x => x._texts).ToArray());
            }

            // TODO:
            // In-game character list > h stats mode > scrolling up and down causes translations to get lost and not picked up automatically
            // This doesn't work, seems like the text is updated through a binding which I couldn't find a place to hook into
            //[HarmonyPostfix]
            //[HarmonyPatch(typeof(HeroineListElement), nameof(HeroineListElement.RefreshInfoMode))]
            //public static void Postfix_HeroineListElement_RefreshInfoMode(HeroineListElement __instance, int type)
            //{
            //    System.Diagnostics.Debug.Write("Postfix_CallsignEditorUI_Refresh");
            //    Touch(__instance._txtState);
            //}

            /// <summary>
            /// Workaround for images not getting translated
            /// Fix found by @ekibun
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(typeof(Image), nameof(Image.OnEnable))]
            public static void Postfix_Image_OnEnable(Image __instance)
            {
                // texture access triggers AT hooks
                if (__instance.sprite != null)
                    _ = __instance.sprite.texture;
            }
        }
    }
}
