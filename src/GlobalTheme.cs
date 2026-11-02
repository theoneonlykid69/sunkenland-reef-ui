using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        readonly HashSet<UnityEngine.Object> globalStyled = new HashSet<UnityEngine.Object>();
        readonly HashSet<UnityEngine.Object> screensStyled = new HashSet<UnityEngine.Object>();
        readonly HashSet<UnityEngine.Object> globalLabels = new HashSet<UnityEngine.Object>();
        readonly Dictionary<RectTransform, Action> rectUndo = new Dictionary<RectTransform, Action>();
        sealed class LabelBinding { public Button Button; public Color Color; public bool SelectionEntry; }
        readonly Dictionary<TMP_Text, LabelBinding> buttonLabels = new Dictionary<TMP_Text, LabelBinding>();
        readonly Dictionary<Text, LabelBinding> legacyButtonLabels = new Dictionary<Text, LabelBinding>();
        sealed class ListenerUndo { public UnityEngine.Object Owner; public Action Restore; }
        readonly List<ListenerUndo> visibilityUndo = new List<ListenerUndo>();
        readonly Dictionary<Behaviour,bool> layoutDrivers=new Dictionary<Behaviour,bool>();
        static readonly Type requirementIconType=AccessTools.TypeByName("RequiredItemIcon");
        static readonly Type originSelectorTypeV91=AccessTools.TypeByName("UICharacterWorldCustomization");

        void AddVisibilityUndo(UnityEngine.Object owner, Action restore)
        { visibilityUndo.Add(new ListenerUndo { Owner=owner, Restore=restore }); }

        readonly Dictionary<Button,List<Graphic>> labelsByButton=new Dictionary<Button,List<Graphic>>();
        void UpdateButtonLabels(Button button)
        {
            if(!button || !labelsByButton.TryGetValue(button,out var labels)) return;
            int state=ButtonState(button);
            bool interactable=button.IsInteractable(), primary=false;
            foreach(var graphic in labels)
            {
                if(!graphic || !graphic.gameObject.activeInHierarchy || graphic.color.a<.01f) continue;
                LabelBinding binding;
                if(graphic is TMP_Text tmp) {if(!buttonLabels.TryGetValue(tmp,out binding)) continue;}
                else if(graphic is Text legacy) {if(!legacyButtonLabels.TryGetValue(legacy,out binding)) continue;}
                else continue;
                primary|=binding.Color==Gold;
                var color=binding.SelectionEntry && !interactable?Gold:binding.Color;
                if(interactable && (state==1 || state==3)) color=new Color(1,.84f,.49f,1);
                if(interactable && state==2) color=new Color(1,.95f,.78f,1);
                color.a=graphic.color.a;
                if(!interactable && !binding.SelectionEntry) {color.r*=.6f;color.g*=.6f;color.b*=.6f;}
                if(graphic.color!=color) graphic.color=color;
                ResetRendererTint(graphic);
            }
            if(buttonAccents.TryGetValue(button,out var accent) && accent)
            {
                float alpha=interactable && (state==1 || state==2 || state==3)?.95f:primary?.55f:0;
                var color=new Color(Gold.r,Gold.g,Gold.b,alpha);
                if(accent.color!=color) accent.color=color;
            }
            if(buttonWashes.TryGetValue(button,out var wash) && wash)
            {
                float alpha=!interactable?0:state==2?.23f:(state==1 || state==3)?.19f:primary?.14f:0;
                var color=new Color(Gold.r,Gold.g,Gold.b,alpha);
                if(wash.color!=color) wash.color=color;
            }
        }

        void RegisterButtonLabels(Button button, bool primary)
        {
            if(!labelsByButton.TryGetValue(button,out var labels)) labelsByButton[button]=labels=new List<Graphic>();
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                string semanticName=text.name.ToLowerInvariant();
                if(semanticName.Contains("count") || semanticName.Contains("amount") || semanticName.Contains("condition")) continue;
                var requirement=requirementIconType==null?null:text.GetComponentInParent(requirementIconType,true);
                if(requirement && Field<TMP_Text>(requirement,"CountTxt")==text) continue;
                if(IsOurObject(text.transform) && text.name!="ReefUI craft label" && text.name!="ReefUI category name" && text.name!="ReefUI building name") continue;
                if (text.color.a < .01f) continue;
                bool gold=primary || (buttonLabels.TryGetValue(text,out var previous) && previous.Color==Gold);
                bool caption=text.name=="ReefUI category name" || text.name=="ReefUI building name" || text.name=="ReefUI craft label";
                TextColor(text, gold ? Gold : White, !caption);
                buttonLabels[text] = new LabelBinding { Button=button, Color=gold ? Gold : White, SelectionEntry=IsSelectionEntry(button.transform) };
                if(!labels.Contains(text)) labels.Add(text);
            }
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                if(IsOurObject(text.transform)) continue;
                if (text.color.a < .01f) continue;
                bool gold=primary || (legacyButtonLabels.TryGetValue(text,out var previous) && previous.Color==Gold);
                LegacyColor(text, gold ? Gold : White);
                legacyButtonLabels[text] = new LabelBinding { Button=button, Color=gold ? Gold : White, SelectionEntry=IsSelectionEntry(button.transform) };
                if(!labels.Contains(text)) labels.Add(text);
            }
            UpdateButtonLabels(button);
        }

        static bool IsSlotButton(Button button)
        {
            for (var p=button.transform; p && p != button.transform.root; p=p.parent)
                foreach (var component in p.GetComponents<MonoBehaviour>())
                    if (component)
                        for (var t=component.GetType(); t!=null; t=t.BaseType)
                            if (t.Name == "UIItemSlot") return true;
            return false;
        }

        void ApplyButtonPlate(Button button, bool primary, bool labels=true)
        {
            if(labels) { DecorateButton(button,primary);return; }
            foreach (Transform child in button.transform)
                if (child.name == "ReefUI plate") child.gameObject.SetActive(false);
            bool parentIsText=button.GetComponent<TMP_Text>() || button.GetComponent<Text>();
            bool preserveGraphic=!labels || parentIsText || button.GetComponent<RawImage>();
            if (!preserveGraphic) TintOriginalButtonSurface(button);
            var plate = Plate(button.transform as RectTransform, Navy, primary ? Gold : Cyan, false, !preserveGraphic);
            if (plate) button.targetGraphic = plate.GetComponent<Image>();
            ButtonStyle(button);
            if (labels) RegisterButtonLabels(button,primary);
        }

        static bool IsOurObject(Transform t)
        {
            for (var p = t; p; p = p.parent)
            {
                if (p.name.StartsWith("ReefUI")) return true;
                // A stock button may have been moved into our dock; its labels/icons remain game content.
                if (p.GetComponent<Button>()) return false;
            }
            return false;
        }

        static bool HasAncestorType(Transform t, params string[] names)
        {
            for(var p=t;p;p=p.parent)
                foreach(var component in p.GetComponents<MonoBehaviour>())
                    if(component)
                        foreach(string name in names) if(component.GetType().Name==name) return true;
            return false;
        }

        static bool IsJournal(Transform t) => HasAncestorType(t,"UIDairy","DairyPageDisplay","DairyEntry","UIDairyLabelButton");
        static bool IsSelectionEntry(Transform t) => HasAncestorType(t,"UIWorldSelectionEntry","UICharacterSelectionEntry");

        static void ResetRendererTint(Graphic graphic)
        {
            if (!graphic) return;
            float alpha=graphic.canvasRenderer.GetAlpha();
            var current=graphic.canvasRenderer.GetColor();
            if(Mathf.Approximately(current.r,1) && Mathf.Approximately(current.g,1) && Mathf.Approximately(current.b,1)) return;
            // Stop obsolete Selectable tweens on the old label; keep visibility controlled by the game.
            graphic.CrossFadeColor(new Color(1,1,1,alpha),0,true,true);
        }

        void PruneSceneCaches()
        {
            // Release references from unloaded scenes; their original objects no longer exist.
            globalStyled.RemoveWhere(x => !x); screensStyled.RemoveWhere(x => !x); globalLabels.RemoveWhere(x => !x);
            inventoryElements.RemoveWhere(x => !x);
            var deadLabels = new List<TMP_Text>(); foreach (var pair in buttonLabels) if (!pair.Key || !pair.Value.Button) deadLabels.Add(pair.Key);
            foreach (var label in deadLabels) buttonLabels.Remove(label);
            var deadLegacy = new List<Text>(); foreach (var pair in legacyButtonLabels) if (!pair.Key || !pair.Value.Button) deadLegacy.Add(pair.Key);
            foreach (var label in deadLegacy) legacyButtonLabels.Remove(label);
            var dead = new List<UnityEngine.Object>();
            foreach (var x in undo.Keys) if (!x) dead.Add(x);
            foreach (var x in dead) undo.Remove(x);
            decorations.RemoveAll(x => !x);
            var deadRects=new List<RectTransform>();foreach(var rect in rectUndo.Keys) if(!rect) deadRects.Add(rect);
            foreach(var rect in deadRects) rectUndo.Remove(rect);
            var deadDrivers=new List<Behaviour>();foreach(var driver in layoutDrivers.Keys) if(!driver) deadDrivers.Add(driver);
            foreach(var driver in deadDrivers) layoutDrivers.Remove(driver);
            visibilityUndo.RemoveAll(listener=>!listener.Owner);
            var deadButtons=new List<Button>();foreach(var button in labelsByButton.Keys) if(!button) deadButtons.Add(button);
            foreach(var button in deadButtons) labelsByButton.Remove(button);
        }

        void StyleRoot(Transform root)
        {
            var canvas=root.GetComponentInParent<Canvas>();
            if(!canvas || canvas.renderMode==RenderMode.WorldSpace) return;
            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                if (IsOurObject(b.transform) || IsJournal(b.transform)) continue;
                if(!globalStyled.Add(b))
                {
                    // Opening a screen may reveal labels populated by localization.
                    if(b.gameObject.activeInHierarchy && !IsSlotButton(b)) RegisterButtonLabels(b,false);
                    continue;
                }
                if (IsSlotButton(b)) continue;
                var text = b.GetComponentInChildren<TMP_Text>(true);
                var legacy = b.GetComponentInChildren<Text>(true);
                bool labeled = (text && !string.IsNullOrWhiteSpace(text.text)) || (legacy && !string.IsNullOrWhiteSpace(legacy.text));
                ButtonStyle(b);
                if (labeled)
                {
                    // Own background leaves icon sprites and the original hit target intact.
                    ApplyButtonPlate(b,false);
                }
            }
            foreach (var s in root.GetComponentsInChildren<Slider>(true))
            {
                if (IsJournal(s.transform) || !globalStyled.Add(s)) continue;
                if (s.fillRect) TintImage(s.fillRect.GetComponent<Image>(), Cyan);
                if (s.handleRect) TintImage(s.handleRect.GetComponent<Image>(), Gold);
                var background = s.transform.Find("Background");
                if (background) ImageStyle(background.GetComponent<Image>(), Navy);
            }
            foreach (var t in root.GetComponentsInChildren<Toggle>(true))
            {
                if (IsJournal(t.transform) || !globalStyled.Add(t)) continue;
                // Preserve the checkmark shape and the game's on/off state.
                if (t.graphic is Image check) TintImage(check, Gold);
                if (t.targetGraphic is Image background && background != t.graphic)
                    ImageStyle(background, Navy);
                foreach (var label in t.GetComponentsInChildren<TMP_Text>(true)) TextColor(label, White);
            }
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (IsJournal(input.transform) || !globalStyled.Add(input)) continue;
                ImageStyle(input.targetGraphic as Image, Navy);
                Border(input.transform as RectTransform, Cyan);
                TextColor(input.textComponent, White);
                if (input.placeholder is TMP_Text placeholder) TextColor(placeholder, new Color(.5f, .7f, .76f));
            }
            foreach (var input in root.GetComponentsInChildren<InputField>(true))
            {
                if (IsJournal(input.transform) || !globalStyled.Add(input)) continue;
                ImageStyle(input.targetGraphic as Image, Navy);
                Border(input.transform as RectTransform, Cyan);
                LegacyColor(input.textComponent, White);
            }
            foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                if (IsJournal(dropdown.transform) || !globalStyled.Add(dropdown)) continue;
                ImageStyle(dropdown.targetGraphic as Image, Navy);
                Border(dropdown.transform as RectTransform, Cyan);
                TextColor(dropdown.captionText, White);
                if (dropdown.template) StylePanelImages(dropdown.template);
            }
            foreach (var dropdown in root.GetComponentsInChildren<Dropdown>(true))
            {
                if (IsJournal(dropdown.transform) || !globalStyled.Add(dropdown)) continue;
                ImageStyle(dropdown.targetGraphic as Image, Navy);
                LegacyColor(dropdown.captionText, White);
                if (dropdown.template) StylePanelImages(dropdown.template);
            }
            foreach (var scroll in root.GetComponentsInChildren<ScrollRect>(true))
            {
                if (IsJournal(scroll.transform) || !globalStyled.Add(scroll)) continue;
                var bg = scroll.GetComponent<Image>();
                if (bg && !scroll.GetComponent<Mask>()) ImageStyle(bg, Navy);
                if (scroll.horizontalScrollbar) StyleScrollbar(scroll.horizontalScrollbar);
                if (scroll.verticalScrollbar) StyleScrollbar(scroll.verticalScrollbar);
            }
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (!image || IsJournal(image.transform) || IsOurObject(image.transform) || globalStyled.Contains(image) || image.GetComponent<Selectable>() ||
                    image.GetComponent<Mask>() || image.GetComponent<RectMask2D>() || image.type == Image.Type.Filled) continue;
                // This native field is selected-option artwork, despite its
                // background-like name. Keep the game's changing illustration.
                var originOwner = originSelectorTypeV91 == null ? null : image.GetComponentInParent(originSelectorTypeV91, true);
                if (originOwner && Field<Image>(originOwner, "imgBackground") == image) continue;
                string n = image.name.ToLowerInvariant();
                var r = image.rectTransform.rect;
                // Restrict to neutral backing surfaces; never replace item/map icons or scene art.
                bool namedPanel = n == "background" || n == "bg" || n == "panel" || n.EndsWith("background") || n.EndsWith("panel");
                var c = image.color;
                bool neutral = Mathf.Abs(c.r - c.g) < .08f && Mathf.Abs(c.g - c.b) < .08f;
                if (namedPanel && neutral && c.a > .15f && r.width > 170 && r.height > 65 &&
                    !image.GetComponentInParent<Slider>() && !image.GetComponentInParent<Scrollbar>())
                {
                    globalStyled.Add(image); ImageStyle(image, Navy);
                }
            }
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                if (!IsJournal(text.transform) && !IsOurObject(text.transform) && globalLabels.Add(text)) LabelStyle(text);
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (IsJournal(text.transform) || !globalLabels.Add(text) || IsOurObject(text.transform)) continue;
                var c = text.color;
                if (c.r > .35f && Mathf.Abs(c.r - c.g) < .1f && Mathf.Abs(c.g - c.b) < .1f) LegacyColor(text, White);
            }
            foreach (var screen in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!screen || IsJournal(screen.transform)) continue;
                StyleInventoryElement(screen);
                switch (screen.GetType().Name)
                {
                    case "UIMenu": if (screensStyled.Add(screen)) LayoutMainMenu(screen, canvas); break;
                    case "UICharacterSelection": if (screensStyled.Add(screen)) CharacterScreen(screen, false); break;
                    case "UICharacterCreation": if (screensStyled.Add(screen)) CharacterScreen(screen, true); break;
                    case "TopTabToggle": if (screensStyled.Add(screen)) ConfigureTopTab(screen); break;
                }
                ApplyScreenTreatments(screen);
                PolishBuilder(screen);
                StyleHudV7(screen);
                // The pause controller can live outside this visual branch.
                // Its native Open event supplies the real controller after setup.
                if(screen.GetType().Name!="UIGameMenu") StyleMenusV7(screen);
                StyleInventoryV7(screen);
                StyleDialogsV8(screen);
                StyleOriginSelectionV91(screen);
            }
        }

        void TextColor(TMP_Text text, Color c, bool button = false)
        {
            if (!text || text.color.a < .01f) return;
            c.a=text.color.a;
            var desiredStyle=button ? text.fontStyle | FontStyles.Bold | FontStyles.UpperCase : text.fontStyle;
            float desiredSpacing=button ? 1.8f : text.characterSpacing;
            if(text.color==c && text.fontStyle==desiredStyle && Mathf.Approximately(text.characterSpacing,desiredSpacing))
            { ResetRendererTint(text);return; }
            Color old = text.color; var style = text.fontStyle; var spacing = text.characterSpacing;
            var rendererColor=text.canvasRenderer.GetColor();
            var align=text.alignment;var overflow=text.overflowMode;var margin=text.margin;
            bool wrapping=text.enableWordWrapping;float size=text.fontSize;
            bool auto = text.enableAutoSizing; float min = text.fontSizeMin, max = text.fontSizeMax;
            Remember(text, () => { if (text) { text.color = old; text.fontStyle = style; text.characterSpacing = spacing;
                text.enableAutoSizing = auto; text.fontSizeMin = min; text.fontSizeMax = max; text.canvasRenderer.SetColor(rendererColor);
                text.alignment=align;text.overflowMode=overflow;text.margin=margin;text.enableWordWrapping=wrapping;text.fontSize=size; } });
            if(text.color!=c) text.color = c;
            ResetRendererTint(text);
            if(text.fontStyle!=desiredStyle) text.fontStyle=desiredStyle;
            if(!Mathf.Approximately(text.characterSpacing,desiredSpacing)) text.characterSpacing=desiredSpacing;
        }

        void LegacyColor(Text text, Color c)
        {
            if (!text || text.color.a < .01f) return;
            c.a=text.color.a;
            if(text.color==c) { ResetRendererTint(text);return; }
            Color old = text.color; var rendererColor=text.canvasRenderer.GetColor();
            Remember(text, () => { if (text) { text.color = old; text.canvasRenderer.SetColor(rendererColor); } }); c.a = old.a; text.color = c;
            ResetRendererTint(text);
        }

        void TintImage(Image image, Color c)
        {
            if (!image) return;
            var old = image.color; var sprite = image.sprite; var type = image.type;bool aspect=image.preserveAspect;
            Remember(image, () => { if (image) { image.color = old; image.sprite = sprite; image.type = type;image.preserveAspect=aspect; } });
            image.color = c;
        }

        void TintOriginalButtonSurface(Button button)
        {
            // Hide only a backing on the button itself; child icon targets retain their textures.
            var image = button.GetComponent<Image>();
            if (image && image.type != Image.Type.Filled)
            {
                ImageStyle(image, new Color(1, 1, 1, 0));
            }
        }

        void StyleScrollbar(Scrollbar scroll)
        {
            ImageStyle(scroll.GetComponent<Image>(), Navy);
            if (scroll.handleRect) ImageStyle(scroll.handleRect.GetComponent<Image>(), Cyan);
        }

        void StylePanelImages(Transform root)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                if (image.name.ToLowerInvariant().Contains("background")) ImageStyle(image, Navy);
        }

        RectTransform Plate(RectTransform parent, Color fill, Color stroke, bool metal, bool fillCenter=true)
        {
            if (!parent) return null;
            var go = new GameObject("ReefUI plate", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            go.transform.SetParent(parent, false); go.transform.SetAsFirstSibling();
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>(); image.sprite = surfaceSpriteV7?surfaceSpriteV7:panelSprite; image.type = Image.Type.Sliced;
            image.color = surfaceSpriteV7?MaterialTint(fill):fill; image.raycastTarget = false;
            image.fillCenter=true; image.enabled=fillCenter;
            image.pixelsPerUnitMultiplier = 1;
            decorations.Add(go); return rt;
        }

        void SaveRect(RectTransform rt)
        {
            if (!rt || rectUndo.ContainsKey(rt)) return;
            var parent = rt.parent; int sibling = rt.GetSiblingIndex();
            Vector2 min = rt.anchorMin, max = rt.anchorMax, pivot = rt.pivot, pos = rt.anchoredPosition, size = rt.sizeDelta;
            Vector3 scale = rt.localScale;
            var action = new Action(() =>
            {
                if (!rt) return;
                if (parent) rt.SetParent(parent, false);
                rt.SetSiblingIndex(sibling); rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
                rt.sizeDelta = size; rt.anchoredPosition = pos; rt.localScale = scale;
            });
            rectUndo.Add(rt, action);
        }

        RectTransform NewContainer(RectTransform root, string name)
        {
            var go = new GameObject("ReefUI " + name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(root, false); go.GetComponent<LayoutElement>().ignoreLayout = true;
            decorations.Add(go); return (RectTransform)go.transform;
        }

        void PlaceButton(Button b, RectTransform parent, Vector2 min, Vector2 max, bool primary)
        {
            if (!b) return;
            var rt = b.transform as RectTransform; SaveRect(rt);
            FreezeLayoutDrivers(rt);
            rt.SetParent(parent, false); rt.anchorMin = min; rt.anchorMax = max;
            rt.pivot = new Vector2(.5f, .5f); rt.offsetMin = rt.offsetMax = Vector2.zero; rt.localScale = Vector3.one;
            // Remove previous generic plate so the tailored button gets one surface.
            foreach (Transform child in rt)
                if (child.name == "ReefUI plate") child.gameObject.SetActive(false);
            bool textOnRoot=b.GetComponent<TMP_Text>() || b.GetComponent<Text>();
            var plate = Plate(rt, primary ? new Color(.18f, .13f, .055f, .96f) : Navy, primary ? Gold : Cyan, false,!textOnRoot);
            b.targetGraphic = plate.GetComponent<Image>();
            ButtonStyle(b);
            RegisterButtonLabels(b,primary);
            var text = b.GetComponentInChildren<TMP_Text>(true);
            if (text)
            {
                TextColor(text, primary ? Gold : White, true);
                text.enableAutoSizing = true; text.fontSizeMin = 12; text.fontSizeMax = 25;
                text.alignment=TextAlignmentOptions.Center;text.enableWordWrapping=false;
                text.overflowMode=TextOverflowModes.Ellipsis;text.margin=Vector4.zero;
                if(text.transform!=b.transform)
                {
                    SaveRect(text.rectTransform); FreezeLayoutDrivers(text.rectTransform);
                    if(text.transform.parent!=b.transform) text.transform.SetParent(b.transform,false);
                    text.rectTransform.anchorMin=Vector2.zero;text.rectTransform.anchorMax=Vector2.one;
                    text.rectTransform.offsetMin=new Vector2(38,5);text.rectTransform.offsetMax=new Vector2(-38,-5);
                }
            }
        }

        void FreezeLayoutDrivers(RectTransform rt)
        {
            foreach(var driver in rt.GetComponents<Behaviour>())
                if(driver is ContentSizeFitter || driver is AspectRatioFitter)
                {
                    if(!layoutDrivers.ContainsKey(driver)) layoutDrivers.Add(driver,driver.enabled);
                    driver.enabled=false;
                }
        }

        void LayoutMainMenu(MonoBehaviour screen, Canvas canvas)
        {
            // tmpCustomTest may share a real button label in shipped scenes; preserve its enabled state.
            var root = screen.transform as RectTransform; if (!root) return;
            var panel = NewContainer(root, "menu dock");
            panel.anchorMin = new Vector2(.5f, .13f); panel.anchorMax = new Vector2(.5f, .13f);
            panel.pivot = new Vector2(.5f, 0); panel.anchoredPosition = Vector2.zero;
            // Reference units follow the existing canvas scale, with a responsive height cap.
            var canvasRt = canvas.transform as RectTransform;
            float h = canvasRt ? canvasRt.rect.height : 1080;
            float height = Mathf.Min(380, h * .36f);
            panel.sizeDelta = new Vector2(height * 1.04f, height);
            Plate(panel, new Color(.02f, .05f, .065f, .92f), new Color(.46f, .32f, .17f), true);
            string[] fields = { "btnStartGame", "btnOpenUITutorial", "btnOpenSettings", "btnQuit" };
            for (int i = 0; i < fields.Length; i++)
            {
                float top = .95f - i * .235f;
                PlaceButton(Field<Button>(screen, fields[i]), panel, new Vector2(.055f, top - .20f), new Vector2(.945f, top), i == 0);
                AddSymbol(Field<Button>(screen,fields[i]),i);
            }
            var social=NewContainer(root,"social dock");
            social.anchorMin=social.anchorMax=new Vector2(1,0);social.pivot=new Vector2(1,0);
            social.anchoredPosition=new Vector2(-24,62);social.sizeDelta=new Vector2(232,112);
            int socialIndex = 0;
            foreach (string field in new[] { "btnDiscord", "btnSteamNews" })
            {
                var b = Field<Button>(screen, field);
                float bottom = socialIndex++ == 0 ? .55f : 0;
                PlaceButton(b,social,new Vector2(0,bottom),new Vector2(1,bottom+.45f),false);
                FitSocialContents(b);
            }
            Logger.LogInfo("Reef main menu layout applied.");
        }

        void FitSocialContents(Button b)
        {
            if(!b) return;
            foreach(var label in b.GetComponentsInChildren<TMP_Text>(true))
            {
                TextColor(label,White,true); label.enableAutoSizing=true; label.fontSizeMin=12;label.fontSizeMax=21;
                label.alignment=TextAlignmentOptions.Center;label.enableWordWrapping=false;label.overflowMode=TextOverflowModes.Ellipsis;
                label.margin=label.transform==b.transform ? new Vector4(10,0,44,0) : Vector4.zero;
                if(label.transform==b.transform) continue;
                var rt=label.rectTransform;SaveRect(rt);FreezeLayoutDrivers(rt);
                rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;
                rt.offsetMin=new Vector2(12,5);rt.offsetMax=new Vector2(-48,-5);
            }
            foreach(var icon in b.GetComponentsInChildren<Image>(true))
            {
                if(icon.transform==b.transform || IsOurObject(icon.transform)) continue;
                var rt=icon.rectTransform;SaveRect(rt);FreezeLayoutDrivers(rt);
                rt.SetParent(b.transform,false);rt.anchorMin=rt.anchorMax=new Vector2(1,.5f);rt.pivot=new Vector2(.5f,.5f);
                rt.sizeDelta=new Vector2(30,30);rt.anchoredPosition=new Vector2(-27,0);
                TintImage(icon,icon.color);icon.preserveAspect=true;
            }
        }

        void CharacterScreen(MonoBehaviour screen, bool creation)
        {
            var field = Field<Transform>(screen, "contentRoot");
            var root = field as RectTransform;
            if (!root) root = screen.transform as RectTransform;
            if (!root) return;
            // Existing panel positions and all model/customization behavior remain owned by the game.
            var canvas = root.GetComponentInParent<Canvas>();
            var canvasRect = canvas ? canvas.transform as RectTransform : null;
            RectTransform frameRoot = root;
            if (canvasRect && root.rect.width > canvasRect.rect.width * .6f)
            {
                // A contentRoot may span the screen. Frame only the control column, leaving the model visible.
                string[] fields = creation ? new[] { "btnMale", "btnFemale", "btnDone", "btnCancel", "sliderHair", "sliderSkin", "sliderHairColor", "characterNameField" }
                    : new[] { "btnNewCharacter", "btnDeleteCharacter", "btnOpenUIRestoreBackupSave", "btnSinglePlayer", "btnHostGame", "btnJoinGame", "btnBack" };
                bool found = false; Bounds bounds = default;
                foreach (string f in fields)
                {
                    var c = Field<Component>(screen, f); if (!c) continue;
                    var rt = c.transform as RectTransform; if (!rt) continue;
                    var corners = new Vector3[4]; rt.GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        var point = root.InverseTransformPoint(corner);
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                if (!found) return;
                frameRoot = NewContainer(root, "character frame"); frameRoot.SetAsFirstSibling();
                frameRoot.anchorMin = frameRoot.anchorMax = root.pivot;
                frameRoot.pivot = new Vector2(.5f,.5f); frameRoot.anchoredPosition = bounds.center;
                frameRoot.sizeDelta = new Vector2(bounds.size.x + 32, bounds.size.y + 32);
            }
            else
            {
                var surface = root.GetComponent<Image>(); if (surface) ImageStyle(surface, Navy);
            }
            Plate(frameRoot, new Color(.015f, .045f, .055f, .88f), new Color(.44f, .31f, .18f), true);
            foreach (string f in creation ? new[] { "btnDone" } : new[] { "btnSinglePlayer" })
            {
                var b = Field<Button>(screen, f);
                if (!b) continue;
                foreach (Transform child in b.transform) if (child.name == "ReefUI plate") child.gameObject.SetActive(false);
                var plate = Plate(b.transform as RectTransform, new Color(.18f, .13f, .055f, .97f), Gold, false);
                b.targetGraphic = plate.GetComponent<Image>();
                ButtonStyle(b);
                RegisterButtonLabels(b,true);
                TextColor(b.GetComponentInChildren<TMP_Text>(true), Gold, true);
            }
            Logger.LogInfo("Reef " + (creation ? "character creation" : "character selection") + " panel applied.");
        }

        void ConfigureTopTab(MonoBehaviour tab)
        {
            var fields=new Dictionary<System.Reflection.FieldInfo,object>();
            var colors=new Dictionary<string,Color> {
                {"imgBackgroundNormalColor",new Color(.86f,.93f,.98f,.98f)},{"imgBackgroundSelectedColor",new Color(1.22f,1.07f,.79f,1)},
                {"imgBackgroundHoverColor",new Color(1.1f,1.1f,1.05f,1)},
                {"tmpTextNormalColor",White},{"tmpTextSelectedColor",Gold},
                {"imgIconNormalColor",White},{"imgIconSelectedColor",Gold} };
            foreach (var pair in colors)
            {
                var f=FindField(tab.GetType(),pair.Key);
                if (f==null) continue; fields[f]=f.GetValue(tab); f.SetValue(tab,pair.Value);
            }
            Remember(tab,()=> {
                if (!tab) return;
                foreach (var pair in fields) pair.Key.SetValue(tab,pair.Value);
                var toggle=tab.GetComponent<Toggle>();
                AccessTools.Method(tab.GetType(),"SwitchState")?.Invoke(tab,new object[]{ toggle && toggle.isOn });
            });
            var image=Field<Image>(tab,"imgBackground"); ImageStyle(image,Navy);
            var current=tab.GetComponent<Toggle>();
            AccessTools.Method(tab.GetType(),"SwitchState")?.Invoke(tab,new object[]{current && current.isOn});
            Border(tab.transform as RectTransform,new Color(.3f,.7f,.8f,.6f));
        }

        void AddSymbol(Button button,int symbol)
        {
            if (!button) return;
            var go=new GameObject("ReefUI symbol",typeof(RectTransform),typeof(ReefSymbol));
            go.transform.SetParent(button.transform,false);
            var rt=(RectTransform)go.transform;
            rt.anchorMin=rt.anchorMax=new Vector2(.12f,.5f); rt.sizeDelta=new Vector2(25,25); rt.anchoredPosition=Vector2.zero;
            var graphic=go.GetComponent<ReefSymbol>(); graphic.Kind=symbol; graphic.color=symbol==0?Gold:White; graphic.raycastTarget=false;
            decorations.Add(go);
        }
    }

    public sealed class ReefSymbol : MaskableGraphic
    {
        public int Kind;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); float s=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f;
            if (Kind==0)
            {
                vh.AddVert(new Vector2(-s*.45f,-s*.7f),color,Vector2.zero);
                vh.AddVert(new Vector2(-s*.45f,s*.7f),color,Vector2.zero);
                vh.AddVert(new Vector2(s*.7f,0),color,Vector2.zero); vh.AddTriangle(0,1,2); return;
            }
            if (Kind==1)
            {
                Vector2[] a={new Vector2(-.85f,-.65f),new Vector2(-.85f,.65f),new Vector2(-.3f,.8f),new Vector2(0,.55f),new Vector2(.3f,.8f),new Vector2(.85f,.65f),new Vector2(.85f,-.65f),new Vector2(.3f,-.5f),new Vector2(0,-.7f),new Vector2(-.3f,-.5f),new Vector2(-.85f,-.65f)};
                for(int i=0;i<a.Length-1;i++) Stroke(vh,a[i]*s,a[i+1]*s,1.5f);
                Stroke(vh,new Vector2(0,-.7f)*s,new Vector2(0,.55f)*s,1.5f); return;
            }
            if (Kind==2)
            {
                Ring(vh,s*.6f,0,Mathf.PI*2,24);
                Ring(vh,s*.22f,0,Mathf.PI*2,16);
                for(int i=0;i<8;i++) {float a=i*Mathf.PI/4; var p=new Vector2(Mathf.Cos(a),Mathf.Sin(a)); Stroke(vh,p*s*.58f,p*s*.88f,3);}
                return;
            }
            Ring(vh,s*.75f,Mathf.PI*.7f,Mathf.PI*2.8f,28);
            Stroke(vh,new Vector2(0,s*.2f),new Vector2(0,s*.94f),2);
        }
        void Ring(VertexHelper vh,float radius,float start,float end,int steps)
        {
            for(int i=0;i<steps;i++)
            {
                float a=Mathf.Lerp(start,end,i/(float)steps),b=Mathf.Lerp(start,end,(i+1)/(float)steps);
                Stroke(vh,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,1.8f);
            }
        }
        void Stroke(VertexHelper vh,Vector2 a,Vector2 b,float width)
        {
            var d=(b-a).normalized; var n=new Vector2(-d.y,d.x)*width*.5f; int i=vh.currentVertCount;
            vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }

}
