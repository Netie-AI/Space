namespace NetieSpace.Tests;

/// <summary>
/// Tests that measure the process: memory with GC.GetTotalAllocatedBytes (process-wide), or how fast a cancel
/// stops (wall clock). Any test class running in parallel adds its allocations and its CPU and thread-pool load
/// to the number. Classes in this collection run on their own, after the parallel ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IsolatedMeasurementCollection
{
    public const string Name = "Isolated measurement (not parallel)";
}
