// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net;
using System.Text;
using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml;
using Domovoy.AutomationService.Ml.Templates;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Ml;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Multi-target model serving (Epic 2P): <see cref="MlModelService"/> caches predictors per (target, scope),
/// so a temperature model and an on_off model coexist without colliding, each answering only for its own
/// target, and the task's soft clamp applies to regression predictions (never to binary probabilities).
/// Real ML.NET artifacts are trained in-test; the gateway is a stubbed <see cref="HttpMessageHandler"/>.
/// </summary>
public sealed class MlMultiTargetServingTests
{
    private const string TempModelId = "temp-model";
    private const string ToggleModelId = "toggle-model";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Serves the model registry + artifacts + tasks the way the DbGateway would.
    private sealed class StubGateway : HttpMessageHandler
    {
        public required byte[] TempArtifact { get; init; }
        public required byte[] ToggleArtifact { get; init; }
        public double? ClampMax { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            return Task.FromResult(path switch
            {
                "api/ml/models" => JsonResponse(new[]
                {
                    new MlModel { Id = TempModelId, Name = "temp", Kind = MlModelKinds.ScheduleRegression, TargetCapability = "temperature", Scope = ModelScope.Global, Version = 1 },
                    new MlModel { Id = ToggleModelId, Name = "toggle", Kind = MlModelKinds.ScheduleBinary, TargetCapability = "on_off", Scope = ModelScope.Global, Version = 1 },
                }),
                $"api/ml/models/{TempModelId}/artifact" => BytesResponse(TempArtifact),
                $"api/ml/models/{ToggleModelId}/artifact" => BytesResponse(ToggleArtifact),
                "api/ml/tasks" => JsonResponse(new[]
                {
                    new MlTask { Id = "t1", Name = "temperature", TargetCapability = "temperature", ClampMax = ClampMax },
                }),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private static HttpResponseMessage JsonResponse<T>(T body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
        };

        private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
    }

    private static async Task<MlModelService> BuildServiceAsync(double? clampMax)
    {
        // Real artifacts: a regression that always sees ~30° and a binary schedule (on at night, off by day).
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var tempSamples = Enumerable.Range(0, 120)
            .Select(i => new LabeledSample(start.AddHours(i), 30.0)).ToList();
        var toggleSamples = Enumerable.Range(0, 240)
            .Select(i => new LabeledSample(start.AddHours(i), start.AddHours(i).Hour < 8 ? 1 : 0)).ToList();

        var temp = new ScheduleRegressionTemplate().Train(tempSamples, minSamples: 20);
        var toggle = new ScheduleBinaryTemplate().Train(toggleSamples, minSamples: 20);
        Assert.NotNull(temp);
        Assert.NotNull(toggle);

        var handler = new StubGateway { TempArtifact = temp!.Artifact, ToggleArtifact = toggle!.Artifact, ClampMax = clampMax };
        var db = new DbGatewayClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://stub") }, NullLogger<DbGatewayClient>.Instance);
        var service = new MlModelService(
            db, new HomeModeState(), new MlRuntimeState(), Options.Create(new AutomationOptions()), NullLogger<MlModelService>.Instance);
        await service.RefreshAsync(CancellationToken.None);
        return service;
    }

    [Fact]
    public async Task ServesEachTargetFromItsOwnModel()
    {
        var service = await BuildServiceAsync(clampMax: null);
        var chain = new[] { ModelScope.Global };
        var now = new DateTimeOffset(2026, 6, 10, 3, 0, 0, TimeSpan.Zero);

        Assert.True(service.TryPredict("temperature", now, chain, 0, out var temp));
        Assert.InRange(temp, 25, 35); // the regression learned ~30°

        Assert.True(service.TryPredict("on_off", now, chain, 0, out var probability));
        Assert.InRange(probability, 0, 1); // the binary model's natural output is a probability, not degrees

        // No cross-talk: a target nothing was trained for stays unserved.
        Assert.False(service.TryPredict("humidity", now, chain, 0, out _));
    }

    [Fact]
    public async Task TaskClamp_AppliesToRegression_NotToBinaryProbability()
    {
        var service = await BuildServiceAsync(clampMax: 25);
        var chain = new[] { ModelScope.Global };
        var now = new DateTimeOffset(2026, 6, 10, 3, 0, 0, TimeSpan.Zero);

        Assert.True(service.TryPredict("temperature", now, chain, 0, out var temp));
        Assert.True(temp <= 25.001f, $"prediction {temp} must be soft-clamped to 25");

        // The clamp is per-target and regression-only — the toggle's probability is untouched.
        Assert.True(service.TryPredict("on_off", now, chain, 0, out var probability));
        Assert.InRange(probability, 0, 1);
    }

    [Fact]
    public async Task LatestFor_ReturnsServingMetadataPerTargetScope()
    {
        var service = await BuildServiceAsync(clampMax: null);

        Assert.Equal(TempModelId, service.LatestFor("temperature", ModelScope.Global)?.Id);
        Assert.Equal(ToggleModelId, service.LatestFor("on_off", ModelScope.Global)?.Id);
        Assert.Null(service.LatestFor("temperature", ModelScope.Zone("kitchen")));
    }
}
