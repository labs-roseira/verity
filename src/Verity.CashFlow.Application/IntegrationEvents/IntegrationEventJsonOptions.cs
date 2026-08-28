using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verity.CashFlow.Application.IntegrationEvents;

public static class IntegrationEventJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
