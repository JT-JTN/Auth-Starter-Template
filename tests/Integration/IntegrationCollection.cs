using Xunit;

namespace Integration.Tests;

/// <summary>
/// Groups all WebApplicationFactory-based test classes into a single xUnit collection.
/// Tests within a collection run sequentially, which prevents the race condition where
/// two factories starting in parallel both try to freeze Serilog's global ReloadableLogger.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<AuthWebApplicationFactory> { }
