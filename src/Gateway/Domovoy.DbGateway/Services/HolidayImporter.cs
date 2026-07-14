// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Optional online public-holiday import for the Calendar sensor (roadmap Epic 2L). Strictly a convenience:
/// the holiday list is otherwise hand-edited on the Settings page, so an offline install never needs this.
/// Provider-agnostic; the default backing is the free Nager.Date API. A failure degrades to an empty list
/// (the UI keeps the manual list untouched) rather than surfacing an error.
/// </summary>
public interface IHolidayImporter
{
    /// <summary>Public-holiday dates (local <c>yyyy-MM-dd</c>) for a country + year, or empty when offline.</summary>
    Task<IReadOnlyList<string>> ImportAsync(string countryCode, int year, CancellationToken ct);
}

/// <summary>Nager.Date public-holiday importer (<c>date.nager.at</c>). All faults swallowed to an empty list.</summary>
public sealed class NagerHolidayImporter : IHolidayImporter
{
    private readonly HttpClient _http;
    private readonly ILogger<NagerHolidayImporter> _logger;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public NagerHolidayImporter(HttpClient http, ILogger<NagerHolidayImporter> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ImportAsync(string countryCode, int year, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return Array.Empty<string>();

        try
        {
            var code = countryCode.Trim().ToUpperInvariant();
            var url = $"api/v3/PublicHolidays/{year}/{Uri.EscapeDataString(code)}";
            var holidays = await _http.GetFromJsonAsync<List<NagerHoliday>>(url, Json, ct);
            if (holidays is null) return Array.Empty<string>();

            return holidays
                .Select(h => h.Date)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.Ordinal)
                .ToList()!;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Holiday import for {Country} {Year} unavailable (offline?)", countryCode, year);
            return Array.Empty<string>();
        }
    }

    private sealed record NagerHoliday([property: JsonPropertyName("date")] string? Date);
}
