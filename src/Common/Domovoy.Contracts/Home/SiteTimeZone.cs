// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Home;

/// <summary>
/// Разрешение часового пояса площадки — в одном месте.
///
/// <para>Строчка «попробовать <c>FindSystemTimeZoneById</c>, при неудаче взять UTC» была написана
/// шесть раз в шести проектах, и все шесть раз чуть по-разному: где-то ловилось только
/// <c>TimeZoneNotFoundException</c>, где-то запасным вариантом был часовой пояс сервера, а не UTC.
/// Разница видна ровно там, где её меньше всего хочется увидеть, — в расписании бэкапов, в делении
/// суток дневником дома и в тарифных зонах, то есть в деньгах.</para>
///
/// <para>Правило одно: пусто или неизвестный идентификатор ⇒ UTC. Дом обязан работать без сети, а
/// падать из-за строки в настройках — не работать.</para>
/// </summary>
public static class SiteTimeZone
{
    /// <summary>IANA-идентификатор → часовой пояс; пусто/неизвестно ⇒ <see cref="TimeZoneInfo.Utc"/>.</summary>
    public static TimeZoneInfo Resolve(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) return TimeZoneInfo.Utc;

        try
        {
            // .NET разбирает IANA-идентификаторы на всех платформах (ICU); windows-идентификатор
            // тоже пройдёт — отдельной ветки для него не нужно.
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (Exception)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
