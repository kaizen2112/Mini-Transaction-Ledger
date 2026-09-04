using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransactionLedger.Configuration;

/// <summary>
/// Money is always rendered with exactly two decimals, per the Format header
/// of docs/05-api-contract.md ("JSON numbers with two decimals, e.g. 1250.00").
///
/// Without this the scale of the underlying decimal leaks into the response.
/// A SUM over real numeric(18,2) rows comes back with scale 2, but EF Core
/// translates a sum over an empty set to COALESCE(SUM(...), 0.0), whose scale
/// is 1 — so an account list with no accounts serialised "totalBalance": 0.0
/// while a populated one serialised 0.00. Same value, two shapes, and the
/// frontend would have to cope with both.
///
/// Applying it to every decimal is correct here because in this system every
/// decimal IS money (BR-02).
/// </summary>
public sealed class MoneyJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        // WriteRawValue, not WriteStringValue: this must stay a JSON number.
        writer.WriteRawValue(value.ToString("0.00", CultureInfo.InvariantCulture));
}
