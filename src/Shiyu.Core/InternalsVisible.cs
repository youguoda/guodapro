// ExtractContent is internal: it is an implementation detail of the backend,
// but the chunk shapes it has to survive are worth pinning down by test.
// StorageBench measures the store's real write path (including the batch
// transaction) on a throwaway database — the same justification.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Shiyu.Core.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("StorageBench")]
