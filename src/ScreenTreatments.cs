using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        // Separate from the global theme caches: pooled cards and newly opened
        // settings panels must still receive their screen-specific treatment.
        readonly HashSet<UnityEngine.Object> screenSurfaces = new HashSet<UnityEngine.Object>();
        readonly HashSet<UnityEngine.Object> screenFrames = new HashSet<UnityEngine.Object>();
        readonly Dictionary<Toggle, Image> settingsTabTrims = new Dictionary<Toggle, Image>();
        readonly HashSet<Toggle> settingsTabListeners = new HashSet<Toggle>();
        readonly Dictionary<RectTransform, RectTransform> loadingBackdrops = new Dictionary<RectTransform, RectTransform>();
        readonly List<Toggle> deadSettingsTabs = new List<Toggle>();
        readonly List<RectTransform> deadLoadingLabels = new List<RectTransform>();

        internal void ApplyScreenTreatments(MonoBehaviour screen)
        {
            if (!screen || IsOurObject(screen.transform)) return;
            switch (screen.GetType().Name)
            {
                case "UISettings": TreatSettings(screen); break;
                case "LocalizedKeybindingButton": TreatKeyBindingRow(screen); break;
                case "UILoadingScreen": TreatLoading(screen); break;
                case "UICombat": TreatHud(screen); break;
                case "UICompass":
                    ScreenFrame(screen.transform as RectTransform, new Color(.015f, .055f, .075f, .66f),
                        new Color(.3f, .72f, .83f, .45f), 1100, 150, true);
                    break;
                case "UIQuickSlot":
                    // UICombat iterates every child of quickSlotIconGroup as a slot.
                    // Add decoration inside each slot, never to its item holder.
                    if(screenFrames.Add(screen.transform as RectTransform)) StyleSlotSurface(screen,Field<Button>(screen,"btnSlot"));
                    break;
                case "UIBuilding": TreatBuilder(screen); break;
                case "StructuresIcon":
                    // BuilderPolish owns the calm card surfaces and captions.
                    break;
                case "UICraftableInfoDisplayBuilding":
                    ScreenFrame(screen.transform as RectTransform, Color.clear,
                        new Color(.3f, .72f, .83f, .48f), 1050, 260, false);
                    break;
            }
        }

        void TreatSettings(MonoBehaviour screen)
        {
            // These are game-owned panel fields, not images found by a broad
            // background-name match. No controller diagrams or key icons change.
            var root = screen.transform as RectTransform;
            var backdrop = root ? root.GetComponent<Image>() : null;
            if (backdrop) ScreenSurface(backdrop, new Color(.012f, .035f, .05f, .97f));
            foreach (string field in new[] { "panelGeneral", "panelKeybinding", "panelGraphics", "panelAudio", "panelControllers" })
            {
                var panel = ScreenFieldRect(screen, field);
                ScreenFrame(panel, new Color(.015f, .052f, .07f, .9f),
                    new Color(.3f, .72f, .83f, .42f), 1900, 1200, true);
            }
            foreach (string field in new[] { "togglePanelGeneral", "togglePanelKeybinding", "togglePanelGraphics", "togglePanelAudio", "togglePanelControllers" })
            {
                var tab = Field<Toggle>(screen, field);
                if (!tab) continue;
                if (!settingsTabTrims.TryGetValue(tab, out var surface) || !surface)
                {
                    var plate = ScreenFrame(tab.transform as RectTransform, new Color(.015f, .052f, .07f, .96f),
                        Cyan, 600, 150, true);
                    if (plate)
                    {
                        surface = plate.GetComponent<Image>();
                        settingsTabTrims[tab] = surface;
                    }
                    else
                    {
                        surface=tab.targetGraphic as Image;
                        if(surface) { ScreenSurface(surface,new Color(.07f,.08f,.09f,.58f));settingsTabTrims[tab]=surface; }
                    }
                }
                if (!surface) continue;
                if (settingsTabListeners.Add(tab))
                {
                    // Toggle already exposes the exact event that changes its
                    // selection. Register once, rather than polling its state.
                    var observedTab = tab;
                    UnityAction<bool> listener = selected => RefreshSettingsTab(observedTab, selected);
                    tab.onValueChanged.AddListener(listener);
                    AddVisibilityUndo(observedTab, () => { if (observedTab) observedTab.onValueChanged.RemoveListener(listener); });
                }
                RefreshSettingsTab(tab, tab.isOn);
            }

            // Stock keybinding rows use bright torn-paper backgrounds. Replace
            // only wide, short neutral row surfaces that actually contain labels.
            // Small keyboard/mouse Images, dropdown arrows and diagrams are kept.
            foreach (var image in screen.GetComponentsInChildren<Image>(true))
            {
                if (!image || IsOurObject(image.transform) || screenSurfaces.Contains(image) ||
                    image.type == Image.Type.Filled || image.GetComponent<Mask>() || image.GetComponent<RectMask2D>()) continue;
                var r = image.rectTransform.rect;
                var c = image.color;
                bool row = r.width >= 260 && r.height >= 24 && r.height <= 100;
                bool neutral = Mathf.Abs(c.r - c.g) < .1f && Mathf.Abs(c.g - c.b) < .1f;
                bool controlPart = image.GetComponentInParent<Slider>() || image.GetComponentInParent<Scrollbar>() ||
                    image.GetComponentInParent<TMP_Dropdown>() || image.GetComponentInParent<Dropdown>();
                if (row && neutral && !controlPart && c.a > .15f)
                    ScreenSurface(image, new Color(.055f, .065f, .078f, .70f));
            }
            foreach (var text in screen.GetComponentsInChildren<TMP_Text>(true))
                if (!IsOurObject(text.transform)) TextColor(text, White);
            foreach (var text in screen.GetComponentsInChildren<Text>(true))
                if (!IsOurObject(text.transform)) LegacyColor(text, White);

            // Thin settings tracks are calmer than a stretched metal-frame sprite.
            // Slider values, bounds, handles, events and fill amounts remain intact.
            foreach (var slider in screen.GetComponentsInChildren<Slider>(true))
            {
                var bg = slider.transform.Find("Background");
                if (bg) ScreenSurface(bg.GetComponent<Image>(), new Color(.07f, .16f, .19f, 1));
            }
        }

        void RefreshSettingsTab(Toggle tab, bool selected)
        {
            if (!tab || !settingsTabTrims.TryGetValue(tab, out var surface) || !surface) return;
            var color = selected ? new Color(1.22f,1.07f,.79f,.98f) : new Color(.86f,.93f,.98f,.94f);
            if (surface.color != color) surface.color = color;
        }

        void TreatLoading(MonoBehaviour screen)
        {
            // Retire the stock broad brush-stroke backings, leaving a soft rounded surface per text block.
            foreach(var image in screen.GetComponentsInChildren<Image>(true))
            {
                if(!image || IsOurObject(image.transform) || image.type==Image.Type.Filled) continue;
                var r=image.rectTransform.rect;
                if(r.width>250 && r.height>=16 && r.height<200) ScreenSurface(image,new Color(.025f,.033f,.043f,0));
            }
            foreach (var label in screen.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!label || IsOurObject(label.transform)) continue;
                if (label.color.a < .01f) { RefreshLoadingBackdropVisibility(label); continue; }
                TextColor(label, label.name.ToLowerInvariant().Contains("loading") ? Gold : White);
                LoadingBackdrop(label.rectTransform);
            }
            foreach (var label in screen.GetComponentsInChildren<Text>(true))
            {
                if (!label || IsOurObject(label.transform)) continue;
                if (label.color.a < .01f) { RefreshLoadingBackdropVisibility(label); continue; }
                LegacyColor(label, label.name.ToLowerInvariant().Contains("loading") ? Gold : White);
                LoadingBackdrop(label.rectTransform);
            }
        }

        void TreatKeyBindingRow(MonoBehaviour binding)
        {
            var keyIcon=Field<Image>(binding,"imgKey");
            // In these prefabs the white strip is often a sibling of the label,
            // so a child-label heuristic cannot reliably identify its backing.
            var p=binding.transform;
            for(int depth=0;p && depth<5;depth++,p=p.parent)
            {
                var row=p as RectTransform;
                if(!row || row.rect.width<240 || row.rect.height<20 || row.rect.height>115) continue;
                foreach(var image in p.GetComponentsInChildren<Image>(true))
                {
                    if(!image || image==keyIcon || IsOurObject(image.transform) || image.type==Image.Type.Filled || image.GetComponent<Mask>()) continue;
                    var r=image.rectTransform.rect;
                    if(r.width<row.rect.width*.6f || r.height<20 || r.height>115) continue;
                    ScreenSurface(image,new Color(.055f,.065f,.078f,.70f));
                }
                break;
            }
        }

        void TreatHud(MonoBehaviour screen)
        {
            // Compact HUD groups only. Leave CombatRoot, markers, reticles,
            // selection indicators, warning colors and all filled bars alone.
            foreach (string field in new[] { "TimeGroup", "PlayerStateGroup", "locationPanel", "hintBG", "importantHintBG", "tooltipBG", "weaponPanel" })
                ScreenFrame(ScreenFieldRect(screen, field), new Color(.012f, .04f, .055f, .77f),
                    new Color(.3f, .72f, .83f, .4f), 750, 330, true);
            foreach (string field in new[] { "tmpDays", "tmpTimer", "tmpHour", "tmpAltitude", "tmpHint", "tmpImportantHint", "tmpTooltip", "tmpLocationName", "tmpLocationFaction" })
                TextColor(Field<TMP_Text>(screen, field), White);
        }

        void TreatBuilder(MonoBehaviour screen)
        {
            // The game reparents every child of StructuresLayoutRoot and
            // destroys children of LayoutRootLeft/Right when changing category.
            // Decorate their containing panel rather than those managed holders.
            ScreenFrame(ScreenFieldRect(screen, "StructuresPart"), new Color(.012f, .04f, .055f, .87f),
                new Color(.3f, .72f, .83f, .6f), 1900, 1100, true);
            ScreenFrame(ScreenFieldRect(screen, "Tips"), new Color(.012f, .04f, .055f, .96f),
                new Color(.3f, .72f, .83f, .6f), 900, 650, true);
            TextColor(Field<TMP_Text>(screen, "tmpTipName"), Gold);
            TextColor(Field<TMP_Text>(screen, "tmpTipDescription"), White);
            // tmpTipHealth and required material amounts retain semantic colors.
        }

        static RectTransform ScreenFieldRect(object screen, string field)
        {
            var value = Field<UnityEngine.Object>(screen, field);
            if (value is GameObject go) return go.transform as RectTransform;
            if (value is Component component) return component.transform as RectTransform;
            return null;
        }

        void ScreenSurface(Image image, Color color)
        {
            if (!image || IsOurObject(image.transform) || image.type == Image.Type.Filled ||
                image.color.a < .01f || !screenSurfaces.Add(image)) return;
            // ImageStyle records the original sprite, color and sliced scale.
            // This quiet center keeps small labels legible, with metal reserved
            // for larger frames and existing menu buttons.
            ImageStyle(image, color);
        }

        RectTransform ScreenFrame(RectTransform target, Color fill, Color trim, float maxWidth, float maxHeight, bool fillCenter)
        {
            if (!target || IsOurObject(target) || screenFrames.Contains(target)) return null;
            var r = target.rect;
            if (r.width < 18 || r.height < 16 || r.width > maxWidth || r.height > maxHeight) return null;
            var canvas = target.GetComponentInParent<Canvas>();
            var canvasRect = canvas ? canvas.rootCanvas.transform as RectTransform : null;
            if (canvasRect && r.width >= canvasRect.rect.width * .98f && r.height >= canvasRect.rect.height * .82f) return null;
            // A child Image renders over a Graphic on its parent. Such roots get
            // an outline only, preserving icons, text and original backplates.
            var original=target.GetComponent<Graphic>();
            if(original) return null;
            if(!fillCenter) return null;
            var plate = Plate(target, fill, trim, false);
            if (!plate) return null;
            var image = plate.GetComponent<Image>();
            image.sprite = panelSprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.color = fill;
            image.pixelsPerUnitMultiplier = 1;
            image.raycastTarget = false;
            screenFrames.Add(target);
            return plate;
        }

        void LoadingBackdrop(RectTransform label)
        {
            if (!label || !label.parent || label.rect.width < 20 || label.rect.height < 16) return;
            if (!loadingBackdrops.TryGetValue(label, out var backing) || !backing)
            {
                // A sibling is required: a filled child of TMP_Text would cover
                // its glyphs. UILoadingScreen does not iterate a managed child list.
                var go = new GameObject("ReefUI loading backdrop", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
                backing = (RectTransform)go.transform;
                backing.SetParent(label.parent, false);
                go.GetComponent<LayoutElement>().ignoreLayout = true;
                var image = go.GetComponent<Image>();
                image.sprite = panelSprite; image.type = Image.Type.Sliced;
                image.color = new Color(.012f, .04f, .055f, .9f); image.raycastTarget = false;
                decorations.Add(go);
                loadingBackdrops[label] = backing;
            }
            // Follow the existing loading layout without resizing or reparenting it.
            if (backing.parent != label.parent) backing.SetParent(label.parent, false);
            if (backing.anchorMin != label.anchorMin) backing.anchorMin = label.anchorMin;
            if (backing.anchorMax != label.anchorMax) backing.anchorMax = label.anchorMax;
            if (backing.pivot != label.pivot) backing.pivot = label.pivot;
            if (backing.localScale != label.localScale) backing.localScale = label.localScale;
            if (backing.localRotation != label.localRotation) backing.localRotation = label.localRotation;
            if (backing.anchoredPosition != label.anchoredPosition) backing.anchoredPosition = label.anchoredPosition;
            var size = label.sizeDelta + new Vector2(20, 12);
            if (backing.sizeDelta != size) backing.sizeDelta = size;
            int beforeLabel = label.GetSiblingIndex();
            if (backing.GetSiblingIndex() < beforeLabel) beforeLabel--;
            int index = Mathf.Max(0, beforeLabel);
            if (backing.GetSiblingIndex() != index) backing.SetSiblingIndex(index);
            RefreshLoadingBackdropVisibility(label.GetComponent<Graphic>());
        }

        void RefreshLoadingBackdropVisibility(Graphic source)
        {
            if (!source || !loadingBackdrops.TryGetValue(source.rectTransform, out var backing) || !backing) return;
            bool visible = source.enabled && source.gameObject.activeSelf && source.color.a > .01f;
            if (backing.gameObject.activeSelf != visible) backing.gameObject.SetActive(visible);
            var renderer = backing.GetComponent<Image>().canvasRenderer;
            float alpha = source.canvasRenderer.GetAlpha();
            if (!Mathf.Approximately(renderer.GetAlpha(), alpha)) renderer.SetAlpha(alpha);
        }

        internal void ResetScreenTreatments()
        {
            screenSurfaces.Clear(); screenFrames.Clear(); settingsTabTrims.Clear(); settingsTabListeners.Clear(); loadingBackdrops.Clear();
            deadSettingsTabs.Clear(); deadLoadingLabels.Clear();
        }

        internal void PruneScreenTreatments()
        {
            // Scene changes retire their Unity objects. Reuse scratch lists so
            // stale surfaces and callbacks do not accumulate between worlds.
            screenSurfaces.RemoveWhere(surface => !surface);
            screenFrames.RemoveWhere(frame => !frame);
            settingsTabListeners.RemoveWhere(tab => !tab);
            deadSettingsTabs.Clear();
            foreach (var pair in settingsTabTrims)
                if (!pair.Key || !pair.Value) deadSettingsTabs.Add(pair.Key);
            foreach (var tab in deadSettingsTabs) settingsTabTrims.Remove(tab);
            deadSettingsTabs.Clear();
            deadLoadingLabels.Clear();
            foreach (var pair in loadingBackdrops)
                if (!pair.Key || !pair.Value) deadLoadingLabels.Add(pair.Key);
            foreach (var label in deadLoadingLabels)
            {
                var backing=loadingBackdrops[label];
                if(backing) { decorations.Remove(backing.gameObject); Destroy(backing.gameObject); }
                loadingBackdrops.Remove(label);
            }
            deadLoadingLabels.Clear();
        }
    }
}
