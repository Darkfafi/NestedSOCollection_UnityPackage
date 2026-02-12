# Changelog NestedSOCollection

## v3.0.0 - 12/02/2026
* `NestedSOList` support
* Fixed `NestedSOCollectionEditor` Default Drawer not displaying

## v2.0.0 - 12/02/2026
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
