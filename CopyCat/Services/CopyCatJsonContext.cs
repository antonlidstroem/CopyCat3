using System.Text.Json.Serialization;

namespace CopyCat.Services;

/// <summary>
/// Source-generated JSON serializer context for CopyCat.
///
/// WHY THIS EXISTS
/// ───────────────
/// System.Text.Json's default (reflection-based) serializer is NOT trimmer-safe.
/// In an Android Release build the .NET trimmer runs in "full" mode, and any type
/// metadata it cannot statically see from a C# call site is stripped.  A plain
/// JsonSerializer.Serialize(list) call keeps the serializer code but loses the
/// type descriptor for List&lt;string&gt;, causing a JsonException or silent data loss
/// at runtime.
///
/// Source generation solves this: the [JsonSerializable] attributes below instruct
/// the Roslyn generator to emit all required metadata at compile time, so the
/// trimmer can see every type path and keep it.
///
/// USAGE
/// ─────
/// Everywhere the ViewModel previously wrote:
///
///     JsonSerializer.Serialize(myList)
///     JsonSerializer.Deserialize&lt;List&lt;string&gt;&gt;(json)
///
/// it should now write:
///
///     JsonSerializer.Serialize(myList, CopyCatJsonContext.Default.ListString)
///     JsonSerializer.Deserialize(json,  CopyCatJsonContext.Default.ListString) ?? []
///
/// Adding a new serializable type?  Add a [JsonSerializable] attribute here.
/// </summary>
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class CopyCatJsonContext : JsonSerializerContext { }
