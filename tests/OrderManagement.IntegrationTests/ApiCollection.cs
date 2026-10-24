namespace OrderManagement.IntegrationTests;

/// <summary>All API tests share one <see cref="ApiFactory"/> (one host, one MySQL container) and run serially.</summary>
[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
