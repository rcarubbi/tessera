using System.Text.Json.Serialization;

namespace Tessera.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReprocessMode
{
    Full,
    Incremental
}
