namespace Domovoy.Contracts.Security;

/// <summary>
/// The open permission vocabulary (roadmap Epic 2E — roles model). Permissions are plain strings, kept
/// <b>open</b> like the capability model (<see cref="Domovoy.Contracts.Capabilities.WellKnownCapabilities"/>):
/// a plugin can request a permission that isn't listed here (plugin manifests already carry
/// <c>Permissions</c> as free strings), and a future domain adds one without a schema change.
/// <see cref="All"/> is the canonical set the WebUI renders as checkboxes when editing a role.
///
/// <para><b>No enforcement in Phase 2.</b> These name what a role is <i>allowed</i> to do; nothing checks them
/// yet. Local auth (login/tokens/sessions) that actually gates requests on these is Phase 3 — this epic only
/// makes the model, so it's ready to be enforced later without a data migration.</para>
/// </summary>
public static class WellKnownPermissions
{
    /// <summary>See devices and their live state.</summary>
    public const string DevicesView = "devices.view";

    /// <summary>Send commands to devices (switch/dim/setpoint).</summary>
    public const string DevicesControl = "devices.control";

    /// <summary>Create/edit/delete zones (the area graph).</summary>
    public const string ZonesManage = "zones.manage";

    /// <summary>Change the home mode (Home/Away/Night/Vacation).</summary>
    public const string ModesManage = "modes.manage";

    /// <summary>Create/edit/enable automation rules.</summary>
    public const string AutomationsManage = "automations.manage";

    /// <summary>Create/edit control blocks and their parameters.</summary>
    public const string BlocksManage = "blocks.manage";

    /// <summary>Train models and manage the ML registry.</summary>
    public const string ModelsManage = "models.manage";

    /// <summary>Approve or reject proposals in the queue (Epic 2C).</summary>
    public const string ProposalsApprove = "proposals.approve";

    /// <summary>Install/start/stop integration plugins.</summary>
    public const string PluginsManage = "plugins.manage";

    /// <summary>Read the activity center (events, automation history, ops logs).</summary>
    public const string ActivityView = "activity.view";

    /// <summary>Manage local users and roles (this epic's own surface).</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Super-permission: full control, including everything above. The admin role holds this.</summary>
    public const string SystemAdmin = "system.admin";

    /// <summary>Canonical, ordered vocabulary for the role editor. Open — not an exhaustive constraint.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        DevicesView,
        DevicesControl,
        ZonesManage,
        ModesManage,
        AutomationsManage,
        BlocksManage,
        ModelsManage,
        ProposalsApprove,
        PluginsManage,
        ActivityView,
        UsersManage,
        SystemAdmin,
    };
}
