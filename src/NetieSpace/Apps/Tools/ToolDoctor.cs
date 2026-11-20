using System;
using System.Collections.Generic;
using System.Linq;
using NetieSpace.Apps.Tools.Catalog;

namespace NetieSpace.Apps.Tools;

/// <summary>One engine as the doctor reports it. Present is definite: true or false, never unknown.</summary>
public sealed record EngineReport(
    string Name,
    EngineLicense License,
    bool Present,
    string Reason,
    int ToolCount,
    int RunnerCount,
    IReadOnlyList<string> ToolIds);

public static class ToolDoctor
{
    /// <summary>
    /// One report per engine (the catalog's engines plus every engine a tool is judged on). The Reason of a
    /// missing engine is exactly the text every tool on it shows in --list-tools.
    /// </summary>
    public static IReadOnlyList<EngineReport> Build(ToolRegistry registry)
    {
        var onEngine = registry.Entries
            .GroupBy(e => registry.EffectiveEngine(e).Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList(), StringComparer.Ordinal);

        var engines = Engines.All
            .Concat(registry.Entries.Select(registry.EffectiveEngine))
            .GroupBy(e => e.Name, StringComparer.Ordinal).Select(g => g.First()).ToList();

        registry.WarmEngines(engines.Where(LicenseGate.IsCleared));

        var list = new List<EngineReport>();
        foreach (var e in engines)
        {
            bool present;
            string reason;
            if (!LicenseGate.IsCleared(e))
            {
                present = false;
                reason = ToolAvailabilityRules.PrefixLicence + e.Name;
            }
            else
            {
                var (ok, detail) = registry.ProbeEngine(e);
                present = ok;
                reason = ok ? "" : ToolAvailabilityRules.PrefixEngineMissing + (string.IsNullOrWhiteSpace(detail) ? e.Name : detail);
            }
            var ids = onEngine.TryGetValue(e.Name, out var l) ? l : new List<string>();
            list.Add(new EngineReport(e.Name, e.License, present, reason, ids.Count, ids.Count(registry.HasRunner), ids));
        }
        return list;
    }
}
