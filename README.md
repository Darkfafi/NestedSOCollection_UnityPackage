# NestedSO Collection & Query System

A powerful, editor-enhanced framework for managing, nesting, and querying `ScriptableObjects` in Unity. 

This package allows you to deeply nest ScriptableObjects within each other as sub-assets, cleanly navigate them via custom Inspectors, and query them at runtime with zero-allocation, lazy-loaded indexing.

## 🌟 Core Features
* **Sub-Asset Management:** Safely create, delete, and manage ScriptableObjects *inside* other ScriptableObjects without memory leaks or dangling references.
* **Sub-Asset Migration:** Push external assets into collections, pop nested assets out into standalone files, or move them between collections instantly while preserving infinite nesting.
* **Inline Editor Navigation:** Breadcrumbs, deep-dive inspectors, and search filters built directly into your default Inspector windows.
* **Mass Property Editing:** Filter a list of nested objects and change a specific property across all of them simultaneously.
* **High-Performance Query Database:** Look up items by ID or intersect Tags instantly. Uses integer-based serialized caching and granular lazy-loading.

---

## 📦 1. NestedSOCollection

The **`NestedSOCollectionBase<T>`** is perfect for creating "Root" database assets. 

Use it for clustering information together, such as a Level Collection containing individual levels. It is also incredibly useful to cluster hierarchical `ScriptableObject`s (e.g., Mage -> Spells -> Fireball) without having many 'Fireball' files scattered all over your project. By nesting them, you never have to guess which spell is connected to whom.

### Key Editor Features:
* **Breadcrumb Navigation:** Dive deep into nested structures and navigate back up seamlessly.
* **Deep-Dive Inspector:** Edit child ScriptableObjects directly within the parent's inspector.
* **Advanced Search & Mass Edit:** Search by name, type, or property values. Select a filtered list and mass-apply property changes in one click.
* **Push, Pop & Bulk Pop:** Drag and drop external assets to merge them into the collection, extract individual nested assets back into standalone `.asset` files via the (⋮) menu, or Bulk Pop entire filtered search results to a folder.
* **Move:** Instantly transfer a nested item (and all its children) to another compatible collection directly from the (⋮) menu.

### How to use:
Simply inherit from **`NestedSOCollectionBase<T>`**:

```csharp
using NestedSO;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Character Collection")]
public class CharacterCollection : NestedSOCollectionBase<CharacterData> 
{
    // That's it! The custom Editor will automatically handle creation,
    // deletion, and inline-editing of CharacterData sub-assets.
}
```

---

## 📋 2. NestedSOList

While a Collection acts as a root asset, **`NestedSOList<T>`** allows you to embed a managed list of sub-asset ScriptableObjects as a *property* inside **any** existing ScriptableObject. This is ideal for modular components (like a list of Abilities on a Character class, or a list of Objectives in a Quest).

### Key Editor Features:

* **Reorderable:** Drag and drop items easily.
* **Polymorphic Creation:** A dropdown menu automatically finds all derived, non-abstract types of `T` to let you add variations of your base class.
* **Inline Details Area:** Click "Search / Details" to open an embedded workspace right under the list for searching, mass-editing, and deep-dive inspections.
* **Push, Pop & Bulk Pop:** Seamlessly merge external assets into the list or extract them out into standalone files while maintaining sub-asset integrity.

### How to use:

Declare a **`NestedSOList<T>`** field in your ScriptableObject.

```csharp
using NestedSO;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Character Class")]
public class CharacterClass : ScriptableObject
{
    public string ClassName;
    
    // This will draw the custom inline editor and automatically 
    // save new Abilities as sub-assets of this CharacterClass.
    public NestedSOList<Ability> Abilities; 
}
```

---

## 🔍 3. SOQueryDatabase

The **`SOQueryDatabase`** is a centralized, hyper-optimized lookup system. It allows you to find any data entity in your project via a unique `Id` or a collection of `Tags`.

### Performance & Optimizations:

