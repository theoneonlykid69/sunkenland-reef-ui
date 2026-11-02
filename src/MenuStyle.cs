using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        // Authored treatments run on the existing creation/open hooks. A screen
        // receives its hierarchy treatment once; selection feedback uses events.
        readonly HashSet<UnityEngine.Object> menuScreensV7 = new HashSet<UnityEngine.Object>();
        readonly HashSet<Button> menuButtonsV7 = new HashSet<Button>();
        readonly Dictionary<Toggle, MenuTabV7> menuTabsV7 = new Dictionary<Toggle, MenuTabV7>();
        readonly Dictionary<MonoBehaviour, RectTransform> pauseDocksV8 = new Dictionary<MonoBehaviour, RectTransform>();
        sealed class MenuTabV7
        {
            public Toggle Toggle;
            public TMP_Text[] Labels;
            public Text[] LegacyLabels;
            public Image Surface;
            public Image Rule;
            public bool Hovered;
        }

        internal void StyleMenusV7(MonoBehaviour screen)
        {
            if (!screen) return;
            string type = screen.GetType().Name;
            switch (type)
            {
                case "UIMenu":
                    if (menuScreensV7.Add(screen)) StyleTitleMenuV7(screen);
                    break;
                case "UIGameMenu":
                    // Native Open changes the available actions by session role.
                    // Reflow this one cached column after each Open event.
                    StylePauseMenuV7(screen);
                    break;
                case "UISettings":
                    if (menuScreensV7.Add(screen)) StyleSettingsV7(screen);
                    break;
                case "TopTabToggle":
                    if (menuScreensV7.Add(screen))
                    {
                        var background = Field<Image>(screen, "imgBackground");
                        StyleMenuTabV7(screen.GetComponent<Toggle>(), background ? background.rectTransform : null,
                            Field<TMP_Text>(screen, "tmpText"));
                    }
                    break;
                case "UICharacterSelection":
                case "UICharacterCreation":
                case "UIWorldSelection":
                case "UIWorldCreation":
                case "UIWorldModification":
                    if (menuScreensV7.Add(screen)) StyleCharacterWorldV7(screen, type);
                    break;
                case "UICharacterSelectionEntry":
                case "UIWorldSelectionEntry":
                    StyleEntryV7(screen);
                    break;
                case "UILoadingScreen":
                    // Loading labels may be populated only by the first Open.
                    // This path is still event-driven and each backing is cached.
                    StyleLoadingV7(screen);
                    break;
            }
        }

        void MenuButtonV7(Button button, bool primary = false)
        {
            if (!button || !menuButtonsV7.Add(button)) return;
            DecorateButton(button, primary);
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (IsOurObject(label.transform) && label.name != "ReefUI craft label") continue;
                TextColor(label, primary ? Gold : White, true);
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                // Existing native geometry remains the source of hit areas.
                // Auto sizing prevents longer localized labels from clipping.
                label.enableAutoSizing = true;
                label.fontSizeMax = Mathf.Clamp(label.fontSize, 17, 28);
                label.fontSizeMin = 12;
            }
            UpdateButtonLabels(button);
        }

        void StyleTitleMenuV7(MonoBehaviour screen)
        {
            var dock = screen.transform.Find("ReefUI menu dock") as RectTransform;
            if (dock) DecorateSurface(dock, true);
            var logo=screen.transform.Find("imgLogo") as RectTransform;
            if(logo && logo.rect.width>=300 && logo.rect.width<=1200)
            {
                var plaque=NewContainer(screen.transform as RectTransform,"title plaque");
                plaque.SetAsFirstSibling();
                plaque.anchorMin=logo.anchorMin;plaque.anchorMax=logo.anchorMax;plaque.pivot=logo.pivot;
                plaque.anchoredPosition=logo.anchoredPosition;plaque.sizeDelta=logo.sizeDelta+new Vector2(34,20);
                plaque.localScale=logo.localScale;
                DecorateSurface(plaque,true);
            }
            string[] fields = { "btnStartGame", "btnOpenUITutorial", "btnOpenSettings", "btnQuit" };
            for (int i = 0; i < fields.Length; i++) MenuButtonV7(Field<Button>(screen, fields[i]), i == 0);
            MenuButtonV7(Field<Button>(screen, "btnDiscord"));
            MenuButtonV7(Field<Button>(screen, "btnSteamNews"));
            // The game keeps its animated island, logo and native actions. The
            // dock and its buttons use one quiet material with a bronze frame.
        }

        void StylePauseMenuV7(MonoBehaviour screen)
        {
            string[] fields = { "btnResumeGame", "btnUITutorial", "btnSaveGame", "btnRespawn", "btnServerConsole",
                "btnSettings", "toggleUICombat", "_toggleMarkerBtn", "btnQuitAndSave", "btnQuitWithoutSaving", "btnInviteSteamFriend" };
            var buttons = new List<Button>();
            foreach (string field in fields)
            {
                var button = Field<Button>(screen, field);
                if (button && !buttons.Contains(button)) buttons.Add(button);
            }
            if (buttons.Count == 0) return;
            // UIGameMenu can be a controller ABOVE its Canvas. Locate the real
            // visual parent through the native controls instead of its own rect.
            var canvas = buttons[0].GetComponentInParent<Canvas>();
            var visualRoot = canvas ? canvas.transform as RectTransform : null;
            if (!visualRoot) return;
            if (!pauseDocksV8.TryGetValue(screen, out var dock) || !dock)
            {
                // Retire only the old menu's compact brush plates. Fullscreen
                // dimming and each native button's icon/label remain separate.
                var oldColumn = CommonMenuParentV7(buttons, screen.transform);
                if (oldColumn)
                {
                    FreezeMenuLayoutV8(oldColumn);
                    var oldBackings = new HashSet<Image>();
                    foreach (var button in buttons)
                        for (var ancestor = button.transform.parent; ancestor; ancestor = ancestor.parent)
                        {
                            var image = ancestor.GetComponent<Image>();
                            if (image) oldBackings.Add(image);
                            if (ancestor == oldColumn) break;
                        }
                    foreach (var image in oldBackings)
                    {
                        if (!image || IsOurObject(image.transform) || image.GetComponentInParent<Button>() ||
                            image.type == Image.Type.Filled || image.GetComponent<Mask>()) continue;
                        var rect = image.rectTransform.rect;
                        if (rect.width >= 220 && rect.width <= 900 && rect.height >= 200)
                            ImageStyle(image, Color.clear);
                    }
                }
                dock = NewContainer(visualRoot, "pause dock");
                dock.anchorMin = dock.anchorMax = new Vector2(.5f, .5f);
                dock.pivot = new Vector2(.5f, .5f); dock.anchoredPosition = Vector2.zero;
                DecorateSurface(dock, true);
                MenuHeadingV7(dock, buttons[0].GetComponentInChildren<TMP_Text>(true), "PAUSED", 25);
                var visibility = screen.gameObject.AddComponent<ReefNativeScreenLinkV8>();
                visibility.Initialize(dock);
                AddVisibilityUndo(screen, () => { if (visibility) Destroy(visibility); });
                pauseDocksV8[screen] = dock;
            }
            var visible = new List<Button>();
            foreach (var button in buttons) if (button.gameObject.activeSelf) visible.Add(button);
            const float width = 394, rowHeight = 52, gap = 8, header = 62, bottom = 22;
            float height = header + bottom + visible.Count * rowHeight + Mathf.Max(0, visible.Count - 1) * gap;
            dock.sizeDelta = new Vector2(width, height);
            float maxHeight = visualRoot.rect.height * .91f;
            float scale = maxHeight > 0 ? Mathf.Min(1, maxHeight / height) : 1;
            dock.localScale = new Vector3(scale, scale, 1);
            for (int i = 0; i < visible.Count; i++)
            {
                var button = visible[i];
                var rect = button.transform as RectTransform;
                SaveRect(rect); FreezeMenuLayoutV8(rect);
                if (rect.parent != dock) rect.SetParent(dock, false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
                rect.pivot = new Vector2(.5f, 1);
                rect.sizeDelta = new Vector2(width - 40, rowHeight);
                rect.anchoredPosition = new Vector2(0, -header - i * (rowHeight + gap));
                rect.localScale = Vector3.one;
                bool primary = button == Field<Button>(screen, "btnResumeGame");
                DecorateButton(button, primary);
                FitMenuButtonTextV8(button, primary);
                SyncDecoratedSurface(rect);
            }
            SyncDecoratedSurface(dock);
            bool visibleNow = screen.isActiveAndEnabled;
            if (dock.gameObject.activeSelf != visibleNow) dock.gameObject.SetActive(visibleNow);
            dock.SetAsLastSibling();
        }

        internal void HidePauseDockV8(MonoBehaviour screen)
        {
            if (screen && pauseDocksV8.TryGetValue(screen, out var dock) && dock)
                dock.gameObject.SetActive(false);
        }

        void FreezeMenuLayoutV8(RectTransform rect)
        {
            if (!rect) return;
            foreach (var driver in rect.GetComponents<Behaviour>())
            {
                if (!(driver is LayoutGroup) && !(driver is ContentSizeFitter) && !(driver is AspectRatioFitter)) continue;
                if (!layoutDrivers.ContainsKey(driver)) layoutDrivers[driver] = driver.enabled;
                driver.enabled = false;
            }
        }

        void FitMenuButtonTextV8(Button button, bool primary = false)
        {
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (IsOurObject(label.transform)) continue;
                TextColor(label, primary ? Gold : White, true);
                label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 23;
                label.enableWordWrapping = false; label.alignment = TextAlignmentOptions.Center;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.margin = new Vector4(18, 4, 18, 4);
                if (label.transform != button.transform)
                {
                    var rect = label.rectTransform; SaveRect(rect); FreezeMenuLayoutV8(rect);
                    if (rect.parent != button.transform) rect.SetParent(button.transform, false);
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = new Vector2(15, 4); rect.offsetMax = new Vector2(-15, -4);
                }
            }
            RegisterButtonLabels(button, primary);
            UpdateButtonLabels(button);
        }

        void StyleSettingsV7(MonoBehaviour screen)
        {
            var root = screen.transform as RectTransform;
            if (root) DecorateSurface(root, true);
            foreach (string field in new[] { "panelGeneral", "panelKeybinding", "panelGraphics", "panelAudio", "panelControllers" })
            {
                var panel = ScreenFieldRect(screen, field);
                if (panel) DecorateSurface(panel);
            }
            foreach (string field in new[] { "togglePanelGeneral", "togglePanelKeybinding", "togglePanelGraphics", "togglePanelAudio", "togglePanelControllers" })
                StyleMenuTabV7(Field<Toggle>(screen, field));
            MenuButtonV7(Field<Button>(screen, "btnClose"));
            MenuButtonV7(Field<Button>(screen, "btnResetToDefaultSettings"));
            foreach (var slider in screen.GetComponentsInChildren<Slider>(true)) StyleMenuSliderV7(slider);
            foreach (var text in screen.GetComponentsInChildren<TMP_Text>(true))
            {
                if (IsOurObject(text.transform) || text.GetComponentInParent<Selectable>()) continue;
                string n = text.name.ToLowerInvariant();
                // Section headings gain a warm hierarchy; values and labels
                // remain neutral and their native localization stays intact.
                if (text.fontSize >= 27 && (n.Contains("title") || n.Contains("header") || text.rectTransform.rect.height < 65))
                    TextColor(text, new Color(.82f, .76f, .64f, 1));
            }
        }

        void StyleMenuTabV7(Toggle toggle, RectTransform backing = null, TMP_Text nativeLabel = null, bool createSurface = true)
        {
            if (!toggle || menuTabsV7.ContainsKey(toggle)) return;
            var surface = createSurface ? DecorateSurface(backing ? backing : toggle.transform as RectTransform) : null;
            if (surface)
            {
                var oldTarget = toggle.targetGraphic;
                var oldTransition = toggle.transition;
                var oldColors = toggle.colors;
                AddVisibilityUndo(toggle, () => { if (toggle) { toggle.targetGraphic = oldTarget; toggle.transition = oldTransition; toggle.colors = oldColors; } });
                toggle.targetGraphic = surface;
                toggle.transition = Selectable.Transition.ColorTint;
                var colors = toggle.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.13f, 1.12f, 1.08f, 1);
                colors.pressedColor = new Color(.9f, .85f, .74f, 1);
                colors.selectedColor = Color.white;
                colors.disabledColor = new Color(.55f, .55f, .55f, .7f);
                colors.fadeDuration = .12f;
                toggle.colors = colors;
            }
            var state = new MenuTabV7 { Toggle = toggle, Surface = surface,
                Labels = nativeLabel ? new[] { nativeLabel } : toggle.GetComponentsInChildren<TMP_Text>(true),
                LegacyLabels = nativeLabel ? new Text[0] : toggle.GetComponentsInChildren<Text>(true) };
            var ruleRoot = NewContainer(toggle.transform as RectTransform, "tab accent");
            ruleRoot.anchorMin = new Vector2(.14f, 0); ruleRoot.anchorMax = new Vector2(.86f, 0);
            ruleRoot.pivot = new Vector2(.5f, 0); ruleRoot.anchoredPosition = new Vector2(0, 1);
            ruleRoot.sizeDelta = new Vector2(0, 2);
            state.Rule = ruleRoot.gameObject.AddComponent<Image>();
            state.Rule.sprite = panelSprite; state.Rule.type = Image.Type.Sliced;
            state.Rule.color = Gold; state.Rule.raycastTarget = false;
            menuTabsV7[toggle] = state;
            UnityAction<bool> listener = selected => RefreshMenuTabV7(state);
            toggle.onValueChanged.AddListener(listener);
            var pointer = toggle.gameObject.AddComponent<ReefMenuTabPointerV7>();
            pointer.Hover = hovered => { state.Hovered = hovered; RefreshMenuTabV7(state); };
            AddVisibilityUndo(toggle, () => { if (toggle) toggle.onValueChanged.RemoveListener(listener); if (pointer) Destroy(pointer); });
            RefreshMenuTabV7(state);
        }

        void RefreshMenuTabV7(MenuTabV7 state)
        {
            if (!state.Toggle) return;
            bool selected = state.Toggle.isOn;
            var tint = selected || state.Hovered ? Gold : White;
            if (state.Surface)
            {
                var materialTint = selected ? new Color(1.12f, 1.04f, .86f, .98f) : new Color(.94f, .98f, 1, .96f);
                if (state.Surface.color != materialTint) state.Surface.color = materialTint;
            }
            foreach (var text in state.Labels) if (text) TextColor(text, tint);
            foreach (var text in state.LegacyLabels) if (text) LegacyColor(text, tint);
            if (state.Rule && state.Rule.enabled != selected) state.Rule.enabled = selected;
        }

        void StyleMenuSliderV7(Slider slider)
        {
            if (!slider) return;
            var track = slider.transform.Find("Background") as RectTransform;
            if (track)
            {
                ImageStyle(track.GetComponent<Image>(), new Color(.12f, .15f, .17f, .95f));
                if (Mathf.Approximately(track.anchorMin.y, track.anchorMax.y))
                { SaveRect(track); track.sizeDelta = new Vector2(track.sizeDelta.x, 6); }
            }
            // Filled tracks keep their native fill type, amount and hit testing.
            if (slider.fillRect) TintImage(slider.fillRect.GetComponent<Image>(), new Color(.72f, .57f, .32f, 1));
            if (slider.handleRect) ImageStyle(slider.handleRect.GetComponent<Image>(), new Color(.93f, .84f, .65f, 1));
        }

        void StyleCharacterWorldV7(MonoBehaviour screen, string type)
        {
            var root = screen.transform as RectTransform;
            var frame = screen.transform.Find("ReefUI character frame") as RectTransform;
            var content = ScreenFieldRect(screen, "contentRoot");
            if (!frame && content) frame = content.Find("ReefUI character frame") as RectTransform;
            if (frame)
            {
                // The frame is a decoration. Extend it to make room for the
                // heading while leaving every native control in its old place.
                SaveRect(frame);
                frame.sizeDelta += new Vector2(0, 46);
                frame.anchoredPosition += new Vector2(0, 23);
                DecorateSurface(frame, true);
                MenuHeadingV7(frame, screen.GetComponentInChildren<TMP_Text>(true),
                    type == "UICharacterCreation" ? "CREATE CHARACTER" : "SELECT CHARACTER", 24);
            }
            else if (content && content.rect.width > 200 && content.rect.width < 850)
                DecorateSurface(content, true);
            else if (root && root.rect.width > 200 && root.rect.width < 850)
                DecorateSurface(root, true);
            foreach (var button in screen.GetComponentsInChildren<Button>(true))
            {
                if (IsOurObject(button.transform)) continue;
                bool primary = button == Field<Button>(screen, "btnSinglePlayer") || button == Field<Button>(screen, "btnDone") ||
                    button == Field<Button>(screen, "btnStartGame");
                MenuButtonV7(button, primary);
            }
            foreach (var slider in screen.GetComponentsInChildren<Slider>(true)) StyleMenuSliderV7(slider);
            // Lists are owned by the game: texture their backing and entry
            // buttons, without inserting decorative children into list holders.
            var list = ScreenFieldRect(screen, "nameIconGroup");
            if (list && list.GetComponent<Image>()) DecorateSurface(list);
        }

        void StyleEntryV7(MonoBehaviour entry)
        {
            var button = entry.GetComponent<Button>() ?? entry.GetComponentInChildren<Button>(true);
            MenuButtonV7(button);
            // Entry selection is represented by the game's interactable state;
            // the shared button feedback keeps the selected entry name gold.
        }

        void StyleLoadingV7(MonoBehaviour screen)
        {
            foreach (var pair in loadingBackdrops)
            {
                if (!pair.Key || !pair.Value || !pair.Key.IsChildOf(screen.transform)) continue;
                DecorateSurface(pair.Value);
                var label = pair.Key.GetComponent<TMP_Text>();
                if (!label) continue;
                bool heading = label.text.TrimStart().StartsWith("Loading", StringComparison.OrdinalIgnoreCase);
                TextColor(label, heading ? Gold : White);
                if (heading) { label.fontStyle |= FontStyles.Bold; label.characterSpacing = 1.4f; }
                else { label.enableWordWrapping = true; label.margin = new Vector4(12, 3, 12, 3); }
            }
        }

        RectTransform CommonMenuParentV7(List<Button> buttons, Transform fallback)
        {
            if (buttons.Count == 0) return fallback as RectTransform;
            for (var parent = buttons[0].transform.parent; parent; parent = parent.parent)
            {
                bool containsAll = true;
                foreach (var button in buttons)
                    if (!button.transform.IsChildOf(parent)) { containsAll = false; break; }
                if (containsAll) return parent as RectTransform;
                if (parent == fallback) break;
            }
            return fallback as RectTransform;
        }

        static bool TryMenuBoundsV7(RectTransform parent, List<Button> buttons, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            var corners = new Vector3[4];
            foreach (var button in buttons)
            {
                if (!(button.transform is RectTransform rect) || !button.gameObject.activeSelf) continue;
                rect.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = parent.InverseTransformPoint(corner);
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found && bounds.size.x > 100 && bounds.size.y > 100 && bounds.size.x < 900 && bounds.size.y < 1600;
        }

        void MenuHeadingV7(RectTransform frame, TMP_Text source, string heading, float size)
        {
            if (!frame || !source) return;
            var holder = NewContainer(frame, "menu heading");
            holder.anchorMin = new Vector2(0, 1); holder.anchorMax = new Vector2(1, 1);
            holder.pivot = new Vector2(.5f, 1); holder.anchoredPosition = new Vector2(0, -12);
            holder.sizeDelta = new Vector2(-38, 30);
            var label = holder.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = source.font; label.text = heading; label.fontSize = size;
            label.fontStyle = FontStyles.Bold; label.characterSpacing = 3;
            label.color = new Color(.82f, .76f, .64f, 1); label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false; label.enableAutoSizing = true;
            label.fontSizeMin = 14; label.fontSizeMax = size;
            label.raycastTarget = false;
        }

        internal void PruneMenusV7()
        {
            menuScreensV7.RemoveWhere(screen => !screen);
            menuButtonsV7.RemoveWhere(button => !button);
            var dead = new List<Toggle>();
            foreach (var pair in menuTabsV7) if (!pair.Key) dead.Add(pair.Key);
            foreach (var toggle in dead) menuTabsV7.Remove(toggle);
            var deadPauses = new List<MonoBehaviour>();
            foreach (var pair in pauseDocksV8) if (!pair.Key || !pair.Value) deadPauses.Add(pair.Key);
            foreach (var pause in deadPauses) pauseDocksV8.Remove(pause);
        }

        internal void ResetMenusV7()
        {
            menuScreensV7.Clear(); menuButtonsV7.Clear(); menuTabsV7.Clear(); pauseDocksV8.Clear();
        }
    }

    // Pointer events drive a small cached tab treatment. There is no Update,
    // coroutine, screen discovery or material allocation in pointer feedback.
    public sealed class ReefMenuTabPointerV7 : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Action<bool> Hover;
        bool pointerInside,focused;
        void OnEnable() { pointerInside=focused=false;Notify(); }
        public void OnPointerEnter(PointerEventData eventData) { pointerInside=true;Notify(); }
        public void OnPointerExit(PointerEventData eventData) { pointerInside=false;Notify(); }
        public void OnSelect(BaseEventData eventData) { focused=true;Notify(); }
        public void OnDeselect(BaseEventData eventData) { focused=false;Notify(); }
        void OnDisable() { pointerInside=focused=false;Notify(); }
        void Notify() { Hover?.Invoke(pointerInside||focused); }
    }

    // Screen controllers may live above their visual Canvas. The one authored
    // panel follows the native controller's lifetime without a frame callback.
    public sealed class ReefNativeScreenLinkV8 : MonoBehaviour
    {
        RectTransform target;
        public void Initialize(RectTransform panel) { target = panel; Apply(isActiveAndEnabled); }
        void OnEnable() { Apply(true); }
        void OnDisable() { Apply(false); }
        void OnDestroy() { if (target) Destroy(target.gameObject); }
        void Apply(bool visible) { if (target && target.gameObject.activeSelf != visible) target.gameObject.SetActive(visible); }
    }
}
