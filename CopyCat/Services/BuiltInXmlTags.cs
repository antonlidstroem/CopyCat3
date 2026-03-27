namespace CopyCat.Services;

public static class BuiltInXmlTags
{
    public record TagSeed(string Label, string XmlOpen, string XmlClose, string Placeholder);

    public static readonly IReadOnlyList<TagSeed> Seeds = new[]
    {
        new TagSeed("📌 Role",        "<role>",           "</role>",           "You are a senior software engineer specializing in..."),
        new TagSeed("🎯 Task",        "<task>",           "</task>",           "Your task is to..."),
        new TagSeed("📋 Context",     "<context>",        "</context>",        "Here is the relevant background information..."),
        new TagSeed("📤 Output",      "<output_format>",  "</output_format>",  "Respond with a structured explanation followed by the full implementation code."),
        new TagSeed("🚫 Constraints", "<constraints>",    "</constraints>",    "Do not remove existing functionality. Follow existing patterns."),
        new TagSeed("💡 Examples",    "<examples>",       "</examples>",       "Example:\nInput: ...\nExpected output: ..."),
        new TagSeed("📎 Insert Code", "",                 "",                  "[PASTE CHUNK]"),
    };
}