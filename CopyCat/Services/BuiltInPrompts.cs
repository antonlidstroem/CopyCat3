namespace CopyCat.Services;

/// <summary>
/// SUPERSEDED — kept only to avoid a hard delete requirement.
///
/// The canonical prompt catalog now lives in
/// <c>CopyCat.Models.Catalog.BuiltInPrompts</c> (8 built-in prompts,
/// stable sort orders 1-8, <c>.All</c> list and <c>.BySortOrder</c> dictionary).
///
/// <see cref="DatabaseService"/> and <see cref="PromptsViewModel"/> both
/// import <c>CopyCat.Models.Catalog</c> and reference the Catalog version.
///
/// This file's class has been renamed so the compiler no longer sees
/// two types called <c>BuiltInPrompts</c> in the same compilation unit.
/// </summary>
internal static class LegacyBuiltInPrompts
{
    // Intentionally empty — all content migrated to Models/Catalog/BuiltInPrompts.cs
}
