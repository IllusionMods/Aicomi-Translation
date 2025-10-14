using AC.Scene.Touch;
using Character;
using H;
using H.Sound.Voice;
using ILLGAMES.Extensions;
using Localize.Translate;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AC_Subtitles;

public class SubtitlesCanvas : MonoBehaviour
{
    private TextMeshProUGUI _subtitleCmp = null!;
    private GameObject _subtitleGo = null!;
    private CanvasGroup _canvasGroupCmp = null!;

    private string _currentDisplayText = "";
    private string? _currentKey = null;

    private bool _isHscene;

    private void Start()
    {
        try
        {
            _isHscene = HScene.IsActive();

            // Create subtitle canvas
            var canvasScaler = gameObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);

            var canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -2; // Draw under UI
            _canvasGroupCmp = gameObject.AddComponent<CanvasGroup>();
            _canvasGroupCmp.blocksRaycasts = false;

            // Create subtitle text component. Base it on game UI so it looks consistent
            var origTxt = _isHscene
                ? HScene.Instance.transform.Find("UI/LightPanel/Layout/ACT-00-18")
                : TouchController.Instance.transform.Find("Canvas/LightPanel/Layout/ACT-00-18");

            _subtitleGo = Instantiate(origTxt, transform).gameObject;

            // XUAIGNORE doesn't actually work here because AT checks for it only at Instantiation, this is too late
            _subtitleGo.name = "SubtitleText";

            DestroyImmediate(_subtitleGo.GetComponent<UIBindData>());
            DestroyImmediate(_subtitleGo.GetComponent<TMP_SpriteAnimator>());

            // Keep margin from bottom and sides to avoid overlapping with game UI
            var subtitleRect = _subtitleGo.GetComponent<RectTransform>();
            subtitleRect.anchorMin = subtitleRect.anchorMax = Vector2.zero;
            subtitleRect.offsetMin = new Vector2(300, 100);
            subtitleRect.offsetMax = new Vector2(1620, 1000);

            _subtitleCmp = _subtitleGo.GetComponent<TextMeshProUGUI>();
            _subtitleCmp.fontSize = 31;
            _subtitleCmp.alignment = TextAlignmentOptions.Bottom;
            _subtitleCmp.overflowMode = TextOverflowModes.Overflow;
            _subtitleCmp.enableWordWrapping = true;
            _subtitleCmp.color = Color.white;
            _subtitleCmp.characterSpacing = 4;
            _subtitleCmp.text = "";

            // Make sure AT does not try to translate the subtitle text
            AutoTranslatorHelper.TryIgnore(_subtitleCmp);
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
        if (_isHscene && !HScene.IsActive() || !_isHscene && !TouchController.IsActive())
        {
            // If H/Touch is over but we didn't get destroyed, commit sudoku
            Destroy(gameObject);
            return;
        }

#if DEBUG
        if (Input.GetKeyDown(KeyCode.R))
        {
            SubtitlesPlugin.TryReadSubtitleMap(SubtitlesPlugin.GetTranslationLanguageCode());
            _currentKey = null;
        }
#endif

        var newKey = GetCurrentVoiceKey(out var chara);

        if (newKey != _currentKey)
        {
            _currentKey = newKey;
            _currentDisplayText = _currentKey == null ? "" : GetSubtitleText(_currentKey, chara) ?? "";

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

    private string? GetCurrentVoiceKey(out Human? speaker)
    {
        if (_isHscene)
        {
            var hScene = HScene._instance;
            var count = hScene.CtrlVoice.NowVoices.Count;

            for (var index = 0; index < count; index++)
            {
                var voice = hScene.CtrlVoice.NowVoices[index];

                var voiceState = voice?.State ?? HVoiceCtrl.VoiceKind.None;

                if (voiceState is not HVoiceCtrl.VoiceKind.Voice and not HVoiceCtrl.VoiceKind.VoiceStart)
                    continue;

                var chara = hScene._humanReceivers.SafeGet(index);
                if (chara == null)
                    continue;

                var voiceKey = voice?.VoiceInfo?.Asset;
                if (voiceKey == null)
                    continue;

                speaker = chara;
                return voiceKey;
            }
        }
        else
        {
            var touchScene = TouchController.Instance;
            var vc = touchScene._voiceCtrl;
            if (vc.IsPlayWords)
            {
                speaker = vc._human;
                return vc._wordsVoice?._voiceData?.Asset;
            }
        }

        speaker = null;
        return null;
    }

    private static string? GetSubtitleText(string voiceKey, Human? chara)
    {
        if (!SubtitlesPlugin.SubtitleMap.TryGetValue(voiceKey, out var subtitleText) || string.IsNullOrWhiteSpace(subtitleText))
        {
            SubtitlesPlugin.Log.LogWarning($"Played voice clip [{voiceKey}] is not in the subtitle map!");
            return null;
        }

        if (!SubtitlesPlugin.ShowCharaName.Value)
            return $"「{subtitleText}」";

        var actorName = chara?.fileParam?.fullname;
        if (string.IsNullOrWhiteSpace(actorName))
            return $"「{subtitleText}」";

        if (AutoTranslatorHelper.TryTranslate(actorName, out var tlName))
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

        return $"{actorName} 「{subtitleText}」";
    }
}
