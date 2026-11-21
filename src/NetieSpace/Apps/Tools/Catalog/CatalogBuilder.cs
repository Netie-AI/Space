using System;
using System.Linq;

namespace NetieSpace.Apps.Tools.Catalog;

/// <summary>Terse constructors so the three catalog files stay one line per tool.</summary>
internal static class CatalogBuilder
{
    public static ToolEntry E(
        string id, ToolApp app, string category, string name, string verb, string glyph,
        string acceptedCsv, int min, int max, EngineRef engine, ToolVerdict verdict,
        ToolOption[]? options, string notes) =>
        new(id, app, category, name, verb, glyph,
            acceptedCsv.Length == 0 ? Array.Empty<string>() : acceptedCsv.Split(','),
            min, max, options ?? Array.Empty<ToolOption>(), engine, verdict, null, notes);

    public static ToolOption O(
        string key, ToolOptionKind kind, string label, string? def,
        string[]? choices = null, int? min = null, int? max = null) =>
        new(key, kind, label, def, choices, min, max);

    public static string[] C(params string[] choices) => choices;
}
