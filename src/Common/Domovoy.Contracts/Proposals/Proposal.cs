// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Proposals;

using System.Text.Json.Serialization;

using Domovoy.Contracts.Scenes;

/// <summary>
/// A candidate change awaiting human approval (roadmap Epic 2C) — the single queue in front of every
/// automated suggestion the system makes. Persisted in the <c>proposals</c> collection (DbGateway is the
/// source of truth, mirroring <c>automations</c>/<c>ml_models</c>); later also filled by the pattern-discovery
/// engine (Epic 2F). One flat shape with a <see cref="Kind"/> discriminator covers the three approval flows
/// that are technically ready in 1F/2B, so the WebUI has one inbox and the gateway one approve path.
///
/// <para><b>Invariant (principle 1):</b> nothing here activates without a person. A proposal is a hypothesis;
/// approval performs the side-effect (rule → Active, block stage promotion, model-version pin), reject leaves
/// the target untouched. The justification a reviewer sees is reused, not rebuilt: 1F replay for rules, the 2B
/// backtest scorecard for model/promotion proposals.</para>
/// </summary>
public class Proposal
{
    /// <summary>Stable proposal id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Which approval flow this proposal drives (see <see cref="ProposalKind"/>).</summary>
    public ProposalKind Kind { get; set; }

    /// <summary>Lifecycle: only <see cref="ProposalStatus.Proposed"/> proposals are actionable.</summary>
    public ProposalStatus Status { get; set; } = ProposalStatus.Proposed;

    /// <summary>Short human-readable headline ("Light on when motion after sunset").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Human-readable justification (support/confidence, scorecard summary, replay hits).</summary>
    public string? Rationale { get; set; }

    /// <summary>Who produced this: <c>user</c>, <c>ml_proposer</c> (the 2C heuristic), <c>discovery</c> (2F).</summary>
    public string Source { get; set; } = "user";

    // --- kind: Rule (a Proposed AutomationRule to activate; justified by 1F replay) ---

    /// <summary>The candidate rule's id in <c>automations</c> (Proposed status). Approve → sets it Active.
    /// Reused by <see cref="ProposalKind.RuleAmendment"/> to point at the <i>existing</i> rule to amend.</summary>
    public string? RuleId { get; set; }

    // --- kind: RuleAmendment (change an existing rule the household keeps overriding, Epic 3J) ---

    /// <summary>
    /// RuleAmendment: what to do to the rule <see cref="RuleId"/> points at on approve. v1 supports
    /// <c>disable</c> — retire a rule the household systematically overrides (the "living rules" signal, Epic 3J).
    /// Null for every other kind.
    /// </summary>
    public string? AmendmentAction { get; set; }

    // --- kind: BlockPromotion / ModelSelection (both target a control block) ---

    /// <summary>The control-block instance id in <c>control_blocks</c> this proposal patches.</summary>
    public string? BlockId { get; set; }

    /// <summary>BlockPromotion: current authority stage (0 Shadow / 1 Bounded / 2 Full), for the reviewer.</summary>
    public int? FromStage { get; set; }

    /// <summary>BlockPromotion: stage to promote to on approve (patches <c>Params["stage"]</c>).</summary>
    public int? ToStage { get; set; }

    /// <summary>ModelSelection: model version to pin to the block on approve (patches <c>Params["model_version"]</c>; 0 = latest).</summary>
    public int? ModelVersion { get; set; }

    // --- kind: MlTask (create an ML training task, Epic 2P) ---

    /// <summary>MlTask: target capability the proposed training task would learn. Approve → creates the task in <c>ml_tasks</c>.</summary>
    public string? MlTaskTarget { get; set; }

    // --- kind: Scene (create a first-class scene from a discovered configuration, Epic 3B × 2F) ---

    /// <summary>
    /// Scene: the scene to create on approve — a name plus the per-device target states the discovery engine
    /// found the household repeatedly arranging by hand (Epic 2F scene-configuration mining). No id yet: the
    /// scene does not exist until approved (like <see cref="MlTaskTarget"/>, the side-effect materializes it).
    /// Null for every other kind.
    /// </summary>
    public Scene? SceneDraft { get; set; }

    /// <summary>
    /// Scene: an optional daily cron (<c>"M H * * *"</c>). When set, approving the scene proposal ALSO creates
    /// an Active rule that activates the new scene on this schedule — the "you keep setting this up around the
    /// same time" bundle (Epic 2F). Null ⇒ approve creates only the scene, no rule.
    /// </summary>
    public string? SceneScheduleCron { get; set; }

    // --- provenance / scorecard (model-backed proposals) ---

    /// <summary>Model id backing this proposal (BlockPromotion/ModelSelection) — links to the scorecard.</summary>
    public string? ModelId { get; set; }

    /// <summary>Holdout metric name of <see cref="Score"/> (MAE / AUC / MacroF1), for display.</summary>
    public string? Metric { get; set; }

    /// <summary>Holdout score of the backing model (the honest "prediction vs fact" signal, Epic 2B).</summary>
    public double? Score { get; set; }

    /// <summary>
    /// Structured numeric evidence behind <see cref="Rationale"/> (support, confidence, lift, mi, p, samples,
    /// windowDays, …) so the WebUI can render a localized justification instead of the English fallback text.
    /// Numeric-only by design, mirroring <c>ControlBlock.Params</c>; producers fill what they measured.
    /// </summary>
    public Dictionary<string, double>? Evidence { get; set; }

    /// <summary>
    /// Provenance handle stamped when the proposal is approved and applied (Epic 2C DoD: an activated action
    /// is traceable to a decision). Generated on approve; empty while Proposed/Rejected.
    /// </summary>
    public string DecisionId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the proposal was approved or rejected; null while Proposed.</summary>
    public DateTime? DecidedAt { get; set; }
}

/// <summary>The three approval flows ready in 1F/2B; the discovery engine (2F) reuses the same set.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalKind
{
    /// <summary>Activate a Proposed <see cref="Domovoy.Contracts.Automations.AutomationRule"/> (justified by 1F replay).</summary>
    Rule,

    /// <summary>Promote an ML governor block's authority stage Shadow→Bounded→Full (justified by 2B scorecard).</summary>
    BlockPromotion,

    /// <summary>Pin a specific model version to a block instance (justified by 2B scorecard).</summary>
    ModelSelection,

    /// <summary>Create an ML training task for a capability with enough history (Epic 2P auto-suggestions).</summary>
    MlTask,

    /// <summary>Create a first-class scene (Epic 3B) from a repeatedly hand-arranged zone configuration, optionally
    /// with a daily schedule rule (Epic 2F scene-configuration mining). Approve materializes the scene (+ rule).</summary>
    Scene,

    /// <summary>Amend an existing rule the household keeps overriding (Epic 3J "living rules"). v1: disable a rule
    /// the user overrode in most of its firings. Approve applies <see cref="Proposal.AmendmentAction"/> to
    /// <see cref="Proposal.RuleId"/>; reject leaves the rule running.</summary>
    RuleAmendment,
}

/// <summary>Proposal lifecycle. Only <see cref="Proposed"/> is actionable; approve/reject are terminal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalStatus { Proposed, Approved, Rejected }
