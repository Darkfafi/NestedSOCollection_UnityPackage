# Changelog NestedSOCollection

## v2.0.0 - 18/02/2026
* `SOQueryDatabase`
	- When you create the `SOQueryDatabase` through the _NestedSO -> SOQueryDatabase_ Context Menu, you can build the Database by inheriting `SOQueryEntity` (or `ISOQueryEntity`). Through `SOQueryTags`, these can be labelled. And then through `SOQueryDatabase.Find<TargetSO>("Tag1", "Tag2")`, you can query to get a list of TargetSO type matching the given tags. 
	- This is to decouple one from a Collection, and allows a SO to be connected to multiple contextual relations through Tags. 
	- You can use`SOQueryTagsContainerAttribute` above a class or enum.
		- *Class*: Turns all const strings into tags selectable within `SOQueryTags`
		- *Enum*: Turns all enum names into tags selectable within `SOQueryTags`
	- Example:
		- I want all Mission for Emily, then I can do `SOQueryDatabase.Find<MissionConfigSO>("Emily")`
		- If I only want the main, hard difficulty, but does not matter for who, I can write `SOQueryDatabase.Find<MissionConfigSO>("MAIN", "Hard")`
* `NestedSOList`
	- When exposing a property of `NestedSOList`, it allows for Nested SO instances to be made on that `ScriptableObject` within that property. This allows for multiple nested ScriptableObjects within the same target ScriptableObject.
	- Example: 
		- TargetSO
			- NestedSOList<MissionConfigSO>
				- MissionSO1
				- MissionSO2
			- NestedSOList<AgentConfigSO>
				- AgentSO1
				- AgentSO2
* Improved `NestedSOCollectionEditor`
  * Added Search / Filtering of Items + Highlighting for
	* Name
	* Type
	* Property
  * Improved performance using Breadcrumbs Navigation
  * Added Mass Property Editing Feature

## v1.4.0 - 30/04/2023
* Corrected namespace categorizing issue within the GenericMenuEditorUtils
* Removed INestedSOCollection interface.
* Removed NestedSO.Editor Assembly
* Made AddAsset & RemoveAsset methods inaccessible from outside the package.
  * To hook into Adding / Removing, override OnAddedAsset & OnRemovedAsset methods instead
* Added AddAssetToCollection and RemoveAssetFromCollection methods to the NestedSOCollectionEditor to allow for adding and removing assets through code (within the Editor)

## v1.3.3 - 14/04/2023
* Fixed exceptions when a scene is loaded while inspecting a collection & during some recompile steps

## v1.3.2 - 06/04/2023
* Fixed exception when a scene is loaded while inspecting a collection

## v1.3.1 - 01/04/2023
* Made it so the NestedSOCollection allows for NestedSO classes in various Assemblies (required for UnityPackage Dependency support)

## v1.3.0 - 04/11/2022
* Unity 2021 Editor Visual Fixes

## v1.2.0 - 10/06/2022
* Initial Public Release
