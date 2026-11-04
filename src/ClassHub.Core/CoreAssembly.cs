namespace ClassHub.Core;

/// <summary>
/// Marker type for the ClassHub.Core assembly (used by module discovery and architecture tests).
/// </summary>
public static class CoreAssembly
{
    public static System.Reflection.Assembly Assembly => typeof(CoreAssembly).Assembly;
}
