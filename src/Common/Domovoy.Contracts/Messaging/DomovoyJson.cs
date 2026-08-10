// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domovoy.Contracts.Messaging;

/// <summary>
/// The wire format of the bus, fixed in one place.
///
/// <para><b>Почему это важно.</b> Конверт (<see cref="Envelope"/>) размечен атрибутами
/// <c>[JsonPropertyName]</c> в нижнем регистре по CloudEvents, а <c>data</c> внутри него ехал так, как
/// его сериализует умолчание <see cref="JsonSerializer"/> — то есть PascalCase. Формат «конверт в одном
/// регистре, полезная нагрузка в другом» не был зафиксирован ничем: ни настройками, ни тестом. Он
/// работал только потому, что и отправитель, и получатель звали сериализатор без опций, и любая
/// попытка «навести порядок» в одном сервисе тихо разъехалась бы с остальными.</para>
///
/// <para>Теперь формат объявлен: <see cref="Bus"/> — то, чем сериализуются и разбираются ВСЕ сообщения
/// шины. Регистр свойств полезной нагрузки намеренно оставлен как есть (PascalCase): менять его —
/// ломающее изменение формата для всех уже развёрнутых компонентов, а не уборка.</para>
/// </summary>
public static class DomovoyJson
{
    /// <summary>Опции сериализации сообщений шины. Единственный источник истины по формату.</summary>
    public static readonly JsonSerializerOptions Bus = new()
    {
        // Имена свойств полезной нагрузки — как в C# (PascalCase). Это ЗАФИКСИРОВАННЫЙ формат,
        // совпадающий с тем, что дома уже работает; конверт независимо размечен атрибутами.
        PropertyNamingPolicy = null,

        // Разбор терпим к регистру: сообщение от компонента другой версии не должно теряться из-за
        // того, что кто-то когда-то поменял политику имён.
        PropertyNameCaseInsensitive = true,

        // Значения по умолчанию не пишем: конверт и без того несёт много необязательных полей, а
        // каждый лишний байт умножается на поток состояний устройств.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
