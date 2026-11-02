using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        readonly Dictionary<MonoBehaviour, RectTransform> confirmationDocksV8 = new Dictionary<MonoBehaviour, RectTransform>();
        readonly Dictionary<MonoBehaviour, RectTransform> notificationDocksV9 = new Dictionary<MonoBehaviour, RectTransform>();
        readonly HashSet<MonoBehaviour> tutorialScreensV8 = new HashSet<MonoBehaviour>();
        readonly HashSet<MonoBehaviour> joinScreensV8 = new HashSet<MonoBehaviour>();

        internal void StyleDialogsV8(MonoBehaviour screen)
        {
            if (!screen) return;
            switch (screen.GetType().Name)
            {
                case "UIConfirmation": StyleConfirmationV8(screen); break;
                case "UINotification": StyleNotificationV9(screen); break;
                case "UITutorial":
                    if (tutorialScreensV8.Add(screen)) StyleTutorialV8(screen);
                    break;
                case "UIJoinServerConfirmation":
                    if (joinScreensV8.Add(screen)) StyleJoinDialogV8(screen);
                    break;
            }
        }

        void StyleNotificationV9(MonoBehaviour screen)
        {
            // Inventory tips use Mainframe.ShowNotification, not UIMessage or
            // the two-action UIConfirmation. These are the actual prefab fields.
            var title = Field<TMP_Text>(screen, "tmpTitle");
            var content = Field<TMP_Text>(screen, "tmpContent");
            var confirm = Field<Button>(screen, "btnConfirm");
            var canvas = confirm ? confirm.GetComponentInParent<Canvas>() : null;
            var visualRoot = canvas ? canvas.transform as RectTransform : null;
            if (!content || !confirm || !visualRoot) return;
            if (!notificationDocksV9.TryGetValue(screen, out var dock) || !dock)
            {
                RetireNotificationBackingsV9(screen, canvas, content, confirm);
                var layout = Field<VerticalLayoutGroup>(screen, "verticalLayoutGroup");
                var fitter = Field<ContentSizeFitter>(screen, "contentSizeFitter");
                if (layout) FreezeMenuLayoutV8(layout.transform as RectTransform);
                if (fitter) FreezeMenuLayoutV8(fitter.transform as RectTransform);
                dock = NewContainer(visualRoot, "notification dock");
                dock.anchorMin = dock.anchorMax = new Vector2(.5f, .5f);
                dock.pivot = new Vector2(.5f, .5f); dock.anchoredPosition = Vector2.zero;
                DecorateSurface(dock, true);
                var link = screen.gameObject.AddComponent<ReefNativeScreenLinkV8>();
                link.Initialize(dock);
                AddVisibilityUndo(screen, () => { if (link) Destroy(link); });
                notificationDocksV9[screen] = dock;
            }
            const float width = 880;
            bool hasTitle = title && title.gameObject.activeSelf && !string.IsNullOrWhiteSpace(title.text);
            TextColor(content, White);
            // Measure the final body typography, including its paragraph/line
            // spacing. Measuring the old font then capping it at 500 could hide
            // the last instructions or force the whole help block down to 16px.
            content.fontStyle &= ~FontStyles.UpperCase;
            content.enableAutoSizing = false; content.fontSize = 21;
            content.enableWordWrapping = true; content.margin = Vector4.zero;
            content.lineSpacing = 5; content.overflowMode = TextOverflowModes.Overflow;
            float preferred = content.GetPreferredValues(content.text ?? "", width - 76, 0).y;
            float bodyHeight = Mathf.Clamp(preferred + 20, 90, 900);
            float bodyTop = hasTitle ? 86 : 30;
            float height = bodyTop + bodyHeight + 104;
            dock.sizeDelta = new Vector2(width, height);
            float scale = Mathf.Min(1, Mathf.Min(visualRoot.rect.width * .92f / width, visualRoot.rect.height * .88f / height));
            dock.localScale = new Vector3(Mathf.Max(.4f, scale), Mathf.Max(.4f, scale), 1);
            if (hasTitle) LayoutDialogTextV8(title, dock, -26, 42, width - 76, Gold, 27, TextAlignmentOptions.MidlineLeft);
            LayoutDialogTextV8(content, dock, -bodyTop, bodyHeight, width - 76, White, 21, TextAlignmentOptions.TopLeft, false, false);
            LayoutDialogButtonV8(confirm, dock, new Vector2(0, -height + 76), new Vector2(236, 52), true);
            SyncDecoratedSurface(dock);
            dock.SetAsLastSibling();
        }

        void RetireNotificationBackingsV9(MonoBehaviour screen, Canvas canvas, TMP_Text content, Button confirm)
        {
            // Mainframe instantiates the UINotification component's GameObject.
            // Its subtree is the notification's ownership boundary; inventory
            // and other host screens are ancestors and must never be touched.
            var scope = screen.transform;
            if (!content.transform.IsChildOf(scope) || !confirm.transform.IsChildOf(scope)) return;
            var fullCanvas = canvas && canvas.rootCanvas ? canvas.rootCanvas.transform as RectTransform : null;
            foreach (var image in scope.GetComponentsInChildren<Image>(true))
            {
                if (!image || image.GetComponentInParent<Button>() || image.type == Image.Type.Filled ||
                    image.GetComponent<Mask>() || image.GetComponent<RectMask2D>() || IsOurObject(image.transform)) continue;
                var rect = image.rectTransform.rect;
                if (rect.width < 120 || rect.height < 50) continue;
                // Preserve a genuine full-screen click-blocking dimmer. A wide
                // 1740px brush around the body is still retired: width alone is
                // not ownership or evidence of a full-screen overlay.
                if (fullCanvas && NotificationCoversCanvasV9(image.rectTransform, fullCanvas)) continue;
                ImageStyle(image, Color.clear);
            }
        }

        static bool NotificationCoversCanvasV9(RectTransform image, RectTransform canvas)
        {
            var corners = new Vector3[4]; image.GetWorldCorners(corners);
            var min = canvas.InverseTransformPoint(corners[0]); var max = min;
            for (int i = 1; i < corners.Length; i++)
            { var point = canvas.InverseTransformPoint(corners[i]); min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
            var rect = canvas.rect;
            float marginX = rect.width * .02f, marginY = rect.height * .02f;
            return min.x <= rect.xMin + marginX && max.x >= rect.xMax - marginX &&
                min.y <= rect.yMin + marginY && max.y >= rect.yMax - marginY;
        }

        void StyleConfirmationV8(MonoBehaviour screen)
        {
            var title = Field<TMP_Text>(screen, "tmpTitle");
            var message = Field<TMP_Text>(screen, "tmpMessage");
            var cancel = Field<Button>(screen, "btnCancel");
            var confirm = Field<Button>(screen, "btnConfirm");
            var canvas = confirm ? confirm.GetComponentInParent<Canvas>() : null;
            var visualRoot = canvas ? canvas.transform as RectTransform : null;
            if (!title || !message || !cancel || !confirm || !visualRoot) return;
            if (!confirmationDocksV8.TryGetValue(screen, out var dock) || !dock)
            {
                // The native Init supplies translated text and callbacks. Keep
                // those exact objects; only their presentation is rearranged.
                RetireDialogBackingsV8(new Component[] { title, message, cancel, confirm }, visualRoot);
                var layout = Field<VerticalLayoutGroup>(screen, "layoutGroup");
                if (layout) FreezeMenuLayoutV8(layout.transform as RectTransform);
                dock = NewContainer(visualRoot, "confirmation dock");
                dock.anchorMin = dock.anchorMax = new Vector2(.5f, .5f);
                dock.pivot = new Vector2(.5f, .5f); dock.anchoredPosition = Vector2.zero;
                DecorateSurface(dock, true);
                var link = screen.gameObject.AddComponent<ReefNativeScreenLinkV8>();
                link.Initialize(dock);
                AddVisibilityUndo(screen, () => { if (link) Destroy(link); });
                confirmationDocksV8[screen] = dock;
            }
            const float width = 660;
            float preferred = message.GetPreferredValues(message.text ?? "", width - 72, 0).y;
            float bodyHeight = Mathf.Clamp(preferred + 14, 70, 280);
            float height = 88 + bodyHeight + 96;
            dock.sizeDelta = new Vector2(width, height);
            float scale = Mathf.Min(1, Mathf.Min(visualRoot.rect.width * .9f / width, visualRoot.rect.height * .85f / height));
            dock.localScale = Vector3.one * Mathf.Max(.4f, scale);
            LayoutDialogTextV8(title, dock, -26, 42, width - 64, Gold, 27, TextAlignmentOptions.MidlineLeft);
            LayoutDialogTextV8(message, dock, -84, bodyHeight, width - 64, White, 21, TextAlignmentOptions.TopLeft);
            message.fontStyle &= ~FontStyles.UpperCase;
            message.enableWordWrapping = true; message.lineSpacing = 4;
            LayoutDialogButtonV8(cancel, dock, new Vector2(-153, -height + 74), new Vector2(290, 52), false);
            LayoutDialogButtonV8(confirm, dock, new Vector2(153, -height + 74), new Vector2(290, 52), true);
            SyncDecoratedSurface(dock);
            dock.SetAsLastSibling();
        }

        void LayoutDialogTextV8(TMP_Text text, RectTransform parent, float top, float height,
            float width, Color color, float size, TextAlignmentOptions alignment, bool autoSize = true, bool ellipsis = true)
        {
            var rect = text.rectTransform; SaveRect(rect); FreezeMenuLayoutV8(rect);
            if (rect.parent != parent) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, top); rect.sizeDelta = new Vector2(width, height); rect.localScale = Vector3.one;
            TextColor(text, color);
            text.alignment = alignment; text.enableAutoSizing = autoSize; text.fontSizeMin = size - 4; text.fontSizeMax = size;
            if (!autoSize) text.fontSize = size;
            text.margin = Vector4.zero; text.overflowMode = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            SyncDecoratedSurface(rect);
        }

        void LayoutDialogButtonV8(Button button, RectTransform parent, Vector2 position, Vector2 size, bool primary)
        {
            if (!button) return;
            var rect = button.transform as RectTransform; SaveRect(rect); FreezeMenuLayoutV8(rect);
            if (rect.parent != parent) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one;
            DecorateButton(button, primary); FitMenuButtonTextV8(button, primary); SyncDecoratedSurface(rect);
        }

        void RetireDialogBackingsV8(IEnumerable<Component> controls, RectTransform limit)
        {
            var images = new HashSet<Image>();
            foreach (var control in controls)
            {
                if (!control) continue;
                for (var parent = control.transform.parent; parent && parent != limit; parent = parent.parent)
                {
                    var image = parent.GetComponent<Image>();
                    if (image) images.Add(image);
                    FreezeMenuLayoutV8(parent as RectTransform);
                }
            }
            foreach (var image in images)
            {
                if (!image || image.type == Image.Type.Filled || image.GetComponent<Mask>() || IsOurObject(image.transform)) continue;
                var rect = image.rectTransform.rect;
                if (rect.width >= 220 && rect.width <= 1400 && rect.height >= 100 && rect.height <= 1000)
                    ImageStyle(image, Color.clear);
            }
        }

        void StyleTutorialV8(MonoBehaviour screen)
        {
            var group = Field<ToggleGroup>(screen, "toggleGroup");
            var panelRoot = ScreenFieldRect(screen, "panelRoot");
            var close = Field<Button>(screen, "btnClose");
            var title = Field<TMP_Text>(screen, "tmpPanelTitle");
            if (!group || !panelRoot || !close) { tutorialScreensV8.Remove(screen); return; }
            var canvas = group.GetComponentInParent<Canvas>();
            var common = TutorialCommonRootV8(group.transform, panelRoot, close.transform);
            if (!common || !canvas || common.rect.width < 400 || common.rect.height < 250)
            { tutorialScreensV8.Remove(screen); return; }
            // UITutorials is a deliberately offset full-screen coordinate root
            // (1920x1080 at y=-186.20 in the retained dump). Framing that root
            // produced a huge slab below the actual reading/navigation panels.
            // Frame only those two native panels, leaving the root transparent.
            RetireTutorialRootPlateV9(common);
            TutorialBackingV8(group.transform as RectTransform, common, "tutorial navigation");
            TutorialBackingV8(panelRoot, common, "tutorial reading panel");
            var toggles = new List<Toggle>();
            foreach (Transform child in group.transform)
            {
                var toggle = child.GetComponent<Toggle>();
                if (toggle) toggles.Add(toggle);
            }
            foreach (var toggle in toggles)
            {
                var rect = toggle.transform as RectTransform;
                var label = toggle.GetComponentInChildren<TMP_Text>(true);
                Image backing = toggle.GetComponent<Image>();
                if (backing && backing.GetComponent<Mask>()) backing = null;
                if (!backing)
                    foreach (var image in toggle.GetComponentsInChildren<Image>(true))
                    {
                        if (!image || image == toggle.graphic || image.GetComponent<Mask>() || IsOurObject(image.transform)) continue;
                        if (image.rectTransform.rect.width >= rect.rect.width * .75f && image.rectTransform.rect.height >= rect.rect.height * .65f)
                        { backing = image; break; }
                    }
                // A text-root toggle without a native backing must not acquire
                // a sibling in the indexed group. Its shared navigation plate
                // and a child selection accent provide the same visual states.
                StyleMenuTabV7(toggle, backing ? backing.rectTransform : null, label, backing);
                foreach (var image in toggle.GetComponentsInChildren<Image>(true))
                {
                    if (!image || image == backing || IsOurObject(image.transform) || image.type == Image.Type.Filled) continue;
                    if (image.rectTransform.rect.width >= rect.rect.width * .75f && image.rectTransform.rect.height >= rect.rect.height * .65f)
                        ImageStyle(image, Color.clear);
                }
                if (label)
                {
                    TextColor(label, toggle.isOn ? Gold : White);
                    label.fontSizeMax = 23; label.fontSizeMin = 16; label.enableAutoSizing = true;
                    label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
                    label.margin = new Vector4(14, 4, 14, 4);
                }
            }
            if (title)
            {
                TextColor(title, Gold); title.fontStyle |= FontStyles.Bold;
                title.fontSizeMax = 31; title.fontSizeMin = 22; title.enableAutoSizing = true;
            }
            foreach (Transform article in panelRoot)
            {
                // Body art, keyboard diagrams and links retain their assets.
                // Only exact neutral, wide root backings become quiet metal.
                var backing = article.GetComponent<Image>();
                if (backing && !backing.GetComponent<Mask>() && backing.color.a > .1f && IsNeutralTutorialColorV8(backing.color) &&
                    backing.rectTransform.rect.width > 450 && backing.rectTransform.rect.height > 220)
                    DecorateSurface(backing.rectTransform);
                foreach (var text in article.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (IsOurObject(text.transform) || text.GetComponentInParent<Selectable>() || !IsNeutralTutorialColorV8(text.color)) continue;
                    if (text.color.r > .4f || !TutorialLightBackingV8(text.transform, article)) TextColor(text, White);
                }
                foreach (var button in article.GetComponentsInChildren<Button>(true))
                {
                    var label = button.GetComponentInChildren<TMP_Text>(true);
                    if (label && !string.IsNullOrWhiteSpace(label.text)) MenuButtonV7(button);
                }
            }
            MenuButtonV7(close);
            SyncDecoratedSurface(close.transform as RectTransform);
        }

        void RetireTutorialRootPlateV9(RectTransform root)
        {
            if (!root) return;
            // No native tutorial art is removed. This only retires the exact
            // backing previously created by DecorateSurface on the root itself.
            if (authoredSurfaces.TryGetValue(root, out var surface) && surface && surface.name.StartsWith("ReefUI"))
                surface.gameObject.SetActive(false);
            foreach (Transform child in root)
                if ((child.name == "ReefUI authored surface" || child.name == "ReefUI plate") && child.GetComponent<Image>())
                    child.gameObject.SetActive(false);
        }

        void TutorialBackingV8(RectTransform holder, RectTransform common, string name)
        {
            if (!holder || !common || holder == common) return;
            var native = holder.GetComponent<Image>();
            if (native && native.type != Image.Type.Filled && !native.GetComponent<Mask>())
            { DecorateSurface(holder, true); return; }
            // A scroll/content holder often sits inside the actual black panel.
            // Restyle that exact ancestor so it cannot cover the new material.
            for (var parent = holder.parent; parent && parent != common; parent = parent.parent)
            {
                var image = parent.GetComponent<Image>();
                if (!image || image.GetComponent<Mask>() || image.type == Image.Type.Filled ||
                    image.color.a < .1f || !IsNeutralTutorialColorV8(image.color)) continue;
                var rect = image.rectTransform.rect;
                if (rect.width >= Mathf.Max(150, holder.rect.width * .8f) && rect.height >= 200)
                {
                    DecorateSurface(image.rectTransform, true);
                    return;
                }
            }
            if (holder.rect.width < 150 || holder.rect.height < 100) return;
            // A sibling is attached to the common visual root, never either
            // indexed native holder. It cannot shift tutorial page selection.
            var backdrop = NewContainer(common, name); backdrop.SetAsFirstSibling();
            var corners = new Vector3[4]; holder.GetWorldCorners(corners);
            var min = common.InverseTransformPoint(corners[0]); var max = min;
            for (int i = 1; i < corners.Length; i++)
            { var point = common.InverseTransformPoint(corners[i]); min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
            backdrop.anchorMin = backdrop.anchorMax = common.pivot; backdrop.pivot = new Vector2(.5f, .5f);
            backdrop.anchoredPosition = (min + max) * .5f; backdrop.sizeDelta = new Vector2(max.x - min.x + 16, max.y - min.y + 12);
            DecorateSurface(backdrop);
        }

        static RectTransform TutorialCommonRootV8(Transform navigation, Transform panels, Transform close)
        {
            for (var parent = navigation.parent; parent; parent = parent.parent)
                if (panels.IsChildOf(parent) && close.IsChildOf(parent)) return parent as RectTransform;
            return null;
        }

        static bool IsNeutralTutorialColorV8(Color color)
            => Mathf.Abs(color.r - color.g) < .14f && Mathf.Abs(color.g - color.b) < .14f;

        static bool TutorialLightBackingV8(Transform text, Transform article)
        {
            for (var parent = text.parent; parent; parent = parent.parent)
            {
                var image = parent.GetComponent<Image>();
                if (image && image.color.a > .5f && image.color.r > .7f && IsNeutralTutorialColorV8(image.color) && !image.name.StartsWith("ReefUI")) return true;
                if (parent == article) break;
            }
            return false;
        }

        void StyleJoinDialogV8(MonoBehaviour screen)
        {
            var cancel = Field<Button>(screen, "btnCancel");
            var join = Field<Button>(screen, "btnJoinServer");
            var title = Field<TMP_Text>(screen, "tmpServerName");
            var description = Field<TMP_Text>(screen, "tmpWorldDescription");
            if (!cancel || !join) { joinScreensV8.Remove(screen); return; }
            var root = title && description ? TutorialCommonRootV8(title.transform, description.transform, cancel.transform) : null;
            if (!root) root = CommonMenuParentV7(new List<Button> { cancel, join }, screen.transform);
            if (root) DecorateSurface(root, true);
            TextColor(title, Gold); TextColor(description, White);
            MenuButtonV7(cancel); MenuButtonV7(join, true);
            var password = Field<TMP_InputField>(screen, "tmpInputFieldPassword");
            if (password)
            {
                ImageStyle(password.targetGraphic as Image, Navy);
                TextColor(password.textComponent, White);
                if (password.placeholder is TMP_Text placeholder) TextColor(placeholder, new Color(.6f, .63f, .64f, 1));
            }
        }

        internal void PruneDialogsV8()
        {
            tutorialScreensV8.RemoveWhere(screen => !screen); joinScreensV8.RemoveWhere(screen => !screen);
            var dead = new List<MonoBehaviour>();
            foreach (var pair in confirmationDocksV8) if (!pair.Key || !pair.Value) dead.Add(pair.Key);
            foreach (var screen in dead) confirmationDocksV8.Remove(screen);
            dead.Clear();
            foreach (var pair in notificationDocksV9) if (!pair.Key || !pair.Value) dead.Add(pair.Key);
            foreach (var screen in dead) notificationDocksV9.Remove(screen);
        }

        internal void ResetDialogsV8()
        { confirmationDocksV8.Clear(); notificationDocksV9.Clear(); tutorialScreensV8.Clear(); joinScreensV8.Clear(); }
    }
}
