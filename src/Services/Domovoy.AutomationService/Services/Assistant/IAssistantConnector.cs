namespace Domovoy.AutomationService.Services.Assistant;

/// <summary>
/// Provider-agnostic extension point for a future natural-language assistant (roadmap Epic 2H). Two capabilities,
/// both <b>outside</b> the control path — the assistant never actuates anything, it only sits on top of the
/// deterministic engine:
/// <list type="bullet">
///   <item><b>Authoring</b> — turn a plain-language request ("turn the hall light on when someone comes home
///     after dark") into a <c>Proposed</c> rule that goes through the same approval queue (Epic 2C) and 1F replay
///     validation as any other proposal. Nothing it drafts activates without a person.</item>
///   <item><b>Explaining</b> — put the "why did this happen" attribution the system already has (1F:
///     trigger + rule/decision) into a plain-language sentence.</item>
/// </list>
///
/// <para>This is intentionally a stub: the shipped implementation (<see cref="DisabledAssistantConnector"/>)
/// reports unavailable and returns a graceful message, gated by <see cref="Configuration.AssistantOptions"/>.
/// A real backend is a later, config-selected or plugin (Epic 1C) implementation of this same interface — the
/// endpoints and UI call the interface, so wiring one in later needs no changes here.</para>
/// </summary>
public interface IAssistantConnector
{
    /// <summary>Whether a real backend is configured and enabled. False for the stub (the default).</summary>
    bool IsAvailable { get; }

    /// <summary>Name of the active backend, or empty when unavailable (documentation/telemetry).</summary>
    string Provider { get; }

    /// <summary>Draft a candidate rule from a plain-language request. The stub returns <c>Available=false</c>.</summary>
    Task<AssistantAuthorResult> AuthorRuleAsync(AssistantAuthorRequest request, CancellationToken ct);

    /// <summary>Explain an action/attribution in plain language. The stub returns <c>Available=false</c>.</summary>
    Task<AssistantExplainResult> ExplainAsync(AssistantExplainRequest request, CancellationToken ct);
}

/// <summary>Current assistant capability status (for a status surface / feature-flag check).</summary>
public sealed record AssistantStatus(bool Available, string Provider, IReadOnlyList<string> Capabilities);

/// <summary>A plain-language authoring request ("turn on the porch light at sunset").</summary>
public sealed record AssistantAuthorRequest(string Prompt);

/// <summary>
/// Result of an authoring attempt. When <see cref="Available"/> is false the request was a no-op (stub / disabled);
/// a real backend would set <see cref="RuleId"/> to the queued <c>Proposed</c> rule.
/// </summary>
public sealed record AssistantAuthorResult(bool Available, string? RuleId, string Message);

/// <summary>What to explain: a device's last change, a rule firing, or a decision id (1F attribution handles).</summary>
public sealed record AssistantExplainRequest(string? DeviceId, string? RuleId, string? DecisionId);

/// <summary>Result of an explanation attempt; <see cref="Available"/> false ⇒ stub / disabled.</summary>
public sealed record AssistantExplainResult(bool Available, string? Explanation, string Message);
