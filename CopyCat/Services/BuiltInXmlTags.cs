namespace CopyCat.Services;

/// <summary>
/// Single source of truth for the 7 built-in XML prompt tag buttons.
///
/// These are seeded into the XmlTagButtons SQLite table on first launch.
/// Keys are SortOrder values (0–6) used for reliable identification.
/// </summary>
public static class BuiltInXmlTags
{
    public record TagSeed(string Label, string XmlOpen, string XmlClose, string Placeholder);

    public static readonly IReadOnlyList<TagSeed> Seeds = new[]
    {
        new TagSeed(
            label:       "📌 Role",
            xmlOpen:     "<role>",
            xmlClose:    "</role>",
            placeholder: "You are a senior software engineer specializing in..."),

        new TagSeed(
            label:       "🎯 Task",
            xmlOpen:     "<task>",
            xmlClose:    "</task>",
            placeholder: "Your task is to..."),

        new TagSeed(
            label:       "📋 Context",
            xmlOpen:     "<context>",
            xmlClose:    "</context>",
            placeholder: "Here is the relevant background information..."),

        new TagSeed(
            label:       "📤 Output",
            xmlOpen:     "<output_format>",
            xmlClose:    "</output_format>",
            placeholder: "Respond with a structured explanation followed by the full implementation code."),

        new TagSeed(
            label:       "🚫 Constraints",
            xmlOpen:     "<constraints>",
            xmlClose:    "</constraints>",
            placeholder: "Do not remove existing functionality. Follow existing patterns."),

        new TagSeed(
            label:       "💡 Examples",
            xmlOpen:     "<examples>",
            xmlClose:    "</examples>",
            placeholder: "Example:\nInput: ...\nExpected output: ..."),

        new TagSeed(
            label:       "📎 Insert Code",
            xmlOpen:     "",
            xmlClose:    "",
            placeholder: "[PASTE CHUNK]"),
    };
}
