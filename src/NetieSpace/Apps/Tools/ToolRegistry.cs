using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ConcurrentDictionary<string, ProbeEntry> _probeCache = new();
    private readonly ConcurrentDictionary<string, ToolAvailability> _availCache = new(StringComparer.Ordinal);
    private readonly CachingProbe _cachingProbe;
    private int _warmed;

    /// <summary>How many engine probes run at once when the registry warms its cache.</summary>
    public const int ProbeParallelism = 6;

    private static readonly Lazy<ToolRegistry> DefaultLazy = new(() => Load());

    /// <summary>Process-wide registry with real probes; availability is computed once per tool.</summary>
    public static ToolRegistry Default => DefaultLazy.Value;

    private ToolRegistry(List<ToolEntry> entries, Dictionary<string, ToolRunnerDelegate> runners, IEngineProbe probe)
    {
        _entries = entries;
        _runners = runners;
        _probe = probe;
        _cachingProbe = new CachingProbe(probe, _probeCache);
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

    public IReadOnlyList<RegisteredTool> All()
    {
        Warm();
        return _entries.Select(Describe).ToList();
    }

    /// <summary>The engine availability is judged on: the catalog row's, or its finer refinement.</summary>
    public EngineRef EffectiveEngine(ToolEntry entry) => Engines.EffectiveFor(entry);

    public bool HasRunner(string toolId) => _runners.ContainsKey(toolId);

    /// <summary>Cached, bounded probe of one engine (the same answer the tools were judged on).</summary>
    public (bool Ok, string Detail) ProbeEngine(EngineRef engine) => _cachingProbe.Probe(engine);

    /// <summary>
    /// Runs every distinct engine probe the catalog needs, in parallel with a bound, once per process.
    /// Wall time is about the slowest probe, not the sum.
    /// </summary>
    public void Warm()
    {
        if (Interlocked.Exchange(ref _warmed, 1) == 1) return;
        var engines = _entries
            .Where(e => e.Verdict != ToolVerdict.CloudOnly && e.Verdict != ToolVerdict.AiEgress)
            .Select(Engines.EffectiveFor)
            .Where(LicenseGate.IsCleared)
            .GroupBy(e => CachingProbe.KeyOf(e)).Select(g => g.First()).ToList();
        WarmEngines(engines);
    }

    /// <summary>Probes the given engines in parallel (bounded); results land in the cache.</summary>
    public void WarmEngines(IEnumerable<EngineRef> engines)
    {
        // Own threads, not the pool: probes block on processes, and a small pool would ramp up one thread at a time.
        var queue = new ConcurrentQueue<EngineRef>(engines);
        var workers = Enumerable.Range(0, Math.Min(ProbeParallelism, queue.Count)).Select(_ =>
        {
            var t = new Thread(() => { while (queue.TryDequeue(out var e)) _cachingProbe.Probe(e); }) { IsBackground = true, Name = "engine-warm" };
            t.Start();
            return t;
        }).ToList();
        foreach (var t in workers) t.Join();
    }

    private RegisteredTool Describe(ToolEntry entry)
    {
        _runners.TryGetValue(entry.Id, out var runner);
        return new RegisteredTool(entry, runner, AvailabilityOf(entry, runner is not null));
    }

    private ToolAvailability AvailabilityOf(ToolEntry entry, bool hasRunner)
    {
        if (_availCache.TryGetValue(entry.Id, out var cached)) return cached;
        var judged = entry with { Engine = Engines.EffectiveFor(entry) };
        var a = ToolAvailabilityRules.Compute(judged, hasRunner, _cachingProbe);
        // A timed-out answer is recomputed later (the engine probe retries it), never kept for the process.
        return IsTimeout(a.Text) ? a : _availCache.GetOrAdd(entry.Id, a);
    }

    /// <summary>A timed-out answer is not a definite one: it is kept this long, then asked again.</summary>
    public const int TimeoutRetryMs = 10_000;

    private sealed record ProbeEntry(Lazy<(bool, string)> Result, long CreatedMs);

    private static long NowMs() => Environment.TickCount64;

    internal static bool IsTimeout(string text) => text.Contains("(probe timed out)", StringComparison.Ordinal);

    private sealed class CachingProbe : IEngineProbe
    {
        private readonly IEngineProbe _inner;
        private readonly ConcurrentDictionary<string, ProbeEntry> _cache;
        public CachingProbe(IEngineProbe inner, ConcurrentDictionary<string, ProbeEntry> cache) { _inner = inner; _cache = cache; }
        public static string KeyOf(EngineRef e) => e.Name + "|" + e.ProbeKey;
        public (bool Ok, string Detail) Probe(EngineRef engine)
        {
            var key = KeyOf(engine);
            while (true)
            {
                var entry = _cache.GetOrAdd(key, _ => New(engine));
                var r = entry.Result.Value;
                if (!r.Item1 && IsTimeout(r.Item2) && NowMs() - entry.CreatedMs > TimeoutRetryMs)
                {
                    if (_cache.TryUpdate(key, New(engine), entry)) continue;
                    continue;
                }
                return r;
            }
        }

        private ProbeEntry New(EngineRef engine) => new(new Lazy<(bool, string)>(() =>
        {
            try { return _inner.Probe(engine); }
            catch (Exception) { return (false, engine.Name); }
        }, LazyThreadSafetyMode.ExecutionAndPublication), NowMs());
    }
}
