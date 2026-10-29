using System;
using System.Reflection;
using System.Runtime.Loader;

namespace Emby.Server.Implementations.Plugins;

/// <summary>
/// A custom <see cref="AssemblyLoadContext"/> for loading Jellyfin plugins.
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver? _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginLoadContext"/> class.
    /// </summary>
    /// <param name="path">The path of the plugin assembly.</param>
    public PluginLoadContext(string path) : base(true)
    {
        // Android : AssemblyDependencyResolver n'est pas supporté (PlatformNotSupportedException)
        // ; les dépendances du plugin sont alors résolues par le contexte par défaut (bundle
        // fusionné android, qui contient déjà toutes les bibliothèques de référence).
        if (!OperatingSystem.IsAndroid())
        {
            _resolver = new AssemblyDependencyResolver(path);
        }
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver?.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is not null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }
}
