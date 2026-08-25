namespace NetieSpace.Services;

/// <summary>
/// Space-side Cortex WASM pack policy. The wasmtime isolate lives in CortexOS;
/// Space sends this contract on every sidecar call and honors CORTEX_WASM_DISABLED.
/// Demo may disable; production default is ON (env unset).
/// See docs/CORTEX_RUNTIME_BEST_FINAL.md and scripts/cortex_wasm_isolate.py.
/// </summary>
public static class CortexWasmPolicy
{
    public const int FuelLimit = 50_000;
    public const int MemoryBytes = 4 * 1024 * 1024;
    public const int TimeoutMs = 2_000;
    public const int PageSize = 65_536;
    public const int MemoryPages = MemoryBytes / PageSize;

    public static bool IsDisabled
    {
        get
        {
            var v = Environment.GetEnvironmentVariable("CORTEX_WASM_DISABLED");
            return v is "1" or "true" or "TRUE" or "yes" or "YES";
        }
    }

    public static Dictionary<string, object?> ToPayload() => new()
    {
        ["enabled"] = !IsDisabled,
        ["fuel"] = FuelLimit,
        ["memory_bytes"] = MemoryBytes,
        ["memory_pages"] = MemoryPages,
        ["timeout_ms"] = TimeoutMs,
        ["deny_imports"] = true,
        ["wasi_fs"] = false,
        ["wasi_net"] = false,
    };
}
