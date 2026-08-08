using System;

namespace MoodleConnector.Presentation.Configuration;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class MoodleToolMetadataAttribute : Attribute
{
    public string Family { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string CanonicalOperation { get; set; } = string.Empty;
    public bool Structural { get; set; }
}