* **Build-Time Indexing:** The database scans your project, maps IDs/Tags, and saves them as a lightweight list of integers (rather than heavy Object references).
* **Granular Lazy-Loading:** At runtime, the database only allocates memory for the specific tags you request. If you never query the "Hard" tag, the database never spends RAM loading it.
* **Prewarmed Queries:** Define queries in the Editor (e.g., `"Weapon, Legendary"`) to have their intersections pre-calculated during the build step.

### Editor Playground:

The Database Inspector features a **Query Playground** where you can test tag intersections and ID lookups directly in the Editor. It features built-in pagination, memory footprint analysis, and cache health verification (including a 1-click Auto-Fix for duplicate IDs).

### How to use:

**1. Setup the Database**

* Create an **`SOQueryDatabase`** asset via `Right Click > Create > NestedSO > SOQueryDatabase`.
* Click **Populate List** to find all **`ISOQueryEntity`** objects in your project.
* Click **Rebuild Cache** to bake the high-performance integer lookup maps.

**2. Runtime Querying**

```csharp
// Fetch a specific item instantly (O(1) lookup)
MissionData level1 = myDatabase.Get<MissionData>("Mission_01");

// Fetch all items matching a set of tags
List<WeaponData> fireSwords = myDatabase.Find<WeaponData>("Sword", "Fire", "Legendary");
```

---

## 🏷️ 4. Advanced Tagging & Dynamic State

### Attributes

* **`[SOQueryExcludeType]`**: By default, the database automatically indexes the class name of your ScriptableObject (and its base classes) as a searchable tag. Apply this attribute to abstract or base classes you *don't* want cluttering your tag pool.
* **`[SOQueryTagsContainer]`**: Apply this to a `static class` or `enum` containing your hardcoded tag strings. The Custom Editor will scan for this attribute to automatically populate the "+ Add Tag Filter" dropdowns and Tags Explorer, saving you from typos!

### Runtime Tags & Post-Deserialization

Sometimes you want a `ScriptableObject` to derive its tags programmatically from its serialized fields (e.g., automatically tagging a spell as "Fire" because its `ElementType` enum is set to `Fire`), rather than typing them manually into the Editor.

This is achieved using the **`SOQueryEntity`** base class. It hooks into Unity's `OnEnable` and `OnValidate` methods to clear and rebuild runtime tags dynamically using the abstract `SyncTags` method, without permanently writing those derived tags into the `.asset` file.

```csharp
// The provided base class
[SOQueryExcludeType]
public abstract class SOQueryEntity : ScriptableObject, ISOQueryEntity
{
    [field: SerializeField] public string Id { get; private set; }
    
    [SerializeField] protected SOQueryTags _tags = new SOQueryTags();
    public IReadOnlySOQueryTags Tags => _tags;

    public virtual void OnEnable() => RefreshEntity();
    public virtual void OnValidate() => RefreshEntity();

    protected void RefreshEntity()
    {
        if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString();
        _tags.ClearRuntime(); // Clears dynamically added tags
        SyncTags(_tags);      // Rebuilds them
    }

    protected abstract void SyncTags(SOQueryTags tags);
}
```

The `SOQueryEntity` is part of the toolbox, and can be used as the following (or used as an example if you want to use `ISOQueryEntity` on your own `ScriptableObject`)

```csharp
// Example Usage in your game:
public class SpellData : SOQueryEntity
{
    [SerializeField] private ElementType _spellElement;

    protected override void SyncTags(SOQueryTags tags)
    {
        // Procedurally injects the element type as a tag after deserialization
        // The database will now instantly include this spell if you query for "Fire"
        tags.AddRuntime(_spellElement.ToString());
    }
}
```

---

## ⚙️ Configuration & Tips

### Auto Refresh On Play

To ensure your **`SOQueryDatabase`** cache is always up-to-date with the latest data you've tweaked in the Editor, the system intercepts the Play Mode transition.

* You can toggle this feature from the top toolbar: **Tools > NestedSO > Auto Refresh On Play**.
* It is also available as a toggle at the bottom of the **`SOQueryDatabase`** inspector.