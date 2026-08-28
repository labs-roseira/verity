using Verity.CashFlow.IntegrationTests.Fixtures;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Support;

[CollectionDefinition("cash-flow")]
public sealed class CashFlowCollection : ICollectionFixture<CashFlowContainers>;
