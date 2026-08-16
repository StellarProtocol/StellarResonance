namespace StellarLauncher.Core.Services;

/// <summary>One user-defined environment variable (Linux advanced launch options).</summary>
public sealed class EnvVar
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}
