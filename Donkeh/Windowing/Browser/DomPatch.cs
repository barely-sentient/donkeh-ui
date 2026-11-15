using System.Text.Json.Serialization;

namespace Donkeh.Windowing.Browser;

/// <summary>A batch of DOM mutations produced from one renderer update.</summary>
internal sealed class DomPatchBatch
{
    /// <summary>Gets or sets the ordered mutations to apply.</summary>
    public List<DomPatch> Patches { get; set; } = [];
}

/// <summary>One DOM mutation.</summary>
/// <remarks>
/// The patch vocabulary deliberately stays small: create, remove, replace
/// children, set text, replace attributes and replace the event-name list. The
/// browser holds the authoritative DOM, so the host only has to describe the
/// difference from the last applied snapshot.
/// </remarks>
internal sealed class DomPatch
{
    /// <summary>Gets or sets the operation name.</summary>
    public string Op { get; set; } = string.Empty;

    /// <summary>Gets or sets the node id the operation targets.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the parent node id for a children operation.</summary>
    public int Parent { get; set; }

    /// <summary>Gets or sets the element tag for a create operation.</summary>
    public string? Tag { get; set; }

    /// <summary>Gets or sets whether a created node is a text node.</summary>
    public bool IsText { get; set; }

    /// <summary>Gets or sets the text for a text node.</summary>
    public string? Text { get; set; }

    /// <summary>Gets or sets the full attribute map for an element.</summary>
    public Dictionary<string, string>? Attrs { get; set; }

    /// <summary>Gets or sets the full event-name list for an element.</summary>
    public List<string>? Events { get; set; }

    /// <summary>Gets or sets the exact child order for an element.</summary>
    public List<int>? Children { get; set; }
}

/// <summary>Source-generated JSON contract for the DOM bridge.</summary>
/// <remarks>
/// Source generation keeps the bridge safe under trimming, which is the default
/// for published WebAssembly apps.
/// </remarks>
[JsonSerializable(typeof(DomPatchBatch))]
internal partial class DomJsonContext : JsonSerializerContext
{
}
