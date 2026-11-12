using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NetieSpace.Apps.Tools.Catalog;

namespace NetieSpace.Apps.Tools;

/// <summary>A catalog entry joined with its runner, if one is registered.</summary>
public sealed record RegisteredTool(ToolEntry Entry, ToolRunnerDelegate? Runner, ToolAvailability Availability);

/// <summary>
/// Catalog (91 entries) + runners discovered by reflection + lazily computed, cached availability.
/// A provider added anywhere in the scanned assemblies is picked up without editing this folder.
/// </summary>
public sealed class ToolRegistry
{
    private readonly List<ToolEntry> _entries;
    private readonly Dictionary<string, ToolRunnerDelegate> _runners;
    private readonly IEngineProbe _probe;
    private readonly Dictionary<string, (bool, string)> _probeCache = new();
    private readonly Dictionary<string, ToolAvailability> _availCache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private static readonly Lazy<ToolRegistry> DefaultLazy = new(() => Load());

    /// <summary>Process-wide registry with real probes; availability is computed once per tool.</summary>
    public static ToolRegistry Default => DefaultLazy.Value;

    private ToolRegistry(List<ToolEntry> entries, Dictionary<string, ToolRunnerDelegate> runners, IEngineProbe probe)
    {
        _entries = entries;
        _runners = runners;
        _probe = probe;
    }

    public IReadOnlyList<ToolEntry> Entries => _entries;

    public static IReadOnlyList<ToolEntry> BuildCatalog() =>
        PdfCatalog.Entries.Concat(ImageCatalog.Entries).Concat(VideoCatalog.Entries).ToList();

    /// <summary>
    /// Scans <c>typeof(ToolRegistry).Assembly</c> plus <paramref name="extraAssemblies"/> for providers, and
    /// merges <paramref name="extraProviders"/> (instances, used by tests). Throws naming any unknown tool id
    /// or any id claimed twice.
    /// </summary>
    public static ToolRegistry Load(
        IEnumerable<Assembly>? extraAssemblies = null,
        IEngineProbe? probe = null,
        IEnumerable<IToolProvider>? extraProviders = null,
        IEnumerable<ToolEntry>? catalogOverride = null,
        bool scanAssemblies = true)
    {
        var entries = catalogOverride?.ToList() ?? BuildCatalog().ToList();
        var known = new HashSet<string>(entries.Select(e => e.Id), StringComparer.Ordinal);
        if (known.Count != entries.Count)
            throw new InvalidOperationException("Duplicate tool id in catalog.");

        var assemblies = new List<Assembly>();
        if (scanAssemblies) assemblies.Add(typeof(ToolRegistry).Assembly);
        if (scanAssemblies && extraAssemblies is not null)
            assemblies.AddRange(extraAssemblies.Where(a => !assemblies.Contains(a)));

        var providers = new List<IToolProvider>();
        foreach (var a in assemblies) providers.AddRange(DiscoverProviders(a));
        if (extraProviders is not null) providers.AddRange(extraProviders);

        var runners = new Dictionary<string, ToolRunnerDelegate>(StringComparer.Ordinal);
        foreach (var p in providers)
        {
            foreach (var (toolId, run) in p.Runners())
            {
                if (!known.Contains(toolId))
                    throw new InvalidOperationException(
                        $"Provider {p.GetType().Name} registers unknown tool id '{toolId}'.");
                if (!runners.TryAdd(toolId, run))
                    throw new InvalidOperationException(
                        $"Tool id '{toolId}' has more than one runner (provider {p.GetType().Name}).");
            }
        }

        return new ToolRegistry(entries, runners, probe ?? new EngineProbes());
    }

    /// <summary>Non-abstract public <see cref="IToolProvider"/> types with a parameterless constructor.</summary>
    public static IReadOnlyList<IToolProvider> DiscoverProviders(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        var list = new List<IToolProvider>();
        foreach (var t in types)
        {
            if (t.IsAbstract || t.IsInterface || !t.IsClass) continue;
            if (!typeof(IToolProvider).IsAssignableFrom(t)) continue;
            if (t.GetCustomAttribute<ToolProviderOptOutAttribute>() is not null) continue;
            if (t.GetConstructor(Type.EmptyTypes) is null) continue;
            list.Add((IToolProvider)Activator.CreateInstance(t)!);
        }
        return list;
    }

    public RegisteredTool? Find(string id)
    {
        var entry = _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        return entry is null ? null : Describe(entry);
    }

    public IReadOnlyList<RegisteredTool> All() => _entries.Select(Describe).ToList();

    private RegisteredTool Describe(ToolEntry entry)
    {
        _runners.TryGetValue(entry.Id, out var runner);
        return new RegisteredTool(entry, runner, AvailabilityOf(entry, runner is not null));
    }

    private ToolAvailability AvailabilityOf(ToolEntry entry, bool hasRunner)
    {
        lock (_gate)
        {
            if (_availCache.TryGetValue(entry.Id, out var cached)) return cached;
            var cachingProbe = new CachingProbe(_probe, _probeCache);
            var a = ToolAvailabilityRules.Compute(entry, hasRunner, cachingProbe);
            _availCache[entry.Id] = a;
            return a;
        }
    }

    private sealed class CachingProbe : IEngineProbe
    {
        private readonly IEngineProbe _inner;
        private readonly Dictionary<string, (bool, string)> _cache;
        public CachingProbe(IEngineProbe inner, Dictionary<string, (bool, string)> cache) { _inner = inner; _cache = cache; }
        public (bool Ok, string Detail) Probe(EngineRef engine)
        {
            var key = engine.Name + "|" + engine.ProbeKey;
            if (!_cache.TryGetValue(key, out var r)) _cache[key] = r = _inner.Probe(engine);
            return r;
        }
    }
}
