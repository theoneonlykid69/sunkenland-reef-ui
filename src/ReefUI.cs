using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    [BepInPlugin("local.sunkenland.reefui", "Reef UI", "0.9.1")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        static readonly Color Navy = new Color(.025f, .033f, .043f, .86f);
        // Kept as a shared token for existing helpers; the bright cyan treatment is retired.
        static readonly Color Cyan = new Color(.40f, .42f, .43f, .22f);
        static readonly Color White = new Color(.90f, .91f, .92f, 1f);
        static readonly Color Gold = new Color(.87f, .72f, .43f, 1f);
        readonly Dictionary<UnityEngine.Object, Action> undo = new Dictionary<UnityEngine.Object, Action>();
        readonly List<GameObject> decorations = new List<GameObject>();
        readonly HashSet<UnityEngine.Object> inventoryElements = new HashSet<UnityEngine.Object>();
        MonoBehaviour inventory;
        Sprite panelSprite;
        static Plugin instance;
        Harmony hooks;

        void Awake()
        {
            instance = this;
            panelSprite = MakePanelSprite();
            LoadDesignAssets();
            hooks = new Harmony("local.sunkenland.reefui");
            InitializeEventStyling();
            Logger.LogInfo("Reef UI 0.9.1 loaded. Authored nautical UI; event-driven styling, no polling or theme toggle.");
        }

        static readonly Dictionary<Type,Dictionary<string,FieldInfo>> fieldCache=new Dictionary<Type,Dictionary<string,FieldInfo>>();
        static FieldInfo FindField(Type type,string name)
        {
            if(!fieldCache.TryGetValue(type,out var fields)) fieldCache[type]=fields=new Dictionary<string,FieldInfo>();
            if(fields.TryGetValue(name,out var field)) return field;
            for(var t=type;t!=null;t=t.BaseType)
            {
                field=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(field!=null) break;
            }
            fields[name]=field;return field;
        }
        static T Field<T>(object obj, string name) where T : class
        {
            if(obj==null) return null;
            return FindField(obj.GetType(),name)?.GetValue(obj) as T;
        }

        void Remember(UnityEngine.Object key, Action restore)
        { if (!undo.ContainsKey(key)) undo.Add(key, restore); }

        void ImageStyle(Image image, Color color)
        {
            if (!image) return;
            var oldColor = image.color; var oldSprite = image.sprite; var oldType = image.type;
            var overrideField=FindField(typeof(Image),"m_OverrideSprite");
            var oldOverride=overrideField!=null ? overrideField.GetValue(image) as Sprite : image.overrideSprite;
            float oldScale = image.pixelsPerUnitMultiplier;
            Remember(image, () => { if (image) { image.color = oldColor; image.sprite = oldSprite; image.overrideSprite=oldOverride; image.type = oldType; image.pixelsPerUnitMultiplier=oldScale; } });
            image.overrideSprite=null;
            image.sprite = surfaceSpriteV7?surfaceSpriteV7:panelSprite; image.type = Image.Type.Sliced;
            image.color = surfaceSpriteV7?MaterialTint(color):color; image.pixelsPerUnitMultiplier=1;
        }

        void ButtonStyle(Button b, bool craft = false, bool panel = false)
        {
            if (!b) return;
            var old = b.colors;
            var target = b.targetGraphic;
            var transition=b.transition;
            Remember(b, () => { if (b) { b.colors = old; b.targetGraphic = target;b.transition=transition; } });
            if(target && target.name.StartsWith("ReefUI")) b.transition=Selectable.Transition.ColorTint;
            // A few stock buttons animate a text graphic. Do not make hidden text opaque via a color transition.
            if (target is TMP_Text || target is Text) return;
            // Generic button graphics may be category icons: preserve those sprites.
            if (panel || craft) ImageStyle(b.targetGraphic as Image, craft ? Gold : Navy);
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f,1.25f,1.25f,1);
            colors.pressedColor = new Color(.82f,.82f,.82f,1);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(.3f, .4f, .43f, .55f);
            colors.colorMultiplier = 1; colors.fadeDuration = .12f; b.colors = colors;
        }

        void Border(RectTransform target, Color color)
        {
            // Separation comes from spacing and surface tone, not a line around each control.
        }

        void LabelStyle(TMP_Text text)
        {
            if (!text || text.name.StartsWith("ReefUI")) return;
            // Keep quantity colors: green/red communicate affordability and durability.
            string n = text.name.ToLowerInvariant();
            if (n.Contains("amount") || n.Contains("count") || n.Contains("condition")) return;
            Color c = text.color;
            // Dark body text may be printed on a bright paper/art surface; keep its contrast.
            if (c.r < .35f) return;
            if (Mathf.Abs(c.r - c.g) > .12f || Mathf.Abs(c.g - c.b) > .12f) return;
            TextColor(text, White);
        }

        void StyleInventoryElement(MonoBehaviour element)
        {
            string type = element.GetType().Name;
            if (type == "UIInventory") { inventory = element; return; }
            if (type == "UISlotInventoryStorage" || type == "UIEquipmentSlot" || type == "UISlotSpecificTypeItem" || type == "UIQuickSlot")
            {
                if (!inventoryElements.Add(element)) return;
                var slot = Field<Button>(element, "btnSlot");
                StyleSlotSurface(element, slot);
                ButtonStyle(slot);
                if (slot) ApplyButtonPlate(slot, false, false);
            }
            else if (type == "UICraftableInfoDisplayInventory")
            {
                if (!inventoryElements.Add(element)) return;
                DecorateSurface(element.transform as RectTransform);
                var craft = Field<Button>(element, "btnCraft");
                if (craft) { DecorateButton(craft, true); AddCraftLabel(craft); }
                // Retain the shipped nested layout: guessed relative anchors compressed names and item art.
            }
        }

        void AddCraftLabel(Button button)
        {
            if (!button) return;
            if (button.transform.Find("ReefUI craft label"))
            { LayoutCraftContentsV81(button); return; }
            // Fit inside the original hit area so neighboring controls remain unobscured.
            var go = new GameObject("ReefUI craft label", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            go.transform.SetParent(button.transform, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6, 4); rt.offsetMax = new Vector2(-6, -4);
            var t = go.GetComponent<TextMeshProUGUI>();
            var source = Field<TMP_Text>(inventory, "OriginTxt");
            if (source && source.font) t.font = source.font;
            else if (builderCaptionFont) t.font = builderCaptionFont;
            t.text = "CRAFT"; t.fontSize = 14; t.enableAutoSizing = true;
            t.fontSizeMin = 9; t.fontSizeMax = 14; t.alignment = TextAlignmentOptions.Center;
            t.characterSpacing = 0; t.enableWordWrapping = false;
            t.color = Gold; t.raycastTarget = false;
            decorations.Add(go);
            LayoutCraftContentsV81(button);
            RegisterButtonLabels(button,true);
        }

        Sprite MakePanelSprite()
        {
            const int side=64;const float radius=12;
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false);
            texture.name = "ReefUI soft rounded surface";
            texture.wrapMode=TextureWrapMode.Clamp;
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    float px=Mathf.Abs(x+.5f-side*.5f)-(side*.5f-radius);
                    float py=Mathf.Abs(y+.5f-side*.5f)-(side*.5f-radius);
                    float outside=new Vector2(Mathf.Max(px,0),Mathf.Max(py,0)).magnitude+Mathf.Min(Mathf.Max(px,py),0)-radius;
                    float alpha=Mathf.Clamp01(.5f-outside);
                    texture.SetPixel(x,y,new Color(1,1,1,alpha));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, side, side), new Vector2(.5f, .5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(radius+1,radius+1,radius+1,radius+1));
        }

        void Restore()
        {
            // Move original controls out of mod containers before removing those containers.
            foreach (var action in rectUndo.Values) action();
            rectUndo.Clear();
            foreach (var action in undo.Values) action();
            foreach (var listener in visibilityUndo) listener.Restore();
            visibilityUndo.Clear();
            foreach(var pair in layoutDrivers) if(pair.Key) pair.Key.enabled=pair.Value;
            layoutDrivers.Clear();
            foreach (var go in decorations) if (go) Destroy(go);
            undo.Clear(); decorations.Clear(); inventoryElements.Clear();
            globalStyled.Clear(); screensStyled.Clear(); globalLabels.Clear();
            buttonLabels.Clear(); legacyButtonLabels.Clear();
            labelsByButton.Clear();
            ResetScreenTreatments();
            ResetBuilderPolish();
            ResetMenusV7(); ResetHudV7(); ResetInventoryV7();
            ResetDialogsV8();
            ResetOriginSelectionV91();
        }

        void OnDestroy()
        {
            Logger.LogInfo("Reef UI component destroyed.");
            ShutdownEventStyling();
            if (hooks != null) hooks.UnpatchSelf();
            instance = null;
            Restore();
            DisposeDesignAssets();
            if (panelSprite) { var texture = panelSprite.texture; Destroy(panelSprite); Destroy(texture); }
        }
    }
}
