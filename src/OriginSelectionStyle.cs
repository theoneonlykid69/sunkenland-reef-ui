using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        sealed class OriginSelectionV91
        {
            public RectTransform Panel, Choices, Details, Summary;
            public TMP_Text Title, Description, Page, DetailTitle, SummaryTitle;
            public Button Back, Confirm;
            public readonly List<Toggle> Toggles = new List<Toggle>();
            public readonly List<TMP_Text> Labels = new List<TMP_Text>();
            public readonly List<TMP_Text> SummaryCaptions = new List<TMP_Text>();
            public readonly List<TMP_Text> SummaryValues = new List<TMP_Text>();
        }

        readonly Dictionary<MonoBehaviour, OriginSelectionV91> originSelectionsV91 = new Dictionary<MonoBehaviour, OriginSelectionV91>();
        readonly Dictionary<Type, MethodInfo> originLocalizationMethodsV91 = new Dictionary<Type, MethodInfo>();

        internal void StyleOriginSelectionV91(MonoBehaviour screen)
        {
            if (!screen || screen.GetType().Name != "UICharacterWorldCustomization") return;
            if (!originSelectionsV91.TryGetValue(screen, out var state) || !state.Panel)
            {
                state = CreateOriginSelectionV91(screen);
                if (state == null) return;
                originSelectionsV91[screen] = state;
            }

            int step = 0;
            var nativeStep = Field<object>(screen, "_currentSelectionType");
            if (nativeStep != null) step = Convert.ToInt32(nativeStep);
            string listField = step == 1 ? "characterHobbies" : step == 2 ? "characterExtraStartingItems" : "characterOrigins";
            var entries = Field<IList>(screen, listField);
            int count = Mathf.Min(entries == null ? 0 : entries.Count, state.Toggles.Count);
            if (count == 0) return;

            const float width = 1320, left = 36, choicesWidth = 640, detailsLeft = 700, detailsWidth = 584;
            const float rowHeight = 74, gap = 12, header = 106, footer = 96;
            float choicesHeight = count * rowHeight + Mathf.Max(0, count - 1) * gap;
            float contentHeight = Mathf.Max(590, choicesHeight);
            float height = header + contentHeight + footer;

            SaveRect(state.Panel); FreezeMenuLayoutV8(state.Panel);
            state.Panel.anchorMin = state.Panel.anchorMax = new Vector2(.5f, .5f);
            state.Panel.pivot = new Vector2(.5f, .5f); state.Panel.anchoredPosition = Vector2.zero;
            state.Panel.sizeDelta = new Vector2(width, height);
            var parent = state.Panel.parent as RectTransform;
            float scale = parent && parent.rect.width > 0 && parent.rect.height > 0
                ? Mathf.Min(1, Mathf.Min(parent.rect.width * .94f / width, parent.rect.height * .92f / height)) : 1;
            state.Panel.localScale = new Vector3(scale, scale, 1);
            state.Panel.SetAsLastSibling();

            OriginRectV91(state.Choices, state.Panel, left, -header, choicesWidth, choicesHeight);
            OriginRectV91(state.Details, state.Panel, detailsLeft, -header, detailsWidth, contentHeight);
            OriginTextV91(state.Title, state.Panel, left, -28, width - left * 2, 50, 32, Gold, TextAlignmentOptions.MidlineLeft);
            OriginTextV91(state.DetailTitle, state.Details, 26, -23, detailsWidth - 52, 44, 27, Gold, TextAlignmentOptions.MidlineLeft);
            OriginTextV91(state.Description, state.Details, 26, -86, detailsWidth - 52, contentHeight - 302, 21, White, TextAlignmentOptions.TopLeft);
            state.Description.enableAutoSizing = false; state.Description.fontSize = 21;
            state.Description.enableWordWrapping = true; state.Description.lineSpacing = 6;
            state.Description.overflowMode = TextOverflowModes.Overflow;

            string selectedTitle = "", selectedDescription = "";
            for (int i = 0; i < state.Toggles.Count; i++)
            {
                var toggle = state.Toggles[i];
                if (!toggle) continue;
                OriginRectV91(toggle.transform as RectTransform, state.Choices, 0, -i * (rowHeight + gap), choicesWidth, rowHeight);
                var label = state.Labels[i];
                OriginTextV91(label, toggle.transform as RectTransform, 22, -12, choicesWidth - 44, rowHeight - 24, 23,
                    toggle.isOn ? Gold : White, TextAlignmentOptions.MidlineLeft);
                label.fontStyle |= FontStyles.Bold;
                label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
                // The native list and index select the description. No names,
                // guessed perks, percentages, or repacked localization tables.
                string description = i < count ? OriginLocalizedTextV91(entries[i], "localizedStringDescription") : "";
                if (i < count && toggle.isOn)
                { selectedTitle = label.text; selectedDescription = description; }
                if (menuTabsV7.TryGetValue(toggle, out var tab)) RefreshMenuTabV7(tab);
                SyncDecoratedSurface(toggle.transform as RectTransform);
            }
            if (state.DetailTitle.text != selectedTitle) state.DetailTitle.text = selectedTitle;
            // Keep the right presenter synchronized, including an unselected
            // new step where the native false-toggle callback leaves old text.
            if (state.Description.text != selectedDescription) state.Description.text = selectedDescription;

            OriginTextV91(state.SummaryTitle, state.Details, 26, -contentHeight + 196, detailsWidth - 52, 30, 18,
                new Color(.73f, .69f, .60f, 1), TextAlignmentOptions.MidlineLeft);
            if (state.Summary)
            {
                OriginRectV91(state.Summary, state.Details, 26, -contentHeight + 150, detailsWidth - 52, 126);
                StyleOriginSummaryV91(screen, state);
            }

            OriginFooterButtonV91(state.Back, state.Panel, left, -height + 72, 240, false);
            OriginFooterButtonV91(state.Confirm, state.Panel, width - left - 240, -height + 72, 240, true);
            OriginTextV91(state.Page, state.Panel, width * .5f - 90, -height + 72, 180, 54, 21,
                new Color(.73f, .77f, .79f, 1), TextAlignmentOptions.Center);
            SyncDecoratedSurface(state.Panel); SyncDecoratedSurface(state.Details);
        }

        OriginSelectionV91 CreateOriginSelectionV91(MonoBehaviour screen)
        {
            var panel = ScreenFieldRect(screen, "panelSelectionRootRectTransform");
            var choices = ScreenFieldRect(screen, "togglesRoot");
            var title = Field<TMP_Text>(screen, "tmpSelectionTitle");
            var description = Field<TMP_Text>(screen, "tmpSelectionDescription");
            var toggles = Field<IList>(screen, "optionToggles");
            var back = Field<Button>(screen, "btnBack"); var confirm = Field<Button>(screen, "btnConfirm");
            var page = Field<TMP_Text>(screen, "tmpPageNumber");
            if (!panel || !choices || !title || !description || toggles == null || toggles.Count == 0 || !back || !confirm || !page) return null;
            // Refuse a partial mapping instead of skipping an invalid row and
            // shifting all following display indices away from the native list.
            foreach (var item in toggles)
            {
                var toggle = item as Toggle;
                if (!toggle || !(Field<TMP_Text>(toggle, "tmpLabel") ?? toggle.GetComponentInChildren<TMP_Text>(true))) return null;
            }

            var state = new OriginSelectionV91 { Panel = panel, Choices = choices, Title = title,
                Back = back, Confirm = confirm, Page = page };
            // Keep all decoration inside the native selection panel. Its own
            // visibility and the native controller's Close both remain valid.
            FreezeMenuLayoutV8(panel); FreezeMenuLayoutV8(choices);
            var titleBacking = title.transform.parent.GetComponent<Image>();
            if (titleBacking && titleBacking.transform != panel) TintImage(titleBacking, Color.clear);
            foreach (var button in new[] { back, confirm })
                if (button.transform.parent != panel) FreezeMenuLayoutV8(button.transform.parent as RectTransform);
            DecorateSurface(panel, true);
            state.Details = NewContainer(panel, "origin benefits panel");
            DecorateSurface(state.Details);
            state.DetailTitle = NewOriginTextV91(state.Details, title, "selected origin name", "");
            // Native descriptions and summary labels can retain hidden renderer
            // state. Keep their data bindings, but render one visible text view.
            HideOriginNativeTextV91(description);
            state.Description = NewOriginTextV91(state.Details, title, "selected origin benefits", "");
            state.SummaryTitle = NewOriginTextV91(state.Details, title, "origin summary title", "YOUR CHOICES");
            state.Summary = NewContainer(state.Details, "origin choices summary");
            foreach (string field in new[] { "tmpSelectedOrigin", "tmpSelectedHobby", "tmpSelectedExtraStartingItems" })
            {
                var nativeValue = Field<TMP_Text>(screen, field);
                HideOriginNativeTextV91(nativeValue);
                if (nativeValue && nativeValue.transform.parent)
                    foreach (Transform child in nativeValue.transform.parent)
                    { var caption = child.GetComponent<TMP_Text>(); if (caption && caption != nativeValue) HideOriginNativeTextV91(caption); }
                state.SummaryCaptions.Add(NewOriginTextV91(state.Summary, title, "choice caption", ""));
                state.SummaryValues.Add(NewOriginTextV91(state.Summary, title, "choice value", ""));
            }

            foreach (var item in toggles)
            {
                var toggle = item as Toggle;
                var label = Field<TMP_Text>(toggle, "tmpLabel") ?? toggle.GetComponentInChildren<TMP_Text>(true);
                SaveRect(toggle.transform as RectTransform); FreezeMenuLayoutV8(toggle.transform as RectTransform);
                // ToggleWithText hides its stock background when selected.
                // A child material is stable in both states and adds no sibling
                // to the native indexed choice holder.
                foreach (Transform child in toggle.transform)
                {
                    var image = child.GetComponent<Image>();
                    if (image && !IsOurObject(child) && image.type != Image.Type.Filled && !image.GetComponent<Mask>())
                        TintImage(image, Color.clear);
                }
                StyleMenuTabV7(toggle, toggle.transform as RectTransform, label);
                if (menuTabsV7.TryGetValue(toggle, out var tab) && tab.Surface) tab.Surface.raycastTarget = true;
                state.Toggles.Add(toggle); state.Labels.Add(label);
            }
            return state;
        }

        void HideOriginNativeTextV91(TMP_Text text)
        {
            if (!text) return;
            bool enabled = text.enabled;
            AddVisibilityUndo(text, () => { if (text) text.enabled = enabled; });
            text.enabled = false;
        }

        string OriginLocalizedTextV91(object entry, string field)
        {
            var localized = Field<object>(entry, field);
            if (localized == null) return "";
            var type = localized.GetType();
            if (!originLocalizationMethodsV91.TryGetValue(type, out var method))
            {
                method = type.GetMethod("GetLocalizedString", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                originLocalizationMethodsV91[type] = method;
            }
            return method == null ? "" : method.Invoke(localized, null) as string ?? "";
        }

        TMP_Text NewOriginTextV91(RectTransform parent, TMP_Text source, string name, string value)
        {
            var rect = NewContainer(parent, name);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = source.font; text.text = value; text.raycastTarget = false;
            text.color = White; return text;
        }

        void OriginRectV91(RectTransform rect, RectTransform parent, float x, float y, float width, float height)
        {
            if (!rect || !parent) return;
            SaveRect(rect); FreezeMenuLayoutV8(rect);
            if (rect.parent != parent) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height); rect.localScale = Vector3.one;
        }

        void OriginTextV91(TMP_Text text, RectTransform parent, float x, float y, float width, float height,
            float size, Color color, TextAlignmentOptions alignment)
        {
            if (!text) return;
            OriginRectV91(text.rectTransform, parent, x, y, width, height);
            TextColor(text, color); text.fontStyle &= ~FontStyles.UpperCase; text.characterSpacing = 0;
            text.alignment = alignment; text.margin = Vector4.zero;
            text.enableAutoSizing = true; text.fontSizeMin = Mathf.Max(15, size - 3); text.fontSizeMax = size;
            text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Ellipsis;
            SyncDecoratedSurface(text.rectTransform);
        }

        void OriginFooterButtonV91(Button button, RectTransform parent, float x, float y, float width, bool primary)
        {
            if (!button) return;
            OriginRectV91(button.transform as RectTransform, parent, x, y, width, 54);
            DecorateButton(button, primary); FitMenuButtonTextV8(button, primary);
            SyncDecoratedSurface(button.transform as RectTransform);
        }

        void StyleOriginSummaryV91(MonoBehaviour screen, OriginSelectionV91 state)
        {
            string[] fields = { "tmpSelectedOrigin", "tmpSelectedHobby", "tmpSelectedExtraStartingItems" };
            // Stock caption text contains editor placeholders in the hidden
            // native summary. Use our own labels, retaining localized choices.
            string[] captions = { "Profession", "Hobby", "Starting items" };
            for (int i = 0; i < fields.Length; i++)
            {
                var nativeValue = Field<TMP_Text>(screen, fields[i]);
                var caption = state.SummaryCaptions[i]; var value = state.SummaryValues[i];
                string captionText = captions[i];
                string valueText = nativeValue ? nativeValue.text : "";
                if (caption.text != captionText) caption.text = captionText;
                if (value.text != valueText) value.text = valueText;
                OriginTextV91(caption, state.Summary, 0, -i * 42, 174, 40, 18, new Color(.70f, .74f, .76f, 1), TextAlignmentOptions.MidlineLeft);
                OriginTextV91(value, state.Summary, 182, -i * 42, state.Summary.rect.width - 182, 40, 19, Gold, TextAlignmentOptions.MidlineLeft);
                value.enableWordWrapping = false;
            }
        }

        internal void PruneOriginSelectionV91()
        {
            var dead = new List<MonoBehaviour>();
            foreach (var pair in originSelectionsV91) if (!pair.Key || !pair.Value.Panel) dead.Add(pair.Key);
            foreach (var screen in dead) originSelectionsV91.Remove(screen);
        }

        internal void ResetOriginSelectionV91()
        { originSelectionsV91.Clear(); originLocalizationMethodsV91.Clear(); }
    }
}
