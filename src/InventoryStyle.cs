using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        readonly HashSet<MonoBehaviour> inventoryV7Screens = new HashSet<MonoBehaviour>();
        readonly HashSet<Image> inventoryCraftIcons = new HashSet<Image>();

        // Author the real inventory controls. None of these treatments change
        // recipe geometry, item art, quantities or native managed slot lists.
        internal void StyleInventoryV7(MonoBehaviour screen)
        {
            if (!screen || IsOurObject(screen.transform)) return;
            string type = screen.GetType().Name;
            if (type == "UICraftableInfoDisplayInventory")
            {
                if (!inventoryV7Screens.Add(screen)) return;
                DecorateSurface(screen.transform as RectTransform);
                StyleRequiredItemBackingsV8(screen);
                var craft = Field<Button>(screen, "btnCraft");
                if (craft)
                {
                    DecorateButton(craft, true);
                    LayoutCraftContentsV81(craft);
                }
                return;
            }
            if (type != "UIInventory" || !inventoryV7Screens.Add(screen)) return;

            var origin = Field<TMP_Text>(screen, "OriginTxt");
            var hobby = Field<TMP_Text>(screen, "HobbyTxt");
            if (origin && origin.font) builderCaptionFont = origin.font;
            var originGroup = ScreenFieldRect(screen, "originAndHobbyGroup");
            if (originGroup && originGroup.rect.width < 900 && originGroup.rect.height < 180)
                DecorateSurface(originGroup, true);
            StyleInventoryHeading(origin, Gold);
            StyleInventoryHeading(hobby, White);
            StyleIdentityChipV9(origin);
            StyleIdentityChipV9(hobby);
            if (originGroup)
            {
                var identityRow = originGroup.GetComponent<HorizontalLayoutGroup>();
                if (identityRow)
                {
                    SnapshotQuestLayoutV8(identityRow);
                    identityRow.padding = new RectOffset(6, 6, 0, 0);
                    identityRow.spacing = 12; identityRow.childAlignment = TextAnchor.MiddleCenter;
                    identityRow.childControlWidth = identityRow.childControlHeight = true;
                    identityRow.childForceExpandWidth = identityRow.childForceExpandHeight = false;
                }
            }
            PolishCategoryGroup(Field<MonoBehaviour>(screen, "CraftingCatogory_ButtonGroup"));

            // The retained inventory hierarchy confirms this toggle's own
            // Image is the wide black brush. The check and label are separate
            // children, so only the brush receives the shared material.
            var hideEquipment = Field<Toggle>(screen, "HideEquipments");
            if (hideEquipment)
            {
                var backing = hideEquipment.GetComponent<Image>();
                if (backing && backing.type != Image.Type.Filled)
                    DecorateSurface(backing.rectTransform);
                var label = hideEquipment.transform.Find("Label")?.GetComponent<TMP_Text>();
                if (label) TextColor(label, White);
            }

            // Only header strips that own these labels are replaced. A title's
            // rect or an item holder never receives new siblings or children.
            foreach (var label in screen.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!label || IsOurObject(label.transform)) continue;
                string labelName = label.name.ToLowerInvariant();
                string value = (label.text ?? "").Trim().ToLowerInvariant();
                bool header = value == "backpack" || value == "simple crafting" ||
                    labelName == "backpacktitle" || labelName == "simplecraftingtitle";
                if (header) StyleInventoryHeading(label, White);
            }

            foreach (string field in new[] { "btnSortBackpack", "btnSortChest", "btnTakeAllFromChest", "btnPutInSameKindItems", "btnPutInAllItems", "btnShowTips", "btnShowInventoryManagementRobotTips" })
            {
                var button = Field<Button>(screen, field);
                if (!button) continue;
                // These exact generic utility prefabs use their root Image as
                // a backing and a separate imgIcon child as the actual glyph.
                // Matching line glyphs keep sort/help legible on navy even if
                // the original artwork had black pixels baked into the icon.
                var icon = button.transform.Find("imgIcon")?.GetComponent<Image>();
                bool themedUtility = field == "btnSortBackpack" || field == "btnSortChest" ||
                    field == "btnShowTips" || field == "btnShowInventoryManagementRobotTips";
                if (themedUtility && icon && button.GetComponent<Image>())
                {
                    DecorateButton(button);
                    if (field == "btnSortBackpack" || field == "btnSortChest")
                        ReplaceHudGlyphV7(icon, HudGlyphKind.Sort, White);
                    else if (field == "btnShowTips" || field == "btnShowInventoryManagementRobotTips")
                        ReplaceHudGlyphV7(icon, HudGlyphKind.Help, White);
                    else TintImage(icon, White);
                    continue;
                }
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label && !string.IsNullOrWhiteSpace(label.text)) DecorateButton(button);
            }

            foreach (string field in new[] { "chestGroup", "SplitPenal", "DecomposeTableGroup", "newImportantBlueprintPanel", "InventoryManagementRobotPenal", "SquadPenal" })
            {
                var panel = ScreenFieldRect(screen, field);
                if (!panel || panel.rect.width < 100 || panel.rect.height < 60 || panel.rect.width > 1000 || panel.rect.height > 1000) continue;
                DecorateSurface(panel, true);
            }
            TextColor(Field<TMP_Text>(screen, "tmpNewBlueprintHint"), Gold);
            TextColor(Field<TMP_Text>(screen, "tmpNewBlueprintDesHint"), White);
        }

        // The shipped inventory dump shows a 69px hammer inside a 61px craft
        // button, with a full-size CRAFT label over the same space. Lay out only
        // those exact two graphics inside the existing button; the recipe's
        // HorizontalLayoutGroup, item name and ingredient geometry stay native.
        internal void LayoutCraftContentsV81(Button button)
        {
            if (!button) return;
            var rect = button.transform as RectTransform;
            if (!rect || rect.rect.width < 48 || rect.rect.width > 130 ||
                rect.rect.height < 44 || rect.rect.height > 130) return;
            var icon = button.transform.Find("imgIcon")?.GetComponent<Image>();
            if (icon && !IsOurObject(icon.transform))
            {
                var iconRect = icon.rectTransform;
                SaveRect(iconRect);
                if (inventoryCraftIcons.Add(icon))
                {
                    bool oldAspect = icon.preserveAspect;
                    AddVisibilityUndo(icon, () => { if (icon) icon.preserveAspect = oldAspect; });
                }
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f, .66f);
                iconRect.pivot = new Vector2(.5f, .5f);
                iconRect.anchoredPosition = Vector2.zero;
                float size = Mathf.Min(24, Mathf.Min(rect.rect.width, rect.rect.height) * .4f);
                iconRect.sizeDelta = new Vector2(size, size);
                icon.preserveAspect = true;
                TintImage(icon, White);
            }
            var label = button.transform.Find("ReefUI craft label")?.GetComponent<TMP_Text>();
            if (!label) return;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = icon ? new Vector2(1, .42f) : Vector2.one;
            labelRect.offsetMin = icon ? new Vector2(6, 2) : new Vector2(6, 4);
            labelRect.offsetMax = icon ? new Vector2(-6, -2) : new Vector2(-6, -4);
            label.margin = Vector4.zero;
            label.characterSpacing = 0;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center;
        }

        void StyleInventoryHeading(TMP_Text label, Color color)
        {
            if (!label) return;
            TextColor(label, color);
            label.fontStyle |= FontStyles.Bold;
            label.characterSpacing = 1.1f;
            var own = label.GetComponent<Image>();
            if (own && own.rectTransform.rect.height < 120) DecorateSurface(own.rectTransform);
            var parent = label.transform.parent ? label.transform.parent.GetComponent<Image>() : null;
            if (parent && parent.rectTransform.rect.width >= 100 && parent.rectTransform.rect.width <= 850 &&
                parent.rectTransform.rect.height >= 20 && parent.rectTransform.rect.height <= 120)
                DecorateSurface(parent.rectTransform);
        }

        void StyleIdentityChipV9(TMP_Text label)
        {
            if (!label || !label.transform.parent) return;
            var chip = label.transform.parent as RectTransform;
            var layout = chip ? chip.GetComponent<HorizontalLayoutGroup>() : null;
            if (!chip || !layout || !chip.GetComponent<Image>()) return;
            SaveRect(chip); SnapshotQuestLayoutV8(layout);
            layout.padding = new RectOffset(16, 16, 7, 7);
            layout.spacing = 0; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            float width = Mathf.Clamp(label.GetPreferredValues(label.text, float.PositiveInfinity, float.PositiveInfinity).x + 32, 140, 280);
            var box = QuestLayoutElementV8(chip, true);
            box.layoutPriority = 10; box.minWidth = box.preferredWidth = width;
            box.minHeight = box.preferredHeight = 44;
            box.flexibleWidth = box.flexibleHeight = 0;
            var textSize = QuestLayoutElementV8(label.rectTransform, true);
            textSize.minWidth = 0; textSize.preferredWidth = width - 32;
            textSize.minHeight = textSize.preferredHeight = 30; textSize.flexibleWidth = 1;
            bool auto = label.enableAutoSizing, wrap = label.enableWordWrapping;
            float min = label.fontSizeMin, max = label.fontSizeMax; var margin = label.margin; var overflow = label.overflowMode;
            AddVisibilityUndo(label, () => { if (label) { label.enableAutoSizing = auto; label.enableWordWrapping = wrap;
                label.fontSizeMin = min; label.fontSizeMax = max; label.margin = margin; label.overflowMode = overflow; } });
            label.enableAutoSizing = true; label.fontSizeMin = 16; label.fontSizeMax = 20;
            label.enableWordWrapping = false; label.margin = Vector4.zero; label.overflowMode = TextOverflowModes.Ellipsis;
            DecorateSurface(chip);
        }

        internal void ResetInventoryV7() { inventoryV7Screens.Clear(); inventoryCraftIcons.Clear(); }
        internal void PruneInventoryV7()
        {
            inventoryV7Screens.RemoveWhere(screen => !screen);
            inventoryCraftIcons.RemoveWhere(icon => !icon);
        }
    }
}
