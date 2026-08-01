namespace MdReader.Core;

/// <summary>A heading in the rendered document, used to build the table of contents.</summary>
public sealed record HeadingInfo(int Level, string Text, string Id, int SourceLine);
