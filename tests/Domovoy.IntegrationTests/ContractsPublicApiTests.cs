// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Reflection;
using System.Text;

using Domovoy.Contracts.Messaging;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Guards the public surface of <c>Domovoy.Contracts</c> (roadmap Epic 3K).
///
/// <para><b>Why.</b> Domovoy now updates per component, and the resolver trusts exactly what is
/// declared in <c>build/components.json</c>. A contract changed without declaring it is the one way
/// to quietly ship an incompatible update to a live house — and spotting it by eye in a diff is not
/// something anyone should have to rely on.</para>
///
/// <para><b>How.</b> The whole public surface is rendered into a stable text snapshot and compared
/// with the approved one. Any change to a payload record, message type or capability constant fails
/// this test with a diff, forcing a deliberate act: update the snapshot <i>and</i> bump
/// <c>bus</c>/<c>provides</c>/<c>requires</c> in <c>build/components.json</c>. The snapshot file
/// showing up in a commit is the visible signal that compatibility needs a decision.</para>
///
/// <para>To re-approve after an intentional change:
/// <c>UPDATE_APPROVALS=1 dotnet test --filter ContractsPublicApi</c></para>
///
/// <para>Honest limit: this sees signatures, not meaning. The same endpoint returning the same type
/// with a different interpretation of a field passes here — that one is on review and contract tests.</para>
/// </summary>
public class ContractsPublicApiTests
{
    private const string ApprovalFile = "Approvals/ContractsPublicApi.approved.txt";

    [Fact]
    public void PublicSurface_MatchesApprovedSnapshot()
    {
        var actual = RenderPublicApi(typeof(MessageTypes).Assembly);
        var path = ResolveApprovalPath();

        if (Environment.GetEnvironmentVariable("UPDATE_APPROVALS") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        var approved = File.ReadAllText(path).Replace("\r\n", "\n");
        if (approved == actual) return;

        Assert.Fail(
            "Публичная поверхность Domovoy.Contracts изменилась.\n\n" +
            Diff(approved, actual) + "\n" +
            "Если изменение намеренное — сделайте ДВА шага:\n" +
            "  1. Объявите совместимость в build/components.json (bus.speaks/understands,\n" +
            "     provides/requires) — правила в docs/architecture/coding_standards_ru.md,\n" +
            "     раздел «Контракты и совместимость».\n" +
            "  2. Переутвердите снимок: UPDATE_APPROVALS=1 dotnet test --filter ContractsPublicApi\n");
    }

    /// <summary>
    /// Renders the public surface deterministically: sorted, signature-only, no XML docs and no
    /// member ordering from reflection (which is not stable across runs).
    /// </summary>
    private static string RenderPublicApi(Assembly assembly)
    {
        var sb = new StringBuilder();
        sb.Append("# Публичная поверхность Domovoy.Contracts. Снимок, а не документация.\n");
        sb.Append("# Меняется только вместе с объявлением совместимости в build/components.json.\n\n");

        var types = assembly.GetExportedTypes()
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in types)
        {
            sb.Append(type.FullName).Append('\n');

            var members = new List<string>();

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                if (field.IsSpecialName) continue;
                var value = field.IsLiteral ? $" = {Format(field.GetRawConstantValue())}" : "";
                members.Add($"  {(field.IsLiteral ? "const" : "field")} {Name(field.FieldType)} {field.Name}{value}");
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                var accessors = property.CanWrite ? "get set" : "get";
                members.Add($"  prop {Name(property.PropertyType)} {property.Name} {{ {accessors} }}");
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                if (method.IsSpecialName || method.DeclaringType == typeof(object)) continue;
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{Name(p.ParameterType)} {p.Name}"));
                members.Add($"  method {Name(method.ReturnType)} {method.Name}({parameters})");
            }

            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var parameters = string.Join(", ", ctor.GetParameters().Select(p => $"{Name(p.ParameterType)} {p.Name}"));
                members.Add($"  ctor ({parameters})");
            }

            foreach (var member in members.OrderBy(m => m, StringComparer.Ordinal)) sb.Append(member).Append('\n');
            sb.Append('\n');
        }

        return sb.ToString().Replace("\r\n", "\n");
    }

    private static string Name(Type type)
    {
        if (!type.IsGenericType) return type.FullName ?? type.Name;

        var definition = (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0];
        var arguments = string.Join(", ", type.GetGenericArguments().Select(Name));
        return $"{definition}<{arguments}>";
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "",
    };

    /// <summary>Compact line diff — enough to see what moved without pulling in a diff library.</summary>
    private static string Diff(string approved, string actual)
    {
        var before = approved.Split('\n');
        var after = actual.Split('\n');

        var removed = before.Except(after).Where(l => l.Trim().Length > 0).Take(20).ToList();
        var added = after.Except(before).Where(l => l.Trim().Length > 0).Take(20).ToList();

        var sb = new StringBuilder();
        if (removed.Count > 0)
        {
            sb.Append("Убрано или изменено:\n");
            foreach (var line in removed) sb.Append("  - ").Append(line.Trim()).Append('\n');
        }
        if (added.Count > 0)
        {
            sb.Append("Добавлено:\n");
            foreach (var line in added) sb.Append("  + ").Append(line.Trim()).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// The snapshot lives in the repository, not next to the test binary — it is reviewed material,
    /// so it has to show up in diffs.
    /// </summary>
    private static string ResolveApprovalPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Domovoy.IntegrationTests.csproj")))
            directory = directory.Parent;

        var root = directory?.FullName ?? AppContext.BaseDirectory;
        return Path.Combine(root, ApprovalFile);
    }
}
