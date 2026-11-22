namespace MyWealthV2.Domain.Enums;

/// <summary>The kind of a corporate action (corporate-actions R3). Roadmap C2 and C3 add theirs.</summary>
public enum CorporateActionKind
{
    /// <summary><c>ratioNew</c> new shares replace every <c>ratioHeld</c> held, with <c>ratioNew &gt; ratioHeld</c>.
    /// Posted as <c>Split</c>.</summary>
    Split = 0,

    /// <summary><c>ratioNew</c> new shares replace every <c>ratioHeld</c> held, with <c>ratioNew &lt; ratioHeld</c>.
    /// Posted as <c>Split</c>.</summary>
    Consolidation = 1,

    /// <summary><c>ratioNew</c> new shares are added for every <c>ratioHeld</c> held. Posted as <c>Bonus</c>.</summary>
    Bonus = 2
}
