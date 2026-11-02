using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        void StyleSlotSurface(MonoBehaviour slot,Button button)
        {
            if(!slot) return;
            var allowed=new Color(.84f,.90f,.95f,.78f);
            var blocked=new Color(.44f,.50f,.55f,.40f);
            var originals=new Dictionary<FieldInfo,object>();
            foreach(var pair in new[] {new KeyValuePair<string,Color>("colorPutItemAllowed",allowed),
                new KeyValuePair<string,Color>("colorPutItemNotAllowed",blocked)})
            {
                var field=FindField(slot.GetType(),pair.Key);
                if(field==null || field.FieldType!=typeof(Color)) continue;
                originals[field]=field.GetValue(slot);field.SetValue(slot,pair.Value);
            }
            if(originals.Count>0) Remember(slot,()=>{
                if(!slot) return;
                foreach(var pair in originals) pair.Key.SetValue(slot,pair.Value);
            });
            var backing=Field<Image>(slot,"imgBackground");
            if(!backing && button) backing=button.GetComponent<Image>();
            if(!backing && button) backing=button.targetGraphic as Image;
            if(backing && !IsOurObject(backing.transform))
            {
                ImageStyle(backing,allowed);
                // Selectable's multiplicative normal tint stays neutral; the surface itself carries its alpha.
                backing.CrossFadeColor(Color.white,0,true,true);
            }
            var hover=Field<Image>(slot,"imgMouseHoverIndicator");
            if(hover) TintImage(hover,new Color(.87f,.72f,.43f,.14f));
        }
    }
}
