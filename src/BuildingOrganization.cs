using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        sealed class OrganizedBuilding
        {
            public MonoBehaviour Icon;
            public Component Piece;
            public int Group, Material, ID, OriginalIndex;
            public string Shape;
        }

        readonly Dictionary<Type, PropertyInfo> buildingSpaceProperties = new Dictionary<Type, PropertyInfo>();
        readonly Dictionary<MonoBehaviour, TMP_Text> buildingGroupBadges = new Dictionary<MonoBehaviour, TMP_Text>();
        readonly HashSet<RectTransform> organizedBuildingHolders = new HashSet<RectTransform>();
        readonly Dictionary<MonoBehaviour, ReefCostCardBinding> costCardBindings = new Dictionary<MonoBehaviour, ReefCostCardBinding>();
        readonly HashSet<HorizontalLayoutGroup> costSpacingGroups = new HashSet<HorizontalLayoutGroup>();

        // Native evidence: StructuresIcon.Build/OnPointerEnter use the card's
        // own M_BuildingPiece. UIBuilding pools cards by BuildingID, not sibling
        // index. Existing placeholders are moved with the cards, never replaced
        // by extra children that the native pooling loop could misinterpret.
        void OrganizeBuildingCardsV9(MonoBehaviour screen)
        {
            if (BuildingNumberV9(screen, "curSpecificType") != 0) return;
            var holder = ScreenFieldRect(screen, "StructuresLayoutRoot");
            var grid = holder ? holder.GetComponent<GridLayoutGroup>() : null;
            if (!holder || !grid || holder.childCount == 0) return;
            var cards = new List<OrganizedBuilding>();
            var empty = new List<Transform>();
            var originalOrder = new List<Transform>();
            foreach (Transform child in holder)
            {
                MonoBehaviour icon = null;
                foreach (var component in child.GetComponents<MonoBehaviour>())
                    if (component && component.GetType().Name == "StructuresIcon") { icon = component; break; }
                if (!icon) return;
                originalOrder.Add(child);
                var piece = Field<Component>(icon, "M_BuildingPiece");
                if (!piece)
                {
                    empty.Add(child);
                    var backing = child.GetComponent<Image>();
                    if (backing) TintImage(backing, Color.clear);
                    continue;
                }
                cards.Add(new OrganizedBuilding { Icon = icon, Piece = piece,
                    Group = BuildingGroupV9(piece), Shape = BuildingShapeKeyV9(piece.name),
                    Material = BuildingMaterialOrderV9(piece), ID = BuildingNumberV9(piece, "BuildingID"),
                    OriginalIndex = originalOrder.Count - 1 });
            }
            if (cards.Count == 0) return;
            if (organizedBuildingHolders.Add(holder))
            {
                var saved = originalOrder.ToArray();
                AddVisibilityUndo(holder, () => {
                    if (!holder) return;
                    for (int i = 0; i < saved.Length; i++)
                        if (saved[i] && saved[i].parent == holder) saved[i].SetSiblingIndex(i);
                });
            }
            cards.Sort((a, b) => {
                int order = a.Group.CompareTo(b.Group);
                if (order == 0) order = StringComparer.Ordinal.Compare(a.Shape, b.Shape);
                if (order == 0) order = a.Material.CompareTo(b.Material);
                if (order == 0) order = a.ID.CompareTo(b.ID);
                return order == 0 ? a.OriginalIndex.CompareTo(b.OriginalIndex) : order;
            });
            foreach (var card in cards)
                if (buildingGroupBadges.TryGetValue(card.Icon, out var old) && old && old.gameObject.activeSelf)
                    old.gameObject.SetActive(false);

            int columns = grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? grid.constraintCount : 11;
            columns = Mathf.Max(1, columns);
            int position = 0, usedEmpty = 0, lastGroup = -1;
            foreach (var card in cards)
            {
                if (card.Group != lastGroup)
                {
                    int gap = (columns - position % columns) % columns;
                    // Use only native padding that already exists. When a game
                    // version supplies too little padding, keep the next group
                    // contiguous rather than inserting non-native grid objects.
                    if (gap <= empty.Count - usedEmpty)
                        for (int i = 0; i < gap; i++) empty[usedEmpty++].SetSiblingIndex(position++);
                    ShowBuildingGroupBadgeV9(card.Icon, card.Group);
                    lastGroup = card.Group;
                }
                card.Icon.transform.SetSiblingIndex(position++);
            }
            while (usedEmpty < empty.Count) empty[usedEmpty++].SetSiblingIndex(position++);
        }

        int BuildingGroupV9(Component piece)
        {
            if (BuildingFlagV9(piece, "IsWallPaper")) return 7;
            if (BuildingFlagV9(piece, "IsFoundation")) return 0;
            var type = piece.GetType();
            if (!buildingSpaceProperties.TryGetValue(type, out var property))
            {
                property = type.GetProperty("NeededSpace", BindingFlags.Instance | BindingFlags.Public);
                buildingSpaceProperties[type] = property;
            }
            int space = 0;
            if (property != null)
                try { space = Convert.ToInt32(property.GetValue(piece, null)); }
                catch (TargetInvocationException) { }
            // Explicit native Roof space outranks the broader IsFloor flag.
            if (space == 15) return 5;
            if (BuildingFlagV9(piece, "IsFloor")) return 1;
            // Values are the shipped BuildingSpace enum, not translated labels.
            switch (space)
            {
                case 1: return 0; // Foundation
                case 10: return 1; // Floor
                case 5: return 2; // Wall, including door/window frames
                case 30: case 35: return 3; // Door / Window
                case 2: return 4; // Pillar
                case 20: case 25: return 6; // OuterStairs / InnerStairs
                case 36: case 37: return 7; // WallPaper / DOrWPaper
                default: return 8;
            }
        }

        static int BuildingNumberV9(object piece, string name)
        {
            var field = FindField(piece.GetType(), name);
            return field == null ? 0 : Convert.ToInt32(field.GetValue(piece));
        }

        static bool BuildingFlagV9(object piece, string name)
        {
            var field = FindField(piece.GetType(), name);
            return field != null && field.FieldType == typeof(bool) && (bool)field.GetValue(piece);
        }

        static int BuildingMaterialOrderV9(Component piece)
        {
            // Shipped BuildingMaterial: Metal=0, Wood=1, Glass=2,
            // Reinforced=3, Brick=4. Present wood first within each shape.
            int material = BuildingNumberV9(piece, "M_BuildingMaterial");
            return material == 1 ? 0 : material == 0 ? 1 : material;
        }

        static string BuildingShapeKeyV9(string prefabName)
        {
            // These are English INTERNAL prefab names verified in the shipped
            // Addressables catalog (e.g. E2_WoodenWallWithDoorSlot versus
            // F2_Metal Wall With Door). Localized DisplayName is never parsed.
            string name = (prefabName ?? "").Replace("(Clone)", "").Trim();
            int prefix = name.IndexOf('_');
            if (prefix > 0 && prefix <= 5)
            {
                bool orderingPrefix = true;
                for (int i = 0; i < prefix; i++) orderingPrefix &= char.IsLetterOrDigit(name[i]);
                if (orderingPrefix) name = name.Substring(prefix + 1);
            }
            name = name.ToLowerInvariant();
            foreach (string material in new[] { "reinforced", "wooden", "scrap metal", "metal", "glass", "brick" })
                name = name.Replace(material, "");
            name = name.Replace("slot", "").Replace("ledder", "ladder");
            var key = new StringBuilder();
            foreach (char c in name) if (char.IsLetterOrDigit(c)) key.Append(c);
            return key.ToString();
        }

        void ShowBuildingGroupBadgeV9(MonoBehaviour icon, int group)
        {
            if (!buildingGroupBadges.TryGetValue(icon, out var label) || !label)
            {
                var go = new GameObject("ReefUI building group", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
                var rect = (RectTransform)go.transform; rect.SetParent(icon.transform, false);
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(0, -3);
                rect.sizeDelta = new Vector2(-8, 16);
                go.GetComponent<LayoutElement>().ignoreLayout = true;
                label = go.GetComponent<TextMeshProUGUI>();
                if (builderCaptionFont) label.font = builderCaptionFont;
                label.fontSize = 10; label.enableAutoSizing = true;
                label.fontSizeMin = 8; label.fontSizeMax = 10;
                label.fontStyle = FontStyles.Bold; label.characterSpacing = .5f;
                label.enableWordWrapping = false; label.alignment = TextAlignmentOptions.Left;
                label.overflowMode = TextOverflowModes.Ellipsis; label.richText = false;
                label.color = Gold; label.raycastTarget = false;
                buildingGroupBadges[icon] = label; decorations.Add(go);
            }
            string[] names = { "FOUNDATIONS", "FLOORS", "WALLS", "DOORS & WINDOWS", "SUPPORTS", "ROOFS", "STAIRS", "FINISHES", "OTHER" };
            if (label.text != names[group]) label.text = names[group];
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
        }

        float BuildingGroupInsetV9(MonoBehaviour icon)
        {
            return buildingGroupBadges.TryGetValue(icon, out var label) && label && label.gameObject.activeSelf ? 19 : 2;
        }

        void SpaceRequiredCostGroupV9(Array requirements)
        {
            if (requirements.Length < 2) return;
            var first = requirements.GetValue(0) as MonoBehaviour;
            var parent = first && first.transform.parent ? first.transform.parent as RectTransform : null;
            var layout = parent ? parent.GetComponent<HorizontalLayoutGroup>() : null;
            if (!layout || costSpacingGroups.Contains(layout) || parent.rect.width <= 0 ||
                parent.childCount != requirements.Length) return;
            float cardsWidth = 0;
            foreach (var value in requirements)
            {
                var required = value as MonoBehaviour;
                var rect = required ? required.transform as RectTransform : null;
                if (!rect || rect.parent != parent || rect.rect.width <= 0) return;
                // Include native empty cells too. A later recipe may fill them,
                // and its full four-cost row must fit without moving CRAFT.
                cardsWidth += rect.rect.width;
            }
            costSpacingGroups.Add(layout);
            float budget = parent.rect.width - layout.padding.horizontal;
            float gap = cardsWidth + 10 * (requirements.Length - 1) <= budget + .1f ? 10 : 8;
            if (cardsWidth + gap * (requirements.Length - 1) > budget + .1f || layout.spacing >= gap) return;
            float original = layout.spacing;
            AddVisibilityUndo(layout, () => { if (layout) layout.spacing = original; });
            layout.spacing = gap;
        }

        void LayoutRequiredCostV9(MonoBehaviour required, Image backing)
        {
            if (costCardBindings.TryGetValue(required, out var known) && known)
            { known.RefreshVisibility(); return; }
            var source = Field<RawImage>(required, "Icon");
            var count = Field<TMP_Text>(required, "CountTxt");
            var button = Field<Button>(required, "button");
            var root = required.transform as RectTransform;
            if (!source || !count || !button || !backing || !root || root.rect.width < 42 || root.rect.height < 45 ||
                root.rect.width > 150 || root.rect.height > 150) return;
            // Native Icon is the RawImage on the existing button in these
            // prefabs. Its full rect remains the original click/hover area.
            if (source.transform != button.transform) return;
            var inherited = source.transform.Find("ReefUI cost item picture")?.GetComponent<RawImage>();
            var go = inherited ? inherited.gameObject :
                new GameObject("ReefUI cost item picture", typeof(RectTransform), typeof(LayoutElement), typeof(RawImage));
            var iconRect = (RectTransform)go.transform;
            if (iconRect.parent != source.transform) iconRect.SetParent(source.transform, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f, .65f);
            iconRect.pivot = new Vector2(.5f, .5f);
            float size = Mathf.Min(36, Mathf.Min(root.rect.width - 16, root.rect.height * .50f));
            iconRect.sizeDelta = new Vector2(size, size); iconRect.anchoredPosition = Vector2.zero;
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var picture = go.GetComponent<RawImage>(); picture.raycastTarget = false;
            if (!inherited) picture.color = source.color;
            SaveRect(count.rectTransform);
            var oldSize = count.fontSize; var oldMin = count.fontSizeMin; var oldMax = count.fontSizeMax;
            bool oldAuto = count.enableAutoSizing, oldWrap = count.enableWordWrapping;
            var oldMargin = count.margin; float oldSpacing = count.characterSpacing;
            AddVisibilityUndo(count, () => {
                if (!count) return;
                count.fontSize = oldSize; count.fontSizeMin = oldMin; count.fontSizeMax = oldMax;
                count.enableAutoSizing = oldAuto; count.enableWordWrapping = oldWrap;
                count.margin = oldMargin; count.characterSpacing = oldSpacing;
            });
            count.rectTransform.anchorMin = Vector2.zero; count.rectTransform.anchorMax = new Vector2(1, .34f);
            count.rectTransform.offsetMin = new Vector2(3, 3); count.rectTransform.offsetMax = new Vector2(-3, -2);
            count.fontSize = 17; count.fontSizeMin = 11; count.fontSizeMax = 17;
            count.enableAutoSizing = true; count.enableWordWrapping = false;
            count.margin = Vector4.zero; count.characterSpacing = 0;
            // Color stays native: red/green still communicate affordability.
            var binding = count.GetComponent<ReefCostCardBinding>();
            if (!binding) binding = count.gameObject.AddComponent<ReefCostCardBinding>();
            binding.Initialize(source, picture, count, button, backing);
            costCardBindings[required] = binding; decorations.Add(go);
            AddVisibilityUndo(required, () => { if (binding) { binding.Restore(); Destroy(binding); } });
        }

        void ResetBuildingOrganizationV9()
        {
            buildingSpaceProperties.Clear(); buildingGroupBadges.Clear();
            organizedBuildingHolders.Clear(); costCardBindings.Clear(); costSpacingGroups.Clear();
        }

        void PruneBuildingOrganizationV9()
        {
            var dead = new List<MonoBehaviour>();
            foreach (var pair in buildingGroupBadges) if (!pair.Key || !pair.Value) dead.Add(pair.Key);
            foreach (var key in dead) buildingGroupBadges.Remove(key);
            dead.Clear();
            foreach (var pair in costCardBindings) if (!pair.Key || !pair.Value) dead.Add(pair.Key);
            foreach (var key in dead) costCardBindings.Remove(key);
            organizedBuildingHolders.RemoveWhere(holder => !holder);
            costSpacingGroups.RemoveWhere(layout => !layout);
        }
    }

    // One source texture callback and native CountTxt lifecycle events only.
    // No Update, LateUpdate, scan or styling queue is involved.
    public sealed class ReefCostCardBinding : MonoBehaviour
    {
        RawImage source, picture;
        TMP_Text count;
        Button button;
        Image backing;
        Color originalColor;
        bool originalBackingEnabled, restored;

        public void Initialize(RawImage nativeSource, RawImage insetPicture, TMP_Text nativeCount, Button nativeButton, Image nativeBacking)
        {
            source = nativeSource; picture = insetPicture; count = nativeCount;
            button = nativeButton; backing = nativeBacking;
            originalColor = source.color; originalBackingEnabled = backing.enabled;
            // A scene template can be cloned after it was styled. Its copied
            // picture retains the original tint even though its native source
            // was already transparent and private binding references reset.
            if (originalColor.a < .01f && picture.color.a > .01f) originalColor = picture.color;
            // Native RequiredItemIcon reads/writes Icon.texture, not Icon.color.
            // Keep the original graphic raycastable at full size while the
            // inset picture renders its texture once in the same native button.
            source.color = new Color(originalColor.r, originalColor.g, originalColor.b, 0);
            source.RegisterDirtyMaterialCallback(SyncTexture);
            SyncTexture(); RefreshVisibility();
        }

        void SyncTexture()
        {
            if (!source || !picture || restored) return;
            if (picture.texture != source.texture) picture.texture = source.texture;
            if (picture.uvRect != source.uvRect) picture.uvRect = source.uvRect;
        }

        public void RefreshVisibility()
        {
            if (!backing || !count || !button || restored) return;
            // InitEmpty hides the native button AND count object. A visible red
            // count means a real unaffordable ingredient, never an empty slot.
            bool visible = count.gameObject.activeSelf && button.gameObject.activeSelf;
            if (backing.enabled != visible) backing.enabled = visible;
        }

        void OnEnable() { SyncTexture(); RefreshVisibility(); }
        void OnDisable() { RefreshVisibility(); }
        void OnDestroy() { Restore(); }

        public void Restore()
        {
            if (restored) return;
            restored = true;
            if (source) { source.UnregisterDirtyMaterialCallback(SyncTexture); source.color = originalColor; }
            if (backing) backing.enabled = originalBackingEnabled;
        }
    }
}
