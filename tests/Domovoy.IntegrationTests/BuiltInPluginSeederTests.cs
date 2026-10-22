// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.PluginSupervisor.Plugins;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Delivery of the first-party plugins that ship inside the supervisor image (Epic 1C × Epic 3K).
/// The interesting property is not "does it copy files" but the boundary between <b>delivery</b> and
/// the operator's <b>uninstall</b>: a plugin the owner removed must stay removed, or "uninstall" would
/// silently mean "until the next restart".
/// </summary>
public class BuiltInPluginSeederTests
{
    [Theory]
    // никогда не раскладывали → свежий дом получает первопартийный набор
    [InlineData(false, null, "0.1.0", true)]
    // раскладывали, папка на месте, образ несёт новее → обновляем
    [InlineData(true, "0.1.0", "0.2.0", true)]
    // раскладывали, версия та же → не трогаем
    [InlineData(true, "0.2.0", "0.2.0", false)]
    // раскладывали, установлено новее образа (откат компонента) → не понижаем
    [InlineData(true, "0.3.0", "0.2.0", false)]
    // раскладывали, папки нет → владелец удалил плагин, это его решение
    [InlineData(true, null, "0.2.0", false)]
    public void SeedingRespectsUninstallAndOnlyMovesForward(
        bool seenBefore, string? installed, string available, bool expected) =>
        Assert.Equal(expected, BuiltInPluginSeeder.ShouldSeed(seenBefore, installed, available));

    [Theory]
    [InlineData("0.1.11", "0.1.9", true)]   // числовое сравнение, не лексикографическое
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.2", "0.2.0", false)]     // недостающие сегменты = 0
    [InlineData("0.2.1", "0.2", true)]
    [InlineData("кривая", "0.0.1", false)]  // мусор не должен оказаться «новее»
    public void VersionOrdering(string candidate, string current, bool expected) =>
        Assert.Equal(expected, BuiltInPluginSeeder.IsNewer(candidate, current));

    [Fact]
    public void SeedsAFreshRoot_AndLeavesAnUninstalledPluginAlone()
    {
        var root = Path.Combine(Path.GetTempPath(), $"domovoy-seed-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "image");
        var plugins = Path.Combine(root, "plugins");

        try
        {
            WritePlugin(Path.Combine(source, "commute-planner"), "0.1.0");

            var first = BuiltInPluginSeeder.Seed(source, plugins, NullLogger.Instance);
            Assert.Equal(new[] { "commute-planner" }, first);
            Assert.True(File.Exists(Path.Combine(plugins, "commute-planner", "plugin.json")));

            // Владелец удалил плагин из UI — папка исчезла. Перезапуск не должен его воскресить.
            Directory.Delete(Path.Combine(plugins, "commute-planner"), recursive: true);

            var second = BuiltInPluginSeeder.Seed(source, plugins, NullLogger.Instance);
            Assert.Empty(second);
            Assert.False(Directory.Exists(Path.Combine(plugins, "commute-planner")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WritePlugin(string folder, string version)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            $$"""{"id":"commute-planner","name":"Commute","version":"{{version}}"}""");
    }
}
