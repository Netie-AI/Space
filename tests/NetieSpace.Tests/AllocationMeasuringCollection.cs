namespace NetieSpace.Tests;

/// <summary>
/// Tests that measure memory with GC.GetTotalAllocatedBytes. That counter is process-wide, so any test class
/// running in parallel adds its allocations to the measurement. Classes in this collection run on their own,
/// after the parallel ones, so the number they assert on is their own work only.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasuringCollection
{
    public const string Name = "Allocation measuring (not parallel)";
}
