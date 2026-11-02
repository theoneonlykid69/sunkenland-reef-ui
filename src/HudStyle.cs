using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        // Only creation/open/quest-init events reach this file. The game keeps
        // ownership of bar values, radiation geometry, warning animations and text.
        readonly HashSet<MonoBehaviour> hudV7Styled = new HashSet<MonoBehaviour>();
        readonly Dictionary<HudGlyphKind, Sprite> hudV7Glyphs = new Dictionary<HudGlyphKind, Sprite>();
        sealed class QuestLayoutStampV9
        {
            public string Text;
            public TMP_FontAsset Font;
            public FontStyles Style;
            public bool Icon, Clear;
            public Vector2 RowSize;
            public float CardWidth;
        }
        readonly Dictionary<MonoBehaviour, QuestLayoutStampV9> questLayoutsV9 = new Dictionary<MonoBehaviour, QuestLayoutStampV9>();
        Sprite hudBarSprite;
        enum HudGlyphKind { Food, Water, Health, Energy, Objective, Close, Help, Sort }

        internal void StyleHudV7(MonoBehaviour screen)
        {
            if (!screen || IsOurObject(screen.transform)) return;
            string type = screen.GetType().Name;
            if (type == "CurrentTrackingQuestLabel") { StyleTrackedObjectiveV7(screen); return; }
            if (type != "UICombat" && type != "UICompass") return;
            if (!hudV7Styled.Add(screen)) return;
            if (type == "UICompass")
            {
                var compass = screen.transform as RectTransform;
                if (compass && compass.rect.width < 1300 && compass.rect.height < 180)
                    DecorateSurface(compass);
                HudTextV7(Field<TMP_Text>(screen, "tmpHeadingDirection"), Gold);
                return;
            }

            var health = Field<Image>(screen, "healthBar");
            var hunger = Field<Image>(screen, "hungerBar");
            var thirst = Field<Image>(screen, "thirstBar");
            var stamina = Field<Image>(screen, "staminaBar");
            var energy = Field<Image>(screen, "energyBar");
            var group = ScreenFieldRect(screen, "PlayerStateGroup");
            var bounds = new List<RectTransform>();
            foreach (var bar in new[] { health, hunger, thirst, stamina, energy })
            {
                if (!bar) continue;
                StyleHudBarV7(bar);
                if (group && bar.transform.IsChildOf(group)) bounds.Add(bar.rectTransform);
            }
            StyleHudBarV7(Field<Image>(screen, "targetHealthBar"));
            var healthTrack = ScreenFieldRect(screen, "healthBarBG");
            if (healthTrack) StyleHudTrackV7(healthTrack.GetComponent<Image>());
            ReplacePanelGlyphV7(hunger ? hunger.transform.parent : null, "DECOR", HudGlyphKind.Food, Gold, bounds);
            ReplacePanelGlyphV7(thirst ? thirst.transform.parent : null, "DECOR", HudGlyphKind.Water,
                new Color(.63f, .82f, .84f, 1), bounds);
            // The normal-health bar is nested inside the radiation-aware track.
            // Reach its known outer panel without changing either health region.
            var healthPanel = healthTrack ? healthTrack.parent : null;
            ReplacePanelGlyphV7(healthPanel, "DECOR_1", HudGlyphKind.Health, White, bounds);
            var energyPanel = energy ? energy.transform.parent : (stamina ? stamina.transform.parent : null);
            ReplacePanelGlyphV7(energyPanel, "DECOR_2", HudGlyphKind.Energy,
                new Color(.69f, .78f, .87f, 1), bounds);
            if (!LayoutNativeVitalsV8(group, hunger, thirst, healthTrack, stamina, energy, screen))
                AddHudVitalsBackdropV7(group, bounds);

            var time = ScreenFieldRect(screen, "TimeGroup");
            if (time && time.rect.width < 700 && time.rect.height < 140)
            {
                DecorateSurface(time, true);
                var layout = time.GetComponent<HorizontalLayoutGroup>();
                if (layout)
                {
                    var padding = layout.padding; float spacing = layout.spacing;
                    var alignment = layout.childAlignment;
                    AddVisibilityUndo(layout, () => { if (layout) { layout.padding = padding; layout.spacing = spacing; layout.childAlignment = alignment; } });
                    layout.padding = new RectOffset(14, 14, 3, 3);
                    layout.spacing = 8; layout.childAlignment = TextAnchor.MiddleLeft;
                }
            }
            HudTextV7(Field<TMP_Text>(screen, "tmpDays"), Gold);
            HudTextV7(Field<TMP_Text>(screen, "tmpTimer"), White);
            HudTextV7(Field<TMP_Text>(screen, "tmpHour"), White);
            StyleTrackedObjectiveV7(Field<MonoBehaviour>(screen, "currentTrackingQuestLabel"));

            // Notification slots already exist in this prefab; styling them once
            // does not affect the game's queue, fade timing or completion sounds.
            var notifications = Field<System.Collections.IEnumerable>(screen, "questObjectiveNotificationTexts");
            if (notifications != null)
                foreach (var entry in notifications)
                {
                    var text = entry as TMP_Text;
                    if (!text) continue;
                    HudTextV7(text, White);
                    var parent = text.transform.parent as RectTransform;
                    if (parent && parent.rect.width < 850 && parent.rect.height < 180)
                        DecorateSurface(parent);
                }
        }

        void StyleHudBarV7(Image bar)
        {
            if (!bar) return;
            var sprite = bar.sprite; var overrides = bar.overrideSprite;
            bool aspect = bar.preserveAspect;
            AddVisibilityUndo(bar, () => { if (bar) { bar.sprite = sprite; bar.overrideSprite = overrides; bar.preserveAspect = aspect; } });
            // Preserve Image.Type/Filled method/origin and semantic color. A new
            // sprite gives the native fill a clean edge without a second meter.
            bar.overrideSprite = null; bar.sprite = HudBarSpriteV8(); bar.preserveAspect = false;
            DisableHudShadowV7(bar);
            var parent = bar.transform.parent;
            var track = parent ? parent.Find("bg") : null;
            if (track) StyleHudTrackV7(track.GetComponent<Image>());
        }

        void StyleHudTrackV7(Image track)
        {
            if (!track || track.type == Image.Type.Filled) return;
            ImageStyle(track, new Color(.02f, .035f, .045f, .94f));
            DisableHudShadowV7(track);
        }

        void ReplacePanelGlyphV7(Transform panel, string exactName, HudGlyphKind kind, Color color,
            List<RectTransform> bounds)
        {
            if (!panel) return;
            // These exact direct-child names come from the retained game UI dump.
            // Never select an arbitrary Image: warning images are in the same row.
            foreach (Transform child in panel)
            {
                if (child.name != exactName) continue;
                var icon = child.GetComponent<Image>();
                if (!icon) continue;
                ReplaceHudGlyphV7(icon, kind, color);
                var rect = icon.rectTransform;
                SaveRect(rect);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 30);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 30);
                if (bounds != null) bounds.Add(rect);
            }
        }

        void ReplaceHudGlyphV7(Image image, HudGlyphKind kind, Color color)
        {
            var sprite = image.sprite; var overrides = image.overrideSprite; var oldColor = image.color;
            var type = image.type; bool aspect = image.preserveAspect;
            AddVisibilityUndo(image, () => {
                if (image) { image.sprite = sprite; image.overrideSprite = overrides; image.color = oldColor; image.type = type; image.preserveAspect = aspect; }
            });
            image.overrideSprite = null; image.sprite = HudGlyphSpriteV7(kind);
            image.type = Image.Type.Simple; image.preserveAspect = true;
            color.a = oldColor.a; image.color = color;
            DisableHudShadowV7(image);
        }

        void DisableHudShadowV7(Graphic graphic)
        {
            foreach (var shadow in graphic.GetComponents<Shadow>())
            {
                bool enabled = shadow.enabled;
                AddVisibilityUndo(shadow, () => { if (shadow) shadow.enabled = enabled; });
                shadow.enabled = false;
            }
        }

        bool LayoutNativeVitalsV8(RectTransform group, Image hunger, Image thirst, RectTransform healthTrack,
            Image stamina, Image energy, MonoBehaviour combat)
        {
            if (!group || !hunger || !thirst || !healthTrack || !stamina || !energy ||
                group.rect.width > 800 || group.rect.height > 400) return false;
            var foodRow = hunger.transform.parent as RectTransform;
            var waterRow = thirst.transform.parent as RectTransform;
            var healthRow = healthTrack.parent as RectTransform;
            var energyRow = energy.transform.parent as RectTransform;
            if (!foodRow || !waterRow || !healthRow || !energyRow || stamina.transform.parent != energyRow ||
                foodRow.parent != group || waterRow.parent != group ||
                healthRow.parent != group || energyRow.parent != group) return false;

            // These four native rows are not layout-managed. Move their parents,
            // preserving health's nested normal/radiation regions and native
            // width recalculation (MaxHealth * 2 in UICombat.UpdateLogic).
            SaveRect(group);
            group.anchorMin = group.anchorMax = Vector2.zero; group.pivot = Vector2.zero;
            var canvas = group.GetComponentInParent<Canvas>();
            float inset = canvas ? 20 / Mathf.Max(.01f, canvas.rootCanvas.scaleFactor) : 20;
            group.anchoredPosition = new Vector2(inset, inset); group.sizeDelta = new Vector2(390, 140);
            LayoutSmallVitalV8(foodRow, hunger, 0, 111, combat, "lowHungerWarning");
            LayoutSmallVitalV8(waterRow, thirst, 188, 111, combat, "lowThirstWarning");

            SaveRect(healthTrack);
            healthTrack.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 18);
            MoveVitalRowV8(healthRow, healthTrack, group, new Vector2(56, 75));
            PlaceVitalGlyphsV8(healthRow, "DECOR_1", group, new Vector2(32, 75));

            // Stamina and energy are separate values, already separate Images.
            // Keep both native fill updates and give the tracks an eight-pixel gap.
            SaveRect(stamina.rectTransform); SaveRect(energy.rectTransform);
            stamina.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 8);
            energy.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 8);
            var pos = stamina.rectTransform.anchoredPosition; pos.y = 8; stamina.rectTransform.anchoredPosition = pos;
            pos = energy.rectTransform.anchoredPosition; pos.y = -8; energy.rectTransform.anchoredPosition = pos;
            MoveVitalRowV8(energyRow, stamina.rectTransform, group, new Vector2(56, 44));
            PlaceVitalGlyphsV8(energyRow, "DECOR_2", group, new Vector2(32, 36));
            var energyTrack = energyRow.Find("bg") as RectTransform;
            if (energyTrack)
            {
                SaveRect(energyTrack);
                energyTrack.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 30);
            }
            DecorateSurface(group, true);
            return true;
        }

        void LayoutSmallVitalV8(RectTransform row, Image bar, float column, float y,
            MonoBehaviour combat, string warningField)
        {
            SaveRect(row);
            row.anchorMin = row.anchorMax = Vector2.zero; row.pivot = new Vector2(0, .5f);
            row.anchoredPosition = new Vector2(column, y); row.localScale = Vector3.one;
            PlaceVitalRectV8(bar.rectTransform, new Vector2(56, 0), new Vector2(128, 14), false);
            var track = row.Find("bg") as RectTransform;
            if (track) PlaceVitalRectV8(track, new Vector2(55, 0), new Vector2(130, 18), false);
            var warning = ScreenFieldRect(combat, warningField);
            if (warning && warning.parent == row)
                PlaceVitalRectV8(warning, new Vector2(56, 0), new Vector2(128, 14), false);
            foreach (Transform child in row)
            {
                if (child.name == "DECOR")
                    PlaceVitalRectV8(child as RectTransform, new Vector2(32, 0), new Vector2(28, 28), true);
                else if (child.name == "decor") HideOldVitalDecorV8(child);
            }
        }

        void MoveVitalRowV8(RectTransform row, RectTransform reference, RectTransform group, Vector2 leftCenter)
        {
            SaveRect(row);
            row.anchorMin = row.anchorMax = Vector2.zero; row.pivot = new Vector2(0, .5f);
            row.localScale = Vector3.one;
            var corners = new Vector3[4]; reference.GetWorldCorners(corners);
            var left = group.InverseTransformPoint((corners[0] + corners[1]) * .5f);
            row.anchoredPosition += leftCenter - new Vector2(left.x, left.y);
        }

        void PlaceVitalGlyphsV8(RectTransform row, string exactName, RectTransform group, Vector2 center)
        {
            foreach (Transform child in row)
            {
                if (child.name == exactName && child is RectTransform rect)
                {
                    SaveRect(rect);
                    rect.anchorMin = rect.anchorMax = row.pivot; rect.pivot = new Vector2(.5f, .5f);
                    rect.sizeDelta = new Vector2(28, 28);
                    rect.anchoredPosition = row.InverseTransformPoint(group.TransformPoint(center));
                }
                else if (child.name == "decor" || child.name == "TXT DECOR") HideOldVitalDecorV8(child);
            }
        }

        void PlaceVitalRectV8(RectTransform rect, Vector2 position, Vector2 size, bool centered)
        {
            if (!rect) return;
            SaveRect(rect);
            rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
            rect.pivot = new Vector2(centered ? .5f : 0, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        void HideOldVitalDecorV8(Transform target)
        {
            var graphic = target.GetComponent<Graphic>();
            if (!graphic) return;
            bool enabled = graphic.enabled;
            AddVisibilityUndo(graphic, () => { if (graphic) graphic.enabled = enabled; });
            graphic.enabled = false;
        }

        void AddHudVitalsBackdropV7(RectTransform group, List<RectTransform> controls)
        {
            if (!group || controls.Count == 0) return;
            bool found = false; Bounds bounds = default;
            var corners = new Vector3[4];
            foreach (var control in controls)
            {
                if (!control || !control.IsChildOf(group)) continue;
                control.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var local = group.InverseTransformPoint(corner);
                    if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; }
                    else bounds.Encapsulate(local);
                }
            }
            if (!found || bounds.size.x > 700 || bounds.size.y > 240) return;
            var go = new GameObject("ReefUI vital instruments", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(group, false); rect.SetAsFirstSibling();
            rect.anchorMin = rect.anchorMax = group.pivot; rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = bounds.center;
            rect.sizeDelta = new Vector2(bounds.size.x + 30, bounds.size.y + 24);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.GetComponent<Image>().raycastTarget = false;
            decorations.Add(go);
            DecorateSurface(rect, true);
        }

        void StyleTrackedObjectiveV7(MonoBehaviour tracker)
        {
            if (!tracker) return;
            var label = Field<TMP_Text>(tracker, "tmpQuestName");
            if (!label) return;
            bool first = hudV7Styled.Add(tracker);
            var icon = Field<Image>(tracker, "imgQuestIcon");
            var clear = Field<Button>(tracker, "btnClearCurrentTrackingQuest");
            if (first)
            {
                if (icon) ReplaceHudGlyphV7(icon, HudGlyphKind.Objective, Gold);
                HudTextV7(label, White);
                var margin = label.margin; bool wrap = label.enableWordWrapping; bool auto = label.enableAutoSizing;
                var overflow = label.overflowMode; float fontSize = label.fontSize;
                AddVisibilityUndo(label, () => {
                    if (label) { label.margin = margin; label.enableWordWrapping = wrap; label.overflowMode = overflow; label.fontSize = fontSize; label.enableAutoSizing = auto; }
                });
                if (clear)
                {
                    var closeIcon = clear.GetComponent<Image>();
                    if (closeIcon) ReplaceHudGlyphV7(closeIcon, HudGlyphKind.Close, White);
                    var oldColors = clear.colors; var oldTransition = clear.transition;
                    AddVisibilityUndo(clear, () => { if (clear) { clear.colors = oldColors; clear.transition = oldTransition; } });
                    var colors = clear.colors;
                    colors.normalColor = White; colors.highlightedColor = Gold; colors.selectedColor = Gold;
                    colors.pressedColor = new Color(.7f, .55f, .32f, 1); colors.fadeDuration = .12f;
                    clear.colors = colors; clear.transition = Selectable.Transition.ColorTint;
                }
            }
            // Native Init changes text and completion strikethrough. Reapply only
            // cheap typography/layout properties after that event, with no new
            // sprites, surfaces, callbacks or undo records on subsequent calls.
            TextColor(label, White);
            if (label.margin != new Vector4(16, 10, 16, 10)) label.margin = new Vector4(16, 10, 16, 10);
            if (!label.enableWordWrapping) label.enableWordWrapping = true;
            if (label.overflowMode != TextOverflowModes.Ellipsis) label.overflowMode = TextOverflowModes.Ellipsis;
            if (label.enableAutoSizing) label.enableAutoSizing = false;
            if (!Mathf.Approximately(label.fontSize, 21)) label.fontSize = 21;
            if ((label.fontStyle & FontStyles.Bold) == 0) label.fontStyle |= FontStyles.Bold;

            var bg = label.transform.parent as RectTransform;
            var root = tracker.transform as RectTransform;
            bool iconShown = icon && icon.gameObject.activeSelf, clearShown = clear && clear.gameObject.activeSelf;
            if (!first && root && questLayoutsV9.TryGetValue(tracker, out var prior) && prior.Text == label.text &&
                prior.Font == label.font && prior.Style == label.fontStyle && prior.Icon == iconShown && prior.Clear == clearShown &&
                Mathf.Abs(root.rect.width - prior.RowSize.x) < .5f && Mathf.Abs(root.rect.height - prior.RowSize.y) < .5f &&
                Mathf.Abs(label.rectTransform.rect.width - prior.CardWidth) < .5f) return;
            float cardWidth = Mathf.Clamp(label.GetPreferredValues(label.text, float.PositiveInfinity, float.PositiveInfinity).x, 260, 380);
            float cardHeight = Mathf.Max(48, label.GetPreferredValues(label.text, cardWidth - 32, float.PositiveInfinity).y + 20);
            if (bg && (bg.name == "BG" || bg.GetComponent<Image>()))
            {
                if (first) DecorateSurface(bg, true);
                // The shipped text owns an empty VerticalLayoutGroup as well
                // as TMP's layout metrics. It must not compete with its BG.
                if (first) FreezeMenuLayoutV8(label.rectTransform);
                var layout = bg.GetComponent<VerticalLayoutGroup>();
                if (layout)
                {
                    if (first) SnapshotQuestLayoutV8(layout);
                    layout.padding = new RectOffset(0, 0, 0, 0);
                    layout.childControlWidth = true; layout.childControlHeight = true;
                    layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
                }
                else
                {
                    // Some shipped scene instances have the BG Image without a
                    // LayoutGroup. In those variants the text must explicitly
                    // occupy its new padded card instead of retaining 35px height.
                    var textRect = label.rectTransform;
                    SaveRect(textRect);
                    if (textRect.anchorMin != Vector2.zero) textRect.anchorMin = Vector2.zero;
                    if (textRect.anchorMax != Vector2.one) textRect.anchorMax = Vector2.one;
                    if (textRect.offsetMin != Vector2.zero) textRect.offsetMin = Vector2.zero;
                    if (textRect.offsetMax != Vector2.zero) textRect.offsetMax = Vector2.zero;
                }
                SetQuestFitterV8(bg, first, false);
                SetQuestFitterV8(label.rectTransform, first, false);
                FixedQuestSizeV9(bg, first, cardWidth, cardHeight);
                FixedQuestSizeV9(label.rectTransform, first, cardWidth, cardHeight);
                SaveRect(bg);
                bg.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cardWidth);
                bg.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cardHeight);
            }
            var row = tracker.GetComponent<HorizontalLayoutGroup>();
            if (row)
            {
                if (first) SnapshotQuestLayoutV8(row);
                row.spacing = 10; row.padding = new RectOffset(8, 12, 6, 6); row.childAlignment = TextAnchor.MiddleCenter;
                row.childControlWidth = true; row.childControlHeight = true;
                row.childForceExpandWidth = false; row.childForceExpandHeight = false;
            }
            foreach (var child in new Component[] { icon, clear })
            {
                if (!child) continue;
                float side = child == icon ? 24 : 28;
                FixedQuestSizeV9(child.transform as RectTransform, first, side, side);
            }
            if (root)
            {
                // Lock the outer row too: its parent is another layout group.
                // A fitter on that row must not reduce the card to one glyph.
                float extras = 20; int visible = 1;
                if (iconShown) { extras += 24; visible++; }
                if (clearShown) { extras += 28; visible++; }
                float width = cardWidth + extras + (visible - 1) * 10;
                SetQuestFitterV8(root, first, false);
                FixedQuestSizeV9(root, first, width, cardHeight + 12);
                SaveRect(root);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cardHeight + 12);
                var parent = root.parent as RectTransform;
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent && parent.GetComponent<LayoutGroup>() ? parent : root);
                questLayoutsV9[tracker] = new QuestLayoutStampV9 { Text = label.text, Font = label.font,
                    Style = label.fontStyle, Icon = iconShown, Clear = clearShown,
                    RowSize = new Vector2(width, cardHeight + 12), CardWidth = cardWidth };
            }
        }

        void FixedQuestSizeV9(RectTransform rect, bool first, float width, float height)
        {
            var size = QuestLayoutElementV8(rect, first);
            size.ignoreLayout = false; size.layoutPriority = 10;
            size.minWidth = size.preferredWidth = width;
            size.minHeight = size.preferredHeight = height;
            size.flexibleWidth = size.flexibleHeight = 0;
        }

        void SnapshotQuestLayoutV8(HorizontalOrVerticalLayoutGroup layout)
        {
            var padding = layout.padding; var alignment = layout.childAlignment; float spacing = layout.spacing;
            bool width = layout.childControlWidth, height = layout.childControlHeight;
            bool expandWidth = layout.childForceExpandWidth, expandHeight = layout.childForceExpandHeight;
            AddVisibilityUndo(layout, () => {
                if (!layout) return;
                layout.padding = padding; layout.childAlignment = alignment; layout.spacing = spacing;
                layout.childControlWidth = width; layout.childControlHeight = height;
                layout.childForceExpandWidth = expandWidth; layout.childForceExpandHeight = expandHeight;
            });
        }

        void SetQuestFitterV8(RectTransform target, bool first, bool verticalPreferred)
        {
            var fitter = target.GetComponent<ContentSizeFitter>();
            if (!fitter) return;
            if (first)
            {
                var horizontal = fitter.horizontalFit; var vertical = fitter.verticalFit;
                AddVisibilityUndo(fitter, () => { if (fitter) { fitter.horizontalFit = horizontal; fitter.verticalFit = vertical; } });
            }
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = verticalPreferred ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        LayoutElement QuestLayoutElementV8(RectTransform rect, bool first)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (!element)
            {
                element = rect.gameObject.AddComponent<LayoutElement>();
                var created = element;
                AddVisibilityUndo(created, () => { if (created) Destroy(created); });
            }
            else if (first)
            {
                float minWidth = element.minWidth, minHeight = element.minHeight;
                float width = element.preferredWidth, height = element.preferredHeight;
                float flexWidth = element.flexibleWidth, flexHeight = element.flexibleHeight;
                bool ignored = element.ignoreLayout; int priority = element.layoutPriority;
                AddVisibilityUndo(element, () => {
                    if (!element) return;
                    element.minWidth = minWidth; element.minHeight = minHeight;
                    element.preferredWidth = width; element.preferredHeight = height;
                    element.flexibleWidth = flexWidth; element.flexibleHeight = flexHeight;
                    element.ignoreLayout = ignored; element.layoutPriority = priority;
                });
            }
            return element;
        }

        Sprite HudBarSpriteV8()
        {
            if (hudBarSprite) return hudBarSprite;
            const int width = 256, height = 24; const float radius = 4;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float px = Mathf.Abs(x + .5f - width * .5f) - (width * .5f - radius);
                    float py = Mathf.Abs(y + .5f - height * .5f) - (height * .5f - radius);
                    float distance = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude +
                        Mathf.Min(Mathf.Max(px, py), 0) - radius;
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(.5f - distance) * 255));
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "ReefUI proportionate vital fill"; texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels); texture.Apply(false, true);
            hudBarSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            return hudBarSprite;
        }

        void HudTextV7(TMP_Text text, Color color)
        {
            if (!text) return;
            TextColor(text, color);
            float spacing = text.characterSpacing; var style = text.fontStyle;
            AddVisibilityUndo(text, () => { if (text) { text.characterSpacing = spacing; text.fontStyle = style; } });
            text.characterSpacing = .55f;
            text.fontStyle |= FontStyles.Bold;
        }

        Sprite HudGlyphSpriteV7(HudGlyphKind kind)
        {
            if (hudV7Glyphs.TryGetValue(kind, out var sprite) && sprite) return sprite;
            const int size = 96;
            var paths = HudGlyphPathsV7(kind);
            var pixels = new Color32[size * size];
            const float stroke = .027f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var point = new Vector2((x + .5f) / size, (y + .5f) / size);
                    float distance = 10;
                    foreach (var path in paths)
                        for (int i = 1; i < path.Length; i++)
                            distance = Mathf.Min(distance, HudLineDistanceV7(point, path[i - 1], path[i]));
                    float alpha = Mathf.Clamp01((stroke - distance) * size + .5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "ReefUI survival glyph " + kind;
            texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels); texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
            sprite.name = texture.name; hudV7Glyphs[kind] = sprite; return sprite;
        }

        static float HudLineDistanceV7(Vector2 p, Vector2 a, Vector2 b)
        {
            var edge = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, edge) / Mathf.Max(.000001f, edge.sqrMagnitude));
            return (p - a - edge * t).magnitude;
        }

        static List<Vector2[]> HudGlyphPathsV7(HudGlyphKind kind)
        {
            var paths = new List<Vector2[]>();
            if (kind == HudGlyphKind.Food)
            {
                // A fish silhouette belongs to Sunkenland's scavenging setting.
                var body = new Vector2[41];
                for (int i = 0; i < body.Length; i++)
                {
                    float a = i * Mathf.PI * 2 / (body.Length - 1);
                    body[i] = new Vector2(.43f + Mathf.Cos(a) * .28f, .5f + Mathf.Sin(a) * .21f);
                }
                paths.Add(body);
                paths.Add(new[] { new Vector2(.69f,.5f),new Vector2(.88f,.71f),new Vector2(.88f,.29f),new Vector2(.69f,.5f) });
                paths.Add(new[] { new Vector2(.29f,.57f),new Vector2(.29f,.57f) });
                paths.Add(new[] { new Vector2(.41f,.69f),new Vector2(.48f,.8f),new Vector2(.58f,.68f) });
            }
            else if (kind == HudGlyphKind.Water)
            {
                var drop = new List<Vector2>();
                // Smooth lower bowl, tapering to a single high point.
                drop.Add(new Vector2(.5f,.9f)); drop.Add(new Vector2(.26f,.54f));
                for (int i = 0; i <= 30; i++)
                {
                    float a = Mathf.Lerp(150,390,i / 30f) * Mathf.Deg2Rad;
                    drop.Add(new Vector2(.5f+Mathf.Cos(a)*.28f,.4f+Mathf.Sin(a)*.28f));
                }
                drop.Add(new Vector2(.5f,.9f)); paths.Add(drop.ToArray());
                paths.Add(new[] { new Vector2(.34f,.38f),new Vector2(.38f,.29f),new Vector2(.46f,.26f) });
            }
            else if (kind == HudGlyphKind.Health)
                paths.Add(new[] { new Vector2(.4f,.83f),new Vector2(.6f,.83f),new Vector2(.6f,.6f),new Vector2(.83f,.6f),new Vector2(.83f,.4f),new Vector2(.6f,.4f),new Vector2(.6f,.17f),new Vector2(.4f,.17f),new Vector2(.4f,.4f),new Vector2(.17f,.4f),new Vector2(.17f,.6f),new Vector2(.4f,.6f),new Vector2(.4f,.83f) });
            else if (kind == HudGlyphKind.Energy)
                paths.Add(new[] { new Vector2(.57f,.88f),new Vector2(.22f,.45f),new Vector2(.47f,.45f),new Vector2(.4f,.12f),new Vector2(.8f,.59f),new Vector2(.55f,.59f),new Vector2(.57f,.88f) });
            else if (kind == HudGlyphKind.Objective)
            {
                paths.Add(new[] { new Vector2(.5f,.88f),new Vector2(.84f,.5f),new Vector2(.5f,.12f),new Vector2(.16f,.5f),new Vector2(.5f,.88f) });
                paths.Add(new[] { new Vector2(.5f,.69f),new Vector2(.5f,.42f) });
                paths.Add(new[] { new Vector2(.5f,.3f),new Vector2(.5f,.3f) });
            }
            else if (kind == HudGlyphKind.Help)
            {
                var circle = new Vector2[41];
                for (int i = 0; i < circle.Length; i++)
                {
                    float a = i * Mathf.PI * 2 / (circle.Length - 1);
                    circle[i] = new Vector2(.5f + Mathf.Cos(a) * .38f, .5f + Mathf.Sin(a) * .38f);
                }
                paths.Add(circle);
                paths.Add(new[] { new Vector2(.37f,.64f),new Vector2(.4f,.71f),new Vector2(.5f,.74f),new Vector2(.6f,.7f),new Vector2(.63f,.62f),new Vector2(.58f,.55f),new Vector2(.5f,.5f),new Vector2(.5f,.43f) });
                paths.Add(new[] { new Vector2(.5f,.28f),new Vector2(.5f,.28f) });
            }
            else if (kind == HudGlyphKind.Sort)
            {
                paths.Add(new[] { new Vector2(.5f,.86f),new Vector2(.86f,.66f),new Vector2(.5f,.46f),new Vector2(.14f,.66f),new Vector2(.5f,.86f) });
                paths.Add(new[] { new Vector2(.14f,.49f),new Vector2(.5f,.29f),new Vector2(.86f,.49f) });
                paths.Add(new[] { new Vector2(.14f,.32f),new Vector2(.5f,.12f),new Vector2(.86f,.32f) });
            }
            else paths.AddRange(new[] {
                new[] {new Vector2(.29f,.29f),new Vector2(.71f,.71f)},
                new[] {new Vector2(.29f,.71f),new Vector2(.71f,.29f)} });
            return paths;
        }

        internal void PruneHudV7()
        {
            hudV7Styled.RemoveWhere(screen => !screen);
            var dead = new List<MonoBehaviour>();
            foreach (var pair in questLayoutsV9) if (!pair.Key) dead.Add(pair.Key);
            foreach (var key in dead) questLayoutsV9.Remove(key);
        }
        internal void ResetHudV7()
        {
            hudV7Styled.Clear();
            questLayoutsV9.Clear();
            foreach (var sprite in hudV7Glyphs.Values)
                if (sprite) { var texture = sprite.texture; Destroy(sprite); Destroy(texture); }
            hudV7Glyphs.Clear();
            if (hudBarSprite) { var texture = hudBarSprite.texture; Destroy(hudBarSprite); Destroy(texture); }
            hudBarSprite = null;
        }
    }
}
