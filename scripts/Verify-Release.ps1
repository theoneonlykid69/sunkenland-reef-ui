param(
    [string]$GameDir = $env:SUNKENLAND_DIR,
    [string]$BepInExDir,
    [string]$ModPath = "$PSScriptRoot\..\src\bin\Release\netstandard2.1\Sunkenland.ReefUI.dll",
    [string]$SourceDir = "$PSScriptRoot\..\src"
)
$ErrorActionPreference = 'Stop'
if (!$GameDir) { throw 'Pass -GameDir with your own Sunkenland installation, or set SUNKENLAND_DIR.' }
if (!$BepInExDir) { $BepInExDir = Join-Path $GameDir 'BepInEx' }
[xml]$sourceProject = Get-Content -LiteralPath (Join-Path $SourceDir 'ReefUI.csproj') -Raw
$expectedVersion = $sourceProject.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($expectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part project release version.' }
Add-Type -LiteralPath (Join-Path $BepInExDir 'core\Mono.Cecil.dll')
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModPath)
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sunkenland_Data\Managed\Assembly-CSharp.dll'))
try {
    $allTypes = [System.Collections.Generic.List[Mono.Cecil.TypeDefinition]]::new()
    function Add-Types($types) {
        foreach ($type in $types) { $allTypes.Add($type); Add-Types $type.NestedTypes }
    }
    Add-Types $mod.MainModule.Types
    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($type in $allTypes) {
        foreach ($method in $type.Methods) {
            if ($method.Name -in @('Update','LateUpdate','FixedUpdate','OnGUI')) {
                $problems.Add("Recurring Unity callback: $($method.FullName)")
            }
            if (!$method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                if ($instruction.Operand -isnot [Mono.Cecil.MethodReference]) { continue }
                $target = $instruction.Operand
                if ($target.Name -in @('FindObjectsOfTypeAll','FindObjectsOfType','FindObjectOfType','InvokeRepeating','GetKeyDown') -or
                    $target.DeclaringType.FullName -eq 'UnityEngine.WaitForSeconds' -or
                    $target.DeclaringType.FullName -eq 'UnityEngine.WaitForSecondsRealtime') {
                    $problems.Add("Polling/toggle reference: $($method.FullName) -> $($target.FullName)")
                }
            }
        }
    }
    $plugin = $allTypes | Where-Object FullName -eq 'ReefUI.Plugin'
    $attribute = $plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInPlugin' }
    if ($attribute.ConstructorArguments[2].Value -ne $expectedVersion -or $mod.Name.Version.ToString() -ne "$expectedVersion.0") {
        $problems.Add("Plugin and assembly versions must match project release $expectedVersion.")
    }
    # Validate hook names against the shipped binary without loading the game.
    $source = [System.IO.File]::ReadAllText((Join-Path $SourceDir 'EventStyling.cs'))
    $hookCount = 0
    $registeredHooks = [System.Collections.Generic.HashSet[string]]::new()
    $unavailable = [System.Collections.Generic.List[string]]::new()
    foreach ($line in ($source -split '\r?\n')) {
        if ($line -notmatch '^\s*Hook\("([^"]+)"') { continue }
        $typeName = $Matches[1]
        $type = $game.MainModule.Types | Where-Object { $_.Name -eq $typeName -or $_.FullName -eq $typeName }
        $quoted = [regex]::Matches($line, '"([^"]+)"')
        foreach ($quotedName in ($quoted | Select-Object -Skip 1)) {
            $methodName = $quotedName.Groups[1].Value
            [void]$registeredHooks.Add("$typeName.$methodName")
            $methods = @($type.Methods | Where-Object { $_.Name -eq $methodName -and !$_.IsStatic })
            if ($methods.Count -eq 0) { $unavailable.Add("$typeName.$methodName (optional or inherited)") }
            else { $hookCount += $methods.Count }
            if ($methodName -in @('Update','LateUpdate','UpdateLogic','Refresh')) {
                $problems.Add("Recurring native hook: $typeName.$methodName")
            }
        }
    }
    foreach ($required in @('UIMenu.Open','UIGameMenu.Open','UIInventory.Open','UIBuilding.Open','UIBuilding.ChangeSpecificType','UILoadingScreen.Open',
        'UISettings.Open','UISlotInventoryStorage.Init','UIEquipmentSlot.Init','UISlotSpecificTypeItem.Init',
        'UICraftableInfoDisplayInventory.Init','StructuresIcon.Initiate','ButtonGroup.ClickOneButton','CurrentTrackingQuestLabel.Init',
        'UICraftableInfoDisplayBuilding.Init','UIConfirmation.Init','_YaoXiang.UI.UITutorial.Start','UINotification.Init',
        'UICharacterWorldCustomization.Open','UICharacterWorldCustomization.RefreshButtonsForSelection','UICharacterWorldCustomization.RefreshUIText')) {
        $separator = $required.LastIndexOf('.')
        $requiredType = $required.Substring(0, $separator)
        $requiredMethod = $required.Substring($separator + 1)
        if (!$registeredHooks.Contains($required)) { $problems.Add("Required hook is not registered: $required") }
        $type = $game.MainModule.Types | Where-Object { $_.Name -eq $requiredType -or $_.FullName -eq $requiredType }
        if (!($type.Methods | Where-Object Name -eq $requiredMethod)) { $problems.Add("Required native method missing: $required") }
    }
    $requiredFields = @('UIInventory.OriginTxt','UIInventory.HobbyTxt','UIInventory.originAndHobbyGroup',
        'UIInventory.HideEquipments','UINotification.tmpTitle','UINotification.tmpContent','UINotification.btnConfirm',
        'UINotification.verticalLayoutGroup','UINotification.contentSizeFitter',
        'CurrentTrackingQuestLabel.tmpQuestName','CurrentTrackingQuestLabel.imgQuestIcon','CurrentTrackingQuestLabel.btnClearCurrentTrackingQuest',
        'RequiredItemIcon.Icon','RequiredItemIcon.CountTxt','RequiredItemIcon.button',
        'UIBuilding.StructuresLayoutRoot','UIBuilding.curSpecificType','StructuresIcon.M_BuildingPiece',
        'UICharacterWorldCustomization.panelSelectionRootRectTransform','UICharacterWorldCustomization.togglesRoot',
        'UICharacterWorldCustomization.tmpSelectionTitle','UICharacterWorldCustomization.tmpSelectionDescription',
        'UICharacterWorldCustomization.optionToggles','UICharacterWorldCustomization.btnBack','UICharacterWorldCustomization.btnConfirm',
        'UICharacterWorldCustomization.tmpPageNumber','UICharacterWorldCustomization._currentSelectionType',
        'UICharacterWorldCustomization.characterOrigins','UICharacterWorldCustomization.characterHobbies',
        'UICharacterWorldCustomization.characterExtraStartingItems','UICharacterWorldCustomization.tmpSelectedOrigin',
        'UICharacterWorldCustomization.tmpSelectedHobby','UICharacterWorldCustomization.tmpSelectedExtraStartingItems',
        'UICharacterWorldCustomization.imgBackground')
    foreach ($required in $requiredFields) {
        $separator = $required.LastIndexOf('.')
        $typeName = $required.Substring(0, $separator)
        $fieldName = $required.Substring($separator + 1)
        $type = $game.MainModule.Types | Where-Object { $_.Name -eq $typeName -or $_.FullName -eq $typeName }
        if (!($type.Fields | Where-Object Name -eq $fieldName)) { $problems.Add("Required native field missing: $required") }
    }
    # Layout, render, material and hover callbacks may run frequently. Follow
    # their mod-local call graph so a future helper cannot hide a scan here.
    $methodIndex = @{}
    foreach ($type in $allTypes) { foreach ($method in $type.Methods) { $methodIndex[$method.FullName] = $method } }
    $callbackNames = @('NativeSelectableStateChanged','DrivenSurfaceLayout','Synchronize','SyncTexture','RefreshVisibility','OnPopulateMesh')
    $pendingMethods = [System.Collections.Generic.Stack[string]]::new()
    foreach ($type in $allTypes) {
        foreach ($method in $type.Methods) {
            if ($method.Name -in $callbackNames) { $pendingMethods.Push($method.FullName) }
        }
    }
    $visitedMethods = [System.Collections.Generic.HashSet[string]]::new()
    while ($pendingMethods.Count) {
        $name = $pendingMethods.Pop()
        if (!$visitedMethods.Add($name)) { continue }
        $method = $methodIndex[$name]
        if (!$method.HasBody) { continue }
        foreach ($instruction in $method.Body.Instructions) {
            if ($instruction.Operand -isnot [Mono.Cecil.MethodReference]) { continue }
            $target = $instruction.Operand
            if ($target.Name -in @('GetComponentsInChildren','GetComponentInChildren','FindObjectsOfTypeAll','FindObjectsOfType','FindObjectOfType',
                'StyleRoot','QueueRoot','QueueController','OrganizeBuildingCardsV9')) {
                $problems.Add("Hierarchy work in frequent callback: $($method.FullName) -> $($target.FullName)")
            }
            if ($methodIndex.ContainsKey($target.FullName)) { $pendingMethods.Push($target.FullName) }
        }
    }
    if ($problems.Count) { throw ($problems -join "`n") }
    [pscustomobject]@{
        Version = $mod.Name.Version.ToString()
        PollingCallbacks = 0
        PollingOrToggleReferences = 0
        NativeHookTargets = $hookCount
        NativeFieldsChecked = $requiredFields.Count
        FrequentCallbackMethodsChecked = $visitedMethods.Count
        FrequentCallbackHierarchyWork = 0
        OptionalOrInherited = ($unavailable -join '; ')
        SHA256 = (Get-FileHash -LiteralPath $ModPath -Algorithm SHA256).Hash
    }
}
finally { $mod.Dispose(); $game.Dispose() }
