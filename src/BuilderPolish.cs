using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        sealed class BuilderCaption
        {
            public RectTransform Surface;
            public TMP_Text Label;
            public RawImage Icon;
            public Vector2 IconMin, IconMax;
            public bool CanInsetIcon;
        }

        sealed class BuilderGridStyle
        {
            public Vector2 Cell, Spacing;
            public GridLayoutGroup.Constraint Constraint;
            public int Columns;
        }

        sealed class BuilderCategoryVisual
        {
            public Image Surface;
            public TMP_Text Label;
            public Graphic Icon;
        }

        readonly Dictionary<MonoBehaviour, BuilderCaption> builderCaptions = new Dictionary<MonoBehaviour, BuilderCaption>();
        readonly Dictionary<GridLayoutGroup, BuilderGridStyle> builderGrids = new Dictionary<GridLayoutGroup, BuilderGridStyle>();
        readonly Dictionary<Button, ReefBuilderHome> builderHomeIcons = new Dictionary<Button, ReefBuilderHome>();
        readonly Dictionary<Button, BuilderCategoryVisual> builderCategories = new Dictionary<Button, BuilderCategoryVisual>();
        readonly HashSet<Button> builderHomeListeners = new HashSet<Button>();
        readonly HashSet<Image> builderAvailabilityImages = new HashSet<Image>();
        readonly HashSet<MonoBehaviour> buildingRecipeV8Screens = new HashSet<MonoBehaviour>();
        readonly HashSet<Image> ingredientV8Backings = new HashSet<Image>();
        readonly Dictionary<Type, PropertyInfo> builderNameProperties = new Dictionary<Type, PropertyInfo>();
        readonly List<MonoBehaviour> deadBuilderCards = new List<MonoBehaviour>();
        readonly List<GridLayoutGroup> deadBuilderGrids = new List<GridLayoutGroup>();
        readonly List<Button> deadBuilderButtons = new List<Button>();
        TMP_FontAsset builderCaptionFont;

        // UIBuilding owns every direct child of StructuresLayoutRoot and also
        // supplies native padding cards. Keep those card objects intact.
        // Decorations belong INSIDE a card/category button, never in its holder.
        internal void PolishBuilder(MonoBehaviour screen)
        {
            if (!screen || IsOurObject(screen.transform)) return;
            if (screen.GetType().Name == "UICraftableInfoDisplayBuilding")
            {
                StyleBuildingRecipeV8(screen);
                return;
            }
            if (screen.GetType().Name == "UIBuilding")
            {
                var source = Field<TMP_Text>(screen, "tmpTipName");
                if (source && source.font) builderCaptionFont = source.font;
                PruneBuilderPolish();
                PolishBuilderGrid(ScreenFieldRect(screen, "StructuresLayoutRoot"));
                OrganizeBuildingCardsV9(screen);
                PolishBuilderHome(screen);
                DecorateSurface(ScreenFieldRect(screen, "StructuresPart"), true);
                DecorateSurface(ScreenFieldRect(screen, "Tips"), true);
                DecorateSurface(ScreenFieldRect(screen, "OtherPart"), true);
                StyleDemolitionNoticeV8(Field<Toggle>(screen, "ToggleDemolitionNotice"));
                return;
            }
            if (screen.GetType().Name != "StructuresIcon") return;

            // Initiate(null) represents a padding cell. Do not invent a name or
            // make the padding visible; assigned cards can be pooled and reused.
            var piece = Field<Component>(screen, "M_BuildingPiece");
            if (!piece)
            {
                if (builderCaptions.TryGetValue(screen, out var empty) && empty.Surface && empty.Surface.gameObject.activeSelf)
                    empty.Surface.gameObject.SetActive(false);
                return;
            }

            var root = screen.transform as RectTransform;
            if (!root || !screen.gameObject.activeInHierarchy || root.rect.width < 40 || root.rect.height < 50) return;
            string displayName = BuilderDisplayName(piece);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                if (builderCaptions.TryGetValue(screen, out var empty) && empty.Surface && empty.Surface.gameObject.activeSelf)
                    empty.Surface.gameObject.SetActive(false);
                return;
            }
            var button = Field<Button>(screen, "myButton");

            // The actual item picture is the separate RawImage icon field.
            // The Button's Image is a backing; do not replace model textures.
            var backing = button ? button.GetComponent<Image>() : screen.GetComponent<Image>();
            if (backing) DecorateSurface(backing.rectTransform);

            // CantPart's active state is driven by Refresh's material check.
            // Keep its red warning and state, but reduce the full-card red wash.
            var cantPart = Field<GameObject>(screen, "CantPart");
            var warning = cantPart ? cantPart.GetComponent<Image>() : null;
            if (warning && !builderAvailabilityImages.Contains(warning) &&
                warning.type != Image.Type.Filled && warning.color.a > .01f &&
                warning.color.r > warning.color.g + .12f && warning.color.r > warning.color.b + .12f)
            {
                builderAvailabilityImages.Add(warning);
                TintImage(warning, new Color(.62f, .26f, .14f, Mathf.Min(warning.color.a, .17f)));
            }

            if (!builderCaptions.TryGetValue(screen, out var caption) || !caption.Surface)
            {
                caption = CreateBuilderCaption(screen, root);
                builderCaptions[screen] = caption;
            }
            if (!caption.Surface || !caption.Label) return;

            float captionHeight = Mathf.Clamp(root.rect.height * .31f, 25, 34);
            var captionSize = new Vector2(-10, captionHeight);
            if (caption.Surface.sizeDelta != captionSize) caption.Surface.sizeDelta = captionSize;
            if (!caption.Surface.gameObject.activeSelf) caption.Surface.gameObject.SetActive(true);
            if (caption.Label.text != displayName) caption.Label.text = displayName;
            if (builderCaptionFont && caption.Label.font != builderCaptionFont) caption.Label.font = builderCaptionFont;
            var captionColor = new Color(.92f, .90f, .82f, 1f);
            if (caption.Label.color != captionColor) caption.Label.color = captionColor;
            if (button) RegisterButtonLabels(button, false);

            // Only the exact icon field, when it is a direct child of this card,
            // receives a pixel inset. Reuse saved offsets; never compound them
            // or guess the anchors of a nested stock layout.
            if (caption.CanInsetIcon && caption.Icon)
            {
                var iconRect = caption.Icon.rectTransform;
                var min = caption.IconMin + new Vector2(0, captionHeight + 3);
                var max = caption.IconMax + new Vector2(0, -BuildingGroupInsetV9(screen));
                if (iconRect.offsetMin != min) iconRect.offsetMin = min;
                if (iconRect.offsetMax != max) iconRect.offsetMax = max;
            }
        }

        // These are the game's non-structure craft rows (food, furniture and
        // other building categories). Their native Refresh only updates data,
        // requirements and availability; it does not recolor these backings.
        internal void StyleBuildingRecipeV8(MonoBehaviour screen)
        {
            if (!screen || screen.GetType().Name != "UICraftableInfoDisplayBuilding" ||
                !buildingRecipeV8Screens.Add(screen)) return;
            var root = screen.transform as RectTransform;
            var itemArt = Field<RawImage>(screen, "rawImageIcon");
            var graphic = root ? root.GetComponent<Graphic>() : null;
            // A recipe's backing Image may change, while its RawImage remains
            // the actual item picture. Never create a sibling in a managed list.
            if (root && (!graphic || graphic is Image) && (!itemArt || itemArt.transform != root))
                DecorateSurface(root);
            TextColor(Field<TMP_Text>(screen, "tmpItemName"), White);
            StyleRequiredItemBackingsV8(screen);

            var craft = Field<Button>(screen, "btnCraft");
            if (!craft) return;
            bool hasLabel = false, hasChildIcon = false;
            foreach (var label in craft.GetComponentsInChildren<TMP_Text>(true))
                if (!string.IsNullOrWhiteSpace(label.text)) { hasLabel = true; break; }
            foreach (var label in craft.GetComponentsInChildren<Text>(true))
                if (!string.IsNullOrWhiteSpace(label.text)) { hasLabel = true; break; }
            var craftRect = craft.transform as RectTransform;
            foreach (var image in craft.GetComponentsInChildren<Image>(true))
            {
                if (!image || image.transform == craft.transform || !image.sprite || IsOurObject(image.transform)) continue;
                string name = image.name.ToLowerInvariant();
                if (name == "select" || name == "normal" || name == "nomal" || name == "background" || name == "bg") continue;
                var r = image.rectTransform.rect;
                if (craftRect && r.width < craftRect.rect.width * .9f && r.height < craftRect.rect.height * .9f)
                { hasChildIcon = true; break; }
            }
            DecorateButton(craft, true);
            // If the stock hammer was baked into the button's root Image, the
            // new material replaces it. Supply a clear action label in that case.
            if (!hasLabel && !hasChildIcon)
            {
                AddCraftLabel(craft);
                var fontSource = Field<TMP_Text>(screen, "tmpItemName");
                var label = craft.transform.Find("ReefUI craft label")?.GetComponent<TMP_Text>();
                if (label && fontSource && fontSource.font) label.font = fontSource.font;
            }
            LayoutCraftContentsV81(craft);
        }

        void StyleRequiredItemBackingsV8(MonoBehaviour recipe)
        {
            var requirements = Field<Array>(recipe, "requiredItemIcons");
            if (requirements == null) return;
            SpaceRequiredCostGroupV9(requirements);
            foreach (var value in requirements)
            {
                var required = value as MonoBehaviour;
                if (!required) continue;
                var mask = Field<Image>(required, "Mask");
                var plus = Field<Image>(required, "Plus");
                var button = Field<Button>(required, "button");
                var backing = required.GetComponent<Image>();
                if (!backing && button) backing = button.GetComponent<Image>();
                // Mask and Plus carry game-driven availability/craft semantics.
                // Texture and quantity colors remain native; the cost helper
                // changes presentation only, keeping the native hit area.
                if (backing && backing != mask && backing != plus &&
                    backing.type != Image.Type.Filled && !backing.GetComponent<Mask>())
                {
                    if (ingredientV8Backings.Add(backing)) DecorateSurface(backing.rectTransform);
                    LayoutRequiredCostV9(required, backing);
                }
                // Some requirement prefabs put the torn-paper backing one
                // level below the component instead of on its root Image.
                foreach (Transform child in required.transform)
                {
                    var image = child.GetComponent<Image>();
                    if (!image || image == backing || image == mask || image == plus || IsOurObject(child) ||
                        image.type == Image.Type.Filled || image.GetComponent<Mask>()) continue;
                    string name = image.name.ToLowerInvariant();
                    if (name != "background" && name != "bg" && name != "image") continue;
                    var r = image.rectTransform.rect; var color = image.color;
                    if (r.width >= 22 && r.width <= 130 && r.height >= 22 && r.height <= 130 &&
                        Mathf.Max(color.r, Mathf.Max(color.g, color.b)) < .35f &&
                        ingredientV8Backings.Add(image)) DecorateSurface(image.rectTransform);
                }
            }
        }

        void StyleDemolitionNoticeV8(Toggle toggle)
        {
            if (!toggle) return;
            var root = toggle.transform as RectTransform;
            if (root && root.rect.width >= 160 && root.rect.width <= 1000 &&
                root.rect.height >= 20 && root.rect.height <= 120)
                DecorateSurface(root);
            foreach (var image in toggle.GetComponentsInChildren<Image>(true))
            {
                if (!image || image == toggle.graphic || IsOurObject(image.transform) ||
                    image.type == Image.Type.Filled || image.GetComponent<Mask>()) continue;
                var r = image.rectTransform.rect;
                if (r.width >= 160 && r.width <= 1000 && r.height >= 20 && r.height <= 120)
                    DecorateSurface(image.rectTransform);
            }
            foreach (var label in toggle.GetComponentsInChildren<TMP_Text>(true))
                if (!IsOurObject(label.transform)) TextColor(label, White);
        }

        string BuilderDisplayName(Component piece)
        {
            var type = piece.GetType();
            if (!builderNameProperties.TryGetValue(type, out var property))
            {
                // This is the localized property used by UIBuilding.ShowTips.
                property = type.GetProperty("DisplayName", BindingFlags.Instance | BindingFlags.Public);
                builderNameProperties.Add(type, property);
            }
            if (property == null || property.PropertyType != typeof(string)) return null;
            try { return property.GetValue(piece, null) as string; }
            catch (TargetInvocationException) { return null; }
        }

        BuilderCaption CreateBuilderCaption(MonoBehaviour screen, RectTransform root)
        {
            var go = new GameObject("ReefUI builder caption", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(.5f, 0); rt.anchoredPosition = new Vector2(0, 3);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var image = go.GetComponent<Image>();
            image.sprite = panelSprite; image.type = Image.Type.Sliced;
            image.color = new Color(.026f, .040f, .047f, .96f); image.raycastTarget = false;

            var labelObject = new GameObject("ReefUI building name", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rt, false);
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4, 2); labelRect.offsetMax = new Vector2(-4, -2);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            if (builderCaptionFont) label.font = builderCaptionFont;
            label.fontSize = 13; label.enableAutoSizing = true;
            label.fontSizeMin = 11; label.fontSizeMax = 13;
            label.fontStyle = FontStyles.Normal; label.characterSpacing = 0;
            label.enableWordWrapping = true; label.maxVisibleLines = 2;
            label.alignment = TextAlignmentOptions.Center;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false; label.richText = false;
            decorations.Add(go);

            var icon = Field<RawImage>(screen, "icon");
            var caption = new BuilderCaption { Surface = rt, Label = label, Icon = icon };
            if (icon && icon.transform != root && icon.transform.parent == root && icon.rectTransform.rect.height >= 72)
            {
                SaveRect(icon.rectTransform);
                caption.IconMin = icon.rectTransform.offsetMin;
                caption.IconMax = icon.rectTransform.offsetMax;
                caption.CanInsetIcon = true;
            }
            return caption;
        }

        void PolishBuilderGrid(RectTransform holder)
        {
            var grid = holder ? holder.GetComponent<GridLayoutGroup>() : null;
            if (!grid || holder.childCount == 0 || !holder.gameObject.activeInHierarchy || holder.rect.width<=0 ||
                grid.cellSize.x<65 || grid.cellSize.y<72) return;
            // The shipped code pads material families to eleven cells per row.
            // A different future layout should retain its own geometry.
            if (grid.constraint == GridLayoutGroup.Constraint.FixedRowCount ||
                (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount && grid.constraintCount != 11)) return;
            foreach (Transform child in holder)
            {
                bool isStructure = false;
                foreach (var component in child.GetComponents<MonoBehaviour>())
                    if (component && component.GetType().Name == "StructuresIcon") { isStructure = true; break; }
                if (!isStructure) return;
            }
            if (!builderGrids.TryGetValue(grid, out var original))
            {
                original = new BuilderGridStyle { Cell = grid.cellSize, Spacing = grid.spacing,Constraint=grid.constraint,Columns=grid.constraintCount };
                builderGrids.Add(grid, original);
                var savedCell = original.Cell; var savedSpacing = original.Spacing;
                var savedConstraint=original.Constraint;var savedColumns=original.Columns;
                Remember(grid, () => { if (grid) { grid.cellSize = savedCell; grid.spacing = savedSpacing;grid.constraint=savedConstraint;grid.constraintCount=savedColumns; } });
            }
            int rows = Mathf.Max(1, Mathf.CeilToInt(holder.childCount / 11f));
            float gapX = Mathf.Max(original.Spacing.x, 6);
            float gapY = Mathf.Max(original.Spacing.y, 8);
            float occupiedWidth = original.Cell.x * 11 + original.Spacing.x * 10;
            float occupiedHeight = original.Cell.y * rows + original.Spacing.y * (rows - 1);
            float width = (occupiedWidth - gapX * 10) / 11;
            float height = (occupiedHeight - gapY * (rows - 1)) / rows;
            if (width < 65 || height < 72) return;
            if (grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount) grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;
            if (grid.constraintCount != 11) grid.constraintCount=11;
            // Existing total bounds, content height and eleven-column groups
            // stay the same; separation comes from smaller cards, not overflow.
            var cells = new Vector2(width, height); var spacing = new Vector2(gapX, gapY);
            if (grid.cellSize != cells) grid.cellSize = cells;
            if (grid.spacing != spacing) grid.spacing = spacing;
        }

        void PolishBuilderHome(MonoBehaviour screen)
        {
            var group = Field<MonoBehaviour>(screen, "S_ButtonGroup");
            PolishCategoryGroup(group);
        }

        void PolishCategoryGroup(MonoBehaviour group)
        {
            var buttons = group ? Field<List<Button>>(group, "Buttons") : null;
            if (buttons == null) return;
            foreach (var button in buttons)
            {
                if (!button) continue;
                if (!builderCategories.TryGetValue(button, out var visual) || !visual.Surface)
                {
                    // The category is one native hit target. Copy its actual
                    // sprite above the new backing instead of tinting the icon
                    // as though it were a background.
                    var source = CategoryIconSource(button);
                    var go = new GameObject("ReefUI structures category", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
                    var rt = (RectTransform)go.transform; rt.SetParent(button.transform, false);
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                    rt.offsetMin = new Vector2(2, 2); rt.offsetMax = new Vector2(-2, -2);
                    go.GetComponent<LayoutElement>().ignoreLayout = true;
                    var backing = go.GetComponent<Image>();
                    ImageStyle(backing, Navy); backing.raycastTarget = false;
                    Graphic icon = null;
                    bool home = button.name == "S_Structures";
                    if (home)
                    {
                        var iconObject = new GameObject("ReefUI house", typeof(RectTransform), typeof(ReefBuilderHome));
                        var iconRect = (RectTransform)iconObject.transform; iconRect.SetParent(rt, false);
                        iconRect.anchorMin = new Vector2(.22f, .34f); iconRect.anchorMax = new Vector2(.78f, .91f);
                        iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
                        var glyph = iconObject.GetComponent<ReefBuilderHome>(); glyph.raycastTarget = false;
                        builderHomeIcons[button] = glyph; icon = glyph;
                    }
                    else if (source && source.sprite)
                    {
                        var iconObject = new GameObject("ReefUI category icon", typeof(RectTransform), typeof(Image));
                        var iconRect = (RectTransform)iconObject.transform; iconRect.SetParent(rt, false);
                        iconRect.anchorMin = new Vector2(.20f, .34f); iconRect.anchorMax = new Vector2(.80f, .91f);
                        iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
                        var copy = iconObject.GetComponent<Image>();
                        copy.sprite = source.sprite; copy.preserveAspect = true;
                        copy.color = White; copy.raycastTarget = false; icon = copy;
                    }
                    var label = CreateCategoryName(rt, PrettyCategoryName(button.name));
                    visual = new BuilderCategoryVisual { Surface = backing, Label = label, Icon = icon };
                    builderCategories[button] = visual; decorations.Add(go);
                    // This only changes the native button's target Graphic.
                    // Click listeners, hit bounds and navigation remain native.
                    button.targetGraphic = backing; ButtonStyle(button);
                    RegisterButtonLabels(button, false);
                }
                if (builderHomeListeners.Add(button))
                {
                    // Native category listeners are installed before this screen
                    // treatment. Refresh only when the button is actually used.
                    var observedButton = button;
                    var observedGroup = group;
                    UnityAction listener = () => RefreshBuilderHomeState(observedGroup);
                    button.onClick.AddListener(listener);
                    AddVisibilityUndo(observedButton, () => { if (observedButton) observedButton.onClick.RemoveListener(listener); });
                }
            }
            RefreshBuilderHomeState(group);
        }

        internal void RefreshBuilderHomeState(MonoBehaviour group)
        {
            // The ButtonGroup selection postfix also calls this after any other
            // category is selected, including keyboard/programmatic navigation.
            var buttons = group ? Field<List<Button>>(group, "Buttons") : null;
            if (buttons == null) return;
            foreach (var button in buttons)
            {
                if (!button || !builderCategories.TryGetValue(button, out var visual) || !visual.Surface) continue;
                var selected = button.transform.Find("select");
                bool isSelected = selected && selected.gameObject.activeSelf;
                var color = isSelected ? Gold : White;
                if (visual.Icon && visual.Icon.color != color) visual.Icon.color = color;
                if (visual.Label && buttonLabels.TryGetValue(visual.Label,out var binding)) binding.Color=color;
                UpdateButtonLabels(button);
                var surfaceColor = isSelected ? new Color(1.24f,1.06f,.76f,1) : Color.white;
                if (visual.Surface.color != surfaceColor) visual.Surface.color = surfaceColor;
            }
        }

        static Image CategoryIconSource(Button button)
        {
            Image fallback = button.GetComponent<Image>();
            foreach (var image in button.GetComponentsInChildren<Image>(true))
            {
                if (!image || !image.sprite || IsOurObject(image.transform)) continue;
                string name = image.name.ToLowerInvariant();
                if (name == "select" || name.Contains("background") || name == "bg") continue;
                if (image.transform != button.transform) return image;
                fallback = image;
            }
            return fallback;
        }

        TMP_Text CreateCategoryName(RectTransform root, string name)
        {
            var go = new GameObject("ReefUI category name", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform; rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1, .32f);
            rt.offsetMin = new Vector2(2, 2); rt.offsetMax = new Vector2(-2, -1);
            var label = go.GetComponent<TextMeshProUGUI>();
            if (builderCaptionFont) label.font = builderCaptionFont;
            label.text = name; label.fontSize = 10.5f; label.enableAutoSizing = true;
            label.fontSizeMin = 8; label.fontSizeMax = 10.5f;
            label.fontStyle = FontStyles.Normal; label.characterSpacing = 0;
            label.enableWordWrapping = false; label.alignment = TextAlignmentOptions.Center;
            label.overflowMode = TextOverflowModes.Ellipsis; label.raycastTarget = false;
            label.color = White; label.richText = false;
            return label;
        }

        static string PrettyCategoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (name.StartsWith("S_", StringComparison.Ordinal)) name = name.Substring(2);
            name = name.Replace("_", " ").Trim();
            // Shipped category objects include ordering prefixes such as
            // "0 Food" and "0 Furniture". Those numbers are not UI labels.
            int prefix = 0;
            while (prefix < name.Length && char.IsDigit(name[prefix])) prefix++;
            if (prefix > 0 && prefix < name.Length && char.IsWhiteSpace(name[prefix]))
                name = name.Substring(prefix).TrimStart();
            switch (name.ToLowerInvariant())
            {
                case "structures": return "Structures";
                case "materials": return "Materials";
                case "tools": return "Tools";
                case "clothes": return "Clothing";
                case "meds": return "Survival";
                case "weaponpart": return "Parts";
                case "weapons": return "Weapons";
            }
            // A native category object name is a fallback label only. Never
            // synthesize object names from their texture or array position.
            var readable = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1])) readable.Append(' ');
                readable.Append(c);
            }
            return readable.ToString();
        }

        internal void ResetBuilderPolish()
        {
            builderCaptions.Clear(); builderGrids.Clear(); builderHomeIcons.Clear(); builderCategories.Clear();
            builderHomeListeners.Clear(); deadBuilderCards.Clear(); deadBuilderGrids.Clear(); deadBuilderButtons.Clear();
            builderAvailabilityImages.Clear(); builderNameProperties.Clear(); builderCaptionFont = null;
            buildingRecipeV8Screens.Clear(); ingredientV8Backings.Clear();
            ResetBuildingOrganizationV9();
        }

        void PruneBuilderPolish()
        {
            deadBuilderCards.Clear();
            foreach (var pair in builderCaptions) if (!pair.Key || !pair.Value.Surface) deadBuilderCards.Add(pair.Key);
            foreach (var card in deadBuilderCards) builderCaptions.Remove(card);
            deadBuilderCards.Clear();
            deadBuilderGrids.Clear();
            foreach (var pair in builderGrids) if (!pair.Key) deadBuilderGrids.Add(pair.Key);
            foreach (var grid in deadBuilderGrids) builderGrids.Remove(grid);
            deadBuilderGrids.Clear();
            deadBuilderButtons.Clear();
            foreach (var pair in builderHomeIcons) if (!pair.Key || !pair.Value) deadBuilderButtons.Add(pair.Key);
            foreach (var button in deadBuilderButtons) builderHomeIcons.Remove(button);
            deadBuilderButtons.Clear();
            foreach (var pair in builderCategories) if (!pair.Key || !pair.Value.Surface) deadBuilderButtons.Add(pair.Key);
            foreach (var button in deadBuilderButtons) builderCategories.Remove(button);
            deadBuilderButtons.Clear();
            builderHomeListeners.RemoveWhere(button => !button);
            builderAvailabilityImages.RemoveWhere(image => !image);
            buildingRecipeV8Screens.RemoveWhere(screen => !screen);
            ingredientV8Backings.RemoveWhere(image => !image);
            PruneBuildingOrganizationV9();
        }
    }

    // A restrained line icon, drawn at the actual category button resolution.
    public sealed class ReefBuilderHome : MaskableGraphic
    {
        static readonly Vector2[] Points = {
                new Vector2(-.9f, -.02f), new Vector2(0, .8f), new Vector2(.9f, -.02f),
                new Vector2(.64f, -.02f), new Vector2(.64f, -.76f),
                new Vector2(.19f, -.76f), new Vector2(.19f, -.2f),
                new Vector2(-.19f, -.2f), new Vector2(-.19f, -.76f),
                new Vector2(-.64f, -.76f), new Vector2(-.64f, -.02f), new Vector2(-.9f, -.02f) };

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var rect = rectTransform.rect;
            float s = Mathf.Min(rect.width, rect.height) * .5f;
            var center = rect.center;
            float width = Mathf.Max(1.5f, s * .09f);
            for (int i = 0; i < Points.Length - 1; i++)
                BuilderStroke(vh, center + Points[i] * s, center + Points[i + 1] * s, width);
        }

        void BuilderStroke(VertexHelper vh, Vector2 a, Vector2 b, float width)
        {
            var delta = (b - a).normalized; var normal = new Vector2(-delta.y, delta.x) * width * .5f;
            int first = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero); vh.AddVert(a + normal, color, Vector2.zero);
            vh.AddVert(b + normal, color, Vector2.zero); vh.AddVert(b - normal, color, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
