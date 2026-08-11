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
    public string ExposureStatus { get; set; } = "Keep";
    public string ExposureReason { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string RequiredPlatformPermission { get; set; } = string.Empty;
}
