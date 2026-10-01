using Xunit;

namespace PatentMarker.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class PatCheckResultCollection
    {
        public const string Name = "PATCHECK shared result state";
    }
}
