// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml.Governors;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Ml;
using Domovoy.Contracts.Proposals;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Auto-suggests ML training tasks (Epic 2P, decision №2): periodically scans the capabilities that a governor
/// block type can actually consume, and where enough history has accrued but no task exists yet, queues a
/// "start learning X" proposal into the 2C approval queue. Nothing is created without a person — approving the
/// proposal creates the task (with defaults the user can tune on the ML hub). Restricting candidates to
/// governor targets keeps the queue honest: a model nothing can consume is not proposed.
/// </summary>
public sealed class MlTaskSuggester : BackgroundService
{
    private const string Source = MlActivitySources.MlTaskSuggester;

    private readonly DbGatewayClient _db;
    private readonly MlTrainingService _training;
    private readonly BlockCatalog _catalog;
    private readonly MlProposerGate _gate;
    private readonly AutomationOptions _options;
    private readonly ILogger<MlTaskSuggester> _logger;

    public MlTaskSuggester(
        DbGatewayClient db, MlTrainingService training, BlockCatalog catalog, MlProposerGate gate,
        IOptions<AutomationOptions> options, ILogger<MlTaskSuggester> logger)
    {
        _db = db;
        _training = training;
        _catalog = catalog;
        _gate = gate;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Outcome of one scan, for the manual endpoint.</summary>
    public sealed record ScanResult(int Candidates, int Created, string Note);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ProposalScanHours <= 0) return; // periodic scan disabled; manual endpoint still works

        // A small startup delay lets the gateway + counters come up first (mirrors RuleSuggester).
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Epic 3I: the periodic scan is gated (layer + proposers on, history mature); a gated cycle is
                // journalled, not run. Manual "scan now" bypasses the gate (explicit action) via ScanOnceAsync.
                var gate = await _gate.EvaluateAsync(stoppingToken);
                if (gate.Allowed) await ScanOnceAsync(stoppingToken);
                else await _gate.WriteSkippedAsync(Source, gate, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "ML-task suggestion scan failed"); }

            try { await Task.Delay(TimeSpan.FromHours(_options.ProposalScanHours), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Scan once: governor-consumable targets with enough history and no task → queued proposals.</summary>
    public async Task<ScanResult> ScanOnceAsync(CancellationToken ct)
    {
        var tasks = await _db.GetMlTasksAsync(ct);
        if (tasks is null)
        {
            await _gate.WriteErrorAsync(Source, "ml_tasks unavailable", ct);
            return new ScanResult(0, 0, "ml_tasks unavailable");
        }

        var open = await _db.GetProposalsAsync(ct, nameof(ProposalStatus.Proposed)) ?? new List<Proposal>();
        // Epic 3I rejection memory: a target the user already declined must not be re-proposed next scan.
        var rejected = await _db.GetProposalsAsync(ct, nameof(ProposalStatus.Rejected)) ?? new List<Proposal>();
        var taken = new HashSet<string>(
            tasks.Select(t => t.TargetCapability)
                .Concat(open.Concat(rejected)
                    .Where(p => p.Kind == ProposalKind.MlTask && p.MlTaskTarget is not null)
                    .Select(p => p.MlTaskTarget!)),
            StringComparer.OrdinalIgnoreCase);

        // Candidates = the ML targets some governor type can consume (Р9): a model no block can serve is noise.
        var candidates = _catalog.Types
            .OfType<IMlGovernorBlockType>()
            .Select(t => t.MlTargetCapability)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(target => !taken.Contains(target))
            .ToList();

        var created = 0;
        var defaults = new MlTask(); // proposal quotes the default window/threshold the created task will get
        foreach (var target in candidates)
        {
            var check = await _training.CheckDataAsync(target, defaults.WindowDays, defaults.MinSamples, zones: false, ct);
            var global = check.Scopes.FirstOrDefault(s => s.Level == ModelScope.Global.Level);
            if (!check.TemplateAvailable || global is null || !global.Sufficient) continue;

            var proposal = await _db.CreateProposalAsync(new Proposal
            {
                Kind = ProposalKind.MlTask,
                Title = $"Start learning '{target}'",
                Rationale = $"{global.Samples} samples of '{target}' accrued over the last {defaults.WindowDays} days "
                    + $"(≥ {defaults.MinSamples} required) and a governor block type can consume the model. "
                    + "Approving creates the training task; tune its window/limits on the ML page.",
                Source = "ml_task_scanner",
                MlTaskTarget = target,
                Evidence = new Dictionary<string, double>
                {
                    ["samples"] = global.Samples,
                    ["required"] = defaults.MinSamples,
                    ["windowDays"] = defaults.WindowDays,
                },
            }, ct);
            if (proposal is not null) created++;
        }

        _logger.LogInformation("ML-task scan: {Candidates} candidate(s), {Created} proposal(s) queued", candidates.Count, created);

        // Epic 3I: make the scan visible in the ML journal, and raise one notification when it found something.
        var note = created > 0 ? "queued ML-task proposal(s)" : candidates.Count > 0 ? "no new candidates" : "no consumable targets ready";
        await _gate.WriteScanAsync(Source, candidates.Count, created, note, null, ct);
        await _gate.NotifyFindingsAsync(created, ct);

        return new ScanResult(candidates.Count, created, created == 0 ? "no new candidates" : "ok");
    }
}
