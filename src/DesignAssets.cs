using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        Sprite surfaceSpriteV7, frameSpriteV7;
        readonly Dictionary<RectTransform, Image> authoredSurfaces = new Dictionary<RectTransform, Image>();
        readonly Dictionary<RectTransform, ReefSurfaceLink> authoredSurfaceLinks = new Dictionary<RectTransform, ReefSurfaceLink>();
        readonly Dictionary<Button, Image> buttonAccents = new Dictionary<Button, Image>();
        readonly Dictionary<Button, Image> buttonWashes = new Dictionary<Button, Image>();
        readonly Dictionary<Button, int> buttonStates = new Dictionary<Button, int>();

        void LoadDesignAssets()
        {
            RectTransform.reapplyDrivenProperties+=DrivenSurfaceLayout;
            var directory=Path.Combine(Path.GetDirectoryName(Info.Location), "Assets");
            surfaceSpriteV7=LoadDesignSprite(Path.Combine(directory,"reef-surface-v7.png"));
            frameSpriteV7=LoadDesignSprite(Path.Combine(directory,"reef-panel-v7.png"));
        }

        Sprite LoadDesignSprite(string path)
        {
            if(!File.Exists(path)) { Logger.LogWarning("UI artwork missing: "+path); return null; }
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),true)) { Destroy(texture);return null; }
            texture.name=Path.GetFileNameWithoutExtension(path);
            texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
            float border=Mathf.Min(texture.width,texture.height)*.12f;
            // Image.preferredWidth/Height use sprite PPU, not the rendering
            // multiplier. Keep the same 18.8-unit rendered border without
            // forcing content-fitted chips to the artwork's 301-unit minimum.
            return Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),800,0,
                SpriteMeshType.FullRect,new Vector4(border,border,border,border));
        }

        static Color MaterialTint(Color flatColor)
        {
            if(flatColor.a<.01f) return Color.clear;
            // The bitmap carries the material color. Multiplying it by the old
            // dark flat fill would erase its grain and make every surface black.
            if(Mathf.Max(flatColor.r,Mathf.Max(flatColor.g,flatColor.b))<.35f)
                return new Color(.92f,.97f,1,flatColor.a);
            if(flatColor.r>flatColor.b*1.35f && flatColor.r>.45f)
                return new Color(1.25f,1.08f,.78f,flatColor.a);
            return flatColor;
        }

        internal Image DecorateSurface(RectTransform target, bool framed=false, bool primary=false)
        {
            if(!target) return null;
            if(!authoredSurfaces.TryGetValue(target,out var surface) || !surface)
            {
                var own=target.GetComponent<Image>();
                if(own && own.type!=Image.Type.Filled && !own.GetComponent<Mask>()) surface=own;
                else if(target.GetComponent<Graphic>())
                {
                    // A TMP root draws before its children. A sibling backing
                    // keeps the existing glyphs visible and the same hit target.
                    if(!target.parent) return null;
                    surface=CreateSurfaceImage("ReefUI authored backing",target.parent);
                    var link=target.gameObject.AddComponent<ReefSurfaceLink>();
                    link.Initialize(surface.rectTransform);
                    authoredSurfaceLinks[target]=link;
                }
                else
                {
                    foreach(Transform child in target)
                        if(child.name=="ReefUI plate" && child.GetComponent<Image>()) { surface=child.GetComponent<Image>();break; }
                    if(!surface) surface=CreateSurfaceImage("ReefUI authored surface",target);
                    surface.gameObject.SetActive(true);surface.enabled=true;
                    var rect=surface.rectTransform;
                    rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                    rect.SetAsFirstSibling();
                }
                authoredSurfaces[target]=surface;
            }
            var originalColor=surface.color;
            // Earlier generic treatments may have left a filled child plate.
            // Retire it when the native Image itself now owns the material.
            foreach(Transform child in target)
                if(child.name=="ReefUI plate" && child!=surface.transform) child.gameObject.SetActive(false);
            ImageStyle(surface,new Color(1,1,1,Mathf.Max(.92f,originalColor.a)));
            surface.sprite=(framed && frameSpriteV7)?frameSpriteV7:(surfaceSpriteV7?surfaceSpriteV7:panelSprite);
            surface.pixelsPerUnitMultiplier=1;
            surface.color=primary?new Color(1.18f,1.05f,.84f,.98f):new Color(.94f,.98f,1,.96f);
            return surface;
        }

        Image CreateSurfaceImage(string name, Transform parent)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(LayoutElement),typeof(Image));
            go.transform.SetParent(parent,false);go.GetComponent<LayoutElement>().ignoreLayout=true;
            var image=go.GetComponent<Image>();image.raycastTarget=false;
            decorations.Add(go);return image;
        }

        internal void DecorateButton(Button button, bool primary=false)
        {
            if(!button) return;
            foreach(Transform child in button.transform)
                if(child.name=="ReefUI plate") child.gameObject.SetActive(false);
            var surface=DecorateSurface(button.transform as RectTransform,false,primary);
            foreach(Transform child in button.transform)
            {
                // Stock menu buttons use full-size state backings above their
                // root Image. Keep icon and text children, retire those brushes.
                if(child.name!="Nomal" && child.name!="Normal" && child.name!="select") continue;
                var oldState=child.GetComponent<Image>();
                var rect=child as RectTransform;var parent=button.transform as RectTransform;
                if(oldState && rect && parent && rect.rect.width>=parent.rect.width*.85f && rect.rect.height>=parent.rect.height*.75f)
                    ImageStyle(oldState,Color.clear);
            }
            if(surface) { button.targetGraphic=surface;button.transition=Selectable.Transition.ColorTint; }
            ButtonStyle(button);
            if(surface && !buttonAccents.ContainsKey(button))
            {
                var wash=CreateSurfaceImage("ReefUI warm selection",surface.transform);
                wash.transform.SetAsFirstSibling();wash.sprite=panelSprite;wash.type=Image.Type.Sliced;
                var washRect=wash.rectTransform;washRect.anchorMin=Vector2.zero;washRect.anchorMax=Vector2.one;
                washRect.offsetMin=new Vector2(3,3);washRect.offsetMax=new Vector2(-3,-3);
                wash.color=new Color(Gold.r,Gold.g,Gold.b,primary?.14f:0);
                buttonWashes[button]=wash;
                var accent=CreateSurfaceImage("ReefUI selection rail",surface.transform);
                accent.sprite=panelSprite;accent.type=Image.Type.Sliced;
                var rt=accent.rectTransform;
                rt.anchorMin=new Vector2(0,.22f);rt.anchorMax=new Vector2(0,.78f);
                rt.pivot=new Vector2(0,.5f);rt.sizeDelta=new Vector2(3,0);rt.anchoredPosition=new Vector2(7,0);
                accent.color=new Color(Gold.r,Gold.g,Gold.b,primary?.65f:0);
                buttonAccents[button]=accent;
            }
            RegisterButtonLabels(button,primary);
            SyncDecoratedSurface(button.transform as RectTransform);
        }

        void DrivenSurfaceLayout(RectTransform source)
        {
            if(source && authoredSurfaceLinks.TryGetValue(source,out var link) && link) link.Synchronize();
        }

        internal void SyncDecoratedSurface(RectTransform source)
        { DrivenSurfaceLayout(source); }

        int ButtonState(Button button)
        {
            if(buttonStates.TryGetValue(button,out int state)) return state;
            // Read once at registration; subsequent changes arrive from the
            // native state event, including keyboard/controller navigation.
            var property=typeof(Selectable).GetProperty("currentSelectionState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            state=property==null?0:Convert.ToInt32(property.GetValue(button,null));
            buttonStates[button]=state;return state;
        }

        void SetButtonState(Button button,int state)
        {
            if(!labelsByButton.ContainsKey(button)) return;
            buttonStates[button]=state;UpdateButtonLabels(button);
        }

        void PruneDesignAssets()
        {
            var dead=new List<RectTransform>();
            foreach(var pair in authoredSurfaces) if(!pair.Key || !pair.Value) dead.Add(pair.Key);
            foreach(var key in dead)
            {
                var surface=authoredSurfaces[key];
                if(surface && surface.name.StartsWith("ReefUI")) { decorations.Remove(surface.gameObject);Destroy(surface.gameObject); }
                authoredSurfaces.Remove(key);
                authoredSurfaceLinks.Remove(key);
            }
            var buttons=new List<Button>();foreach(var key in buttonStates.Keys) if(!key) buttons.Add(key);
            foreach(var key in buttons) { buttonStates.Remove(key);buttonAccents.Remove(key);buttonWashes.Remove(key); }
        }

        void DisposeDesignAssets()
        {
            RectTransform.reapplyDrivenProperties-=DrivenSurfaceLayout;
            foreach(var target in authoredSurfaces.Keys)
                if(target) foreach(var link in target.GetComponents<ReefSurfaceLink>()) if(link) Destroy(link);
            authoredSurfaces.Clear();authoredSurfaceLinks.Clear();buttonAccents.Clear();buttonWashes.Clear();buttonStates.Clear();
            foreach(var sprite in new[]{surfaceSpriteV7,frameSpriteV7})
                if(sprite) { var texture=sprite.texture;Destroy(sprite);Destroy(texture); }
        }
    }

    // A text-root control's backing is its sibling, so lifecycle and layout
    // callbacks mirror only that control. There is deliberately no Update.
    public sealed class ReefSurfaceLink : MonoBehaviour
    {
        RectTransform backing;
        RectTransform source;
        public void Initialize(RectTransform target) { source=transform as RectTransform;backing=target;Synchronize(); }
        void OnEnable() { Synchronize(); }
        void OnDisable() { if(backing) backing.gameObject.SetActive(false); }
        void OnDestroy() { if(backing) Destroy(backing.gameObject); }
        void OnRectTransformDimensionsChange() { Synchronize(); }
        void OnTransformParentChanged() { Synchronize(); }
        public void Synchronize()
        {
            if(!source || !backing || !source.parent) return;
            if(backing.parent!=source.parent) backing.SetParent(source.parent,false);
            if(backing.anchorMin!=source.anchorMin) backing.anchorMin=source.anchorMin;
            if(backing.anchorMax!=source.anchorMax) backing.anchorMax=source.anchorMax;
            if(backing.pivot!=source.pivot) backing.pivot=source.pivot;
            if(backing.sizeDelta!=source.sizeDelta) backing.sizeDelta=source.sizeDelta;
            if(backing.anchoredPosition!=source.anchoredPosition) backing.anchoredPosition=source.anchoredPosition;
            if(backing.localScale!=source.localScale) backing.localScale=source.localScale;
            if(backing.localRotation!=source.localRotation) backing.localRotation=source.localRotation;
            int index=source.GetSiblingIndex();if(backing.GetSiblingIndex()<index) index--;
            if(backing.GetSiblingIndex()!=index) backing.SetSiblingIndex(Mathf.Max(0,index));
            bool visible=isActiveAndEnabled;
            if(backing.gameObject.activeSelf!=visible) backing.gameObject.SetActive(visible);
        }
    }
}
