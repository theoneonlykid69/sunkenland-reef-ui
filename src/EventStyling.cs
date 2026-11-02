using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ReefUI
{
    public sealed partial class Plugin
    {
        readonly HashSet<Transform> pendingRoots = new HashSet<Transform>();
        readonly HashSet<MonoBehaviour> pendingControllers = new HashSet<MonoBehaviour>();
        readonly List<Transform> coveredRoots = new List<Transform>();
        readonly HashSet<MethodBase> patchedMethods = new HashSet<MethodBase>();
        readonly HashSet<UnityEngine.Object> openedScreens = new HashSet<UnityEngine.Object>();
        Coroutine stylingBatch;
        bool shuttingDown;

        void InitializeEventStyling()
        {
            var uiChanged = new HarmonyMethod(typeof(Plugin), nameof(NativeUIChanged));
            var controlCreated = new HarmonyMethod(typeof(Plugin), nameof(NativeControlCreated));
            // Hooks run after the game has populated its existing controls. No
            // native Update/UpdateLogic/availability Refresh method is patched.
            Hook("UIMenu", uiChanged, "Awake", "Start", "Open");
            Hook("UIGameMenu", uiChanged, "Open");
            Hook("UICharacterSelection", uiChanged, "Open", "RefreshCharacterList");
            Hook("UICharacterCreation", uiChanged, "Open");
            Hook("UICharacterWorldCustomization", uiChanged, "Awake", "Open", "RefreshButtonsForSelection", "RefreshUIText");
            Hook("UIWorldSelection", uiChanged, "Open", "OpenWithoutRefresh", "RefreshWorldList");
            Hook("UIWorldCreation", uiChanged, "Open");
            Hook("UIWorldModification", uiChanged, "Open");
            Hook("UISettings", uiChanged, "Init", "Open");
            Hook("UIInventory", uiChanged, "Open");
            Hook("UIBuilding", uiChanged, "Start", "Open", "ChangeSpecificType");
            Hook("UILoadingScreen", uiChanged, "Open");
            Hook("UICombat", uiChanged, "Start", "Open");
            Hook("UICompass", uiChanged, "Start");
            Hook("StructuresIcon", uiChanged, "Initiate");
            Hook("UICraftableInfoDisplayInventory", controlCreated, "Init");
            Hook("UICraftableInfoDisplayBuilding", controlCreated, "Init");
            Hook("UISlotInventoryStorage", controlCreated, "Init");
            Hook("UIEquipmentSlot", controlCreated, "Init");
            Hook("UISlotSpecificTypeItem", controlCreated, "Init");
            Hook("UICharacterSelectionEntry", uiChanged, "Init");
            Hook("UIWorldSelectionEntry", uiChanged, "Init");
            Hook("LocalizedKeybindingButton", uiChanged, "Start");
            Hook("CurrentTrackingQuestLabel", uiChanged, "Init");
            Hook("_YaoXiang.UI.UITutorial", uiChanged, "Awake", "Start", "Open");
            Hook("UIConfirmation", uiChanged, "Awake", "Init");
            Hook("UIJoinServerConfirmation", uiChanged, "Awake", "Init");
            Hook("UINotification", uiChanged, "Awake", "Init");
            Hook("ButtonGroup", new HarmonyMethod(typeof(Plugin), nameof(NativeSelectionChanged)), "ClickOneButton");
            var transition = typeof(Selectable).GetMethod("DoStateTransition", BindingFlags.Instance | BindingFlags.NonPublic);
            if (transition != null)
                hooks.Patch(transition, postfix: new HarmonyMethod(typeof(Plugin), nameof(NativeSelectableStateChanged)));

            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) QueueScene(SceneManager.GetSceneAt(i));
            Logger.LogInfo("Registered " + patchedMethods.Count + " UI creation/open hooks; no recurring hierarchy scan.");
        }

        void Hook(string typeName, HarmonyMethod postfix, params string[] methodNames)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) return;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (Array.IndexOf(methodNames, method.Name) < 0 || !patchedMethods.Add(method)) continue;
                hooks.Patch(method, postfix: postfix);
            }
        }

        static void NativeUIChanged(MonoBehaviour __instance, MethodBase __originalMethod)
        {
            if (!instance || !__instance) return;
            // Open may be called repeatedly by the game even if the panel is
            // already visible. Only its first open needs a hierarchy treatment;
            // subsequent new children are handled by their own Init callbacks.
            string type=__instance.GetType().Name;
            if(type=="UIGameMenu" || type=="UITutorial" || type=="UIConfirmation" || type=="UIJoinServerConfirmation" || type=="UINotification" || type=="UICharacterWorldCustomization")
            { instance.QueueController(__instance);return; }
            if ((__originalMethod.Name == "Open" || __originalMethod.Name == "OpenWithoutRefresh") &&
                !instance.openedScreens.Add(__instance)) return;
            instance.QueueRoot(__instance.transform);
        }

        static void NativeControlCreated(MonoBehaviour __instance)
        {
            // Storage Init also runs for content changes. Palette fields and
            // surfaces persist, so already styled slots need no further work.
            if (instance && __instance && !instance.inventoryElements.Contains(__instance))
                instance.QueueRoot(__instance.transform);
        }

        static void NativeSelectableStateChanged(Selectable __instance, object __0)
        {
            // The game's state transition is the event; this only visits cached
            // labels of the affected button, never its hierarchy or other UI.
            if (instance && __instance is Button button) instance.SetButtonState(button,Convert.ToInt32(__0));
        }

        static void NativeSelectionChanged(MonoBehaviour __instance)
        {
            if (!instance || !__instance) return;
            var buttons = Field<List<Button>>(__instance, "Buttons");
            if (buttons != null) foreach (var button in buttons) instance.UpdateButtonLabels(button);
            instance.RefreshBuilderHomeState(__instance);
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            PruneAllStyles();
            openedScreens.RemoveWhere(screen => !screen);
            QueueScene(scene);
        }

        void SceneUnloaded(Scene scene)
        {
            pendingRoots.RemoveWhere(root => !root || root.gameObject.scene == scene);
            pendingControllers.RemoveWhere(controller=>!controller || controller.gameObject.scene==scene);
            PruneAllStyles();
            openedScreens.RemoveWhere(screen => !screen);
        }

        void QueueScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            // Discover canvases once per scene load, including initially hidden
            // screens. Instantiated widgets are handled by their native Init.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace) QueueRoot(canvas.transform);
        }

        void QueueRoot(Transform root)
        {
            if (shuttingDown || !root || IsOurObject(root) || IsJournal(root)) return;
            // Coalesce a burst of Init/Open callbacks into the smallest number
            // of roots. A queued parent already includes every queued child.
            coveredRoots.Clear();
            foreach (var pending in pendingRoots)
            {
                if (!pending) { coveredRoots.Add(pending); continue; }
                if (root == pending || root.IsChildOf(pending)) return;
                if (pending.IsChildOf(root)) coveredRoots.Add(pending);
            }
            foreach (var covered in coveredRoots) pendingRoots.Remove(covered);
            pendingRoots.Add(root);
            if (stylingBatch == null) stylingBatch = StartCoroutine(ApplyPendingStyles());
        }

        void QueueController(MonoBehaviour controller)
        {
            if(shuttingDown || !controller) return;
            pendingControllers.Add(controller);
            QueueRoot(controller.transform);
            if(stylingBatch==null) stylingBatch=StartCoroutine(ApplyPendingStyles());
        }

        IEnumerator ApplyPendingStyles()
        {
            // Let native Start/layout/parent assignment finish before treatment.
            yield return null;
            while ((pendingRoots.Count > 0 || pendingControllers.Count > 0) && !shuttingDown)
            {
                var batch = new List<Transform>(pendingRoots);
                var controllers=new List<MonoBehaviour>(pendingControllers);
                pendingRoots.Clear();
                pendingControllers.Clear();
                foreach (var root in batch)
                {
                    if (!root) continue;
                    try { StyleRoot(root); }
                    catch (Exception error) { Logger.LogError("Could not style UI root " + root.name + ": " + error); }
                }
                foreach(var controller in controllers)
                {
                    if(!controller) continue;
                    try { StyleMenusV7(controller); StyleDialogsV8(controller); StyleOriginSelectionV91(controller); }
                    catch(Exception error) { Logger.LogError("Could not style UI controller "+controller.GetType().Name+": "+error); }
                }
                // Item/category/list actions can replace cards without changing
                // scenes. Retire those references after the event batch too.
                PruneAllStyles();
                // Exit completely when idle: there is no timer or polling loop.
                if (pendingRoots.Count > 0 || pendingControllers.Count > 0) yield return null;
            }
            stylingBatch = null;
        }

        void ShutdownEventStyling()
        {
            shuttingDown = true;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            if (stylingBatch != null) StopCoroutine(stylingBatch);
            stylingBatch = null; pendingRoots.Clear(); pendingControllers.Clear(); coveredRoots.Clear(); patchedMethods.Clear(); openedScreens.Clear();
        }

        void PruneAllStyles()
        {
            PruneSceneCaches(); PruneScreenTreatments(); PruneBuilderPolish();
            PruneDesignAssets(); PruneMenusV7(); PruneHudV7(); PruneInventoryV7();
            PruneDialogsV8();
            PruneOriginSelectionV91();
        }
    }
}
