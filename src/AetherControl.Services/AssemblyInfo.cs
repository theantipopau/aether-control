using System.Runtime.CompilerServices;

// Lets AetherControl.Tests exercise internal, pure/testable logic (e.g.
// StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk) directly rather than needing everything
// touched by a regression test to be public API.
[assembly: InternalsVisibleTo("AetherControl.Tests")]
