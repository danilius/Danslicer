using System.Numerics;
using Danslicer.Core.Commands;
using Danslicer.Core.Config;

namespace Danslicer.Core.Supports;

/// <summary>A detached graph for rendering. Planning never changes the document, its selection or undo stack.</summary>
public sealed class StructurePreview : IDisposable
{
    private readonly Document _document;
    private Func<SupportGraph, IReadOnlyList<IDocumentCommand>>? _commands;
    private bool _applying;
    public bool IsValid { get; private set; } = true;
    public bool CanApply => IsValid && _commands is not null;
    public SupportGraph? Graph { get; private set; }
    public IReadOnlySet<Guid> HighlightedElements { get; private set; } = new HashSet<Guid>();
    public string Summary { get; private set; } = "Adjust settings to preview.";
    public string ConstraintDetails { get; private set; } = "";
    public IReadOnlyDictionary<Guid, string> ElementConstraints { get; private set; } = new Dictionary<Guid, string>();
    public bool Bracing { get; }
    public event Action? Invalidated;
    public event Action? BeforeUndo;
    public event Action? Dismissed;

    public StructurePreview(Document document, bool bracing)
    {
        _document = document; Bracing = bracing;
        document.ActiveStructurePreview = this;
        document.Changed += Invalidate;
        document.SelectionChanged += Invalidate;
        document.SupportSelectionChanged += Invalidate;
        document.NotifyStructurePreviewChanged();
    }

    public void Update(SupportConfig settings, Vector2? trunkCentre = null)
    {
        if (!IsValid) return;
        _commands = null;
        ConstraintDetails = "";
        ElementConstraints = new Dictionary<Guid, string>();
        HighlightedElements = new HashSet<Guid>();
        Graph = Clone(_document.Supports);
        if (!Bracing)
        {
            var plan = _document.PlanParentSupports(settings, trunkCentre);
            if (plan is not { } p) { Summary = "Select at least two tips to parent."; return; }
            if (p.Plans.Any(p => p.RemovedNodes.Count > 0 || p.RemovedSegments.Count > 0 || p.Edit.AddedNodes.Count > 0 || p.Edit.AddedSegments.Count > 0))
            {
                _commands = graph => SupportParenting.Commands(graph, p.Plans, "Parent supports");
                foreach (var command in _commands(Graph)) command.Execute();
            }
            var operands = _document.StructureOperands().SelectMany(id => _document.Supports.TryGetNode(id, out _)
                ? _document.Supports.Component(id).Nodes : _document.Supports.TryGetSegment(id, out var segment)
                    ? _document.Supports.Component(segment.NodeA).Nodes : []).ToHashSet();
            var contactPoints = operands.Select(_document.Supports.GetNode).Where(n => n.Type == SupportNodeType.Tip).Select(n => n.Position).ToHashSet();
            var highlights = new HashSet<Guid>();
            var independent = 0;
            var explanations = new Dictionary<Guid, string>();
            var remainingReasons = new List<string>();
            foreach (var component in Graph.Supports())
            {
                var tips = component.Nodes.Select(Graph.GetNode).Where(n => n.Type == SupportNodeType.Tip).ToList();
                if (!tips.Any(t => contactPoints.Contains(t.Position))) continue;
                if (tips.Count == 1) independent++;
                if (tips.Count != 1 && !tips.Any(t => operands.Contains(t.Id))) continue;
                highlights.UnionWith(component.Nodes); highlights.UnionWith(component.Segments);
                var reason = string.Join("\n", tips.Select(t => p.Outcome.TipConstraints.GetValueOrDefault(t.Position,
                    "This support was kept because its component could not be replaced as a whole.")).Distinct());
                remainingReasons.Add(reason);
                foreach (var id in component.Nodes.Concat(component.Segments)) explanations[id] = reason;
            }
            HighlightedElements = highlights;
            ElementConstraints = explanations;
            Summary = $"Trunks: {p.Outcome.TrunksBefore} → {p.Outcome.TrunksAfter} · {independent} independent supports";
            if (highlights.Count > 0) Summary += "\nIndependent or unchanged supports are highlighted in orange.";
            if (p.Outcome.Refused > 0) Summary += $"\n{p.Outcome.Refused} supports kept: no clear route within the current limits.";
            ConstraintDetails = settings.ParentingStyle == ParentingStyle.Candelabra
                ? string.Join("\n\n", remainingReasons.GroupBy(r => r).OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Count()} supports: {g.Key}"))
                : string.Join("\n\n", p.Outcome.Constraints);
        }
        else
        {
            if (settings.BracingPattern == BracingPattern.Zigzag)
                settings = settings with { BracingPattern = BracingPattern.Automatic };
            // Rebuild only braces between operands; preserve connections to unselected neighbours.
            var (nodes, segments) = SupportBracing.BracesBetween(Graph, _document.SupportTarget?.Id, _document.StructureOperands());
            new RemoveSupportElementsCommand(Graph, nodes, segments).Execute();
            var plan = _document.PlanBracing(Graph, settings);
            if (plan is not { } p)
            {
                Summary = "No separate pair of stems to brace. Select more supports, or reduce Cluster gap in Advanced if they form one bundle.";
                if (segments.Count > 0) _commands = graph => [new RemoveSupportElementsCommand(graph, nodes, segments, "Brace supports")];
                return;
            }
            if (nodes.Count > 0 || segments.Count > 0 || p.Edit.AddedNodes.Count > 0 || p.Edit.AddedSegments.Count > 0)
                _commands = graph => [new RemoveSupportElementsCommand(graph, nodes, segments, "Brace supports"),
                    new ApplySupportGraphEditCommand(graph, p.Edit, "Brace supports")];
            new ApplySupportGraphEditCommand(Graph, p.Edit).Execute();
            Summary = $"{p.Outcome.Braces} braces · {p.Outcome.SupportsTied} supports tied";
            if (p.Outcome.Braces == 0) Summary += "\nNo pair fits: check height, distance, angle and model clearance.";
        }
    }

    public bool Apply()
    {
        if (!CanApply) return false;
        _applying = true;
        try
        {
            _document.Execute(new CompositeCommand(Bracing ? "Brace supports" : "Parent supports", _commands!(_document.Supports)));
            _document.ClearSupportSelection();
            IsValid = false;
            _document.NotifyStructurePreviewChanged();
            return true;
        }
        finally { _applying = false; }
    }

    internal bool UndoPending()
    {
        if (!IsValid) return false;
        BeforeUndo?.Invoke();
        // Record the visible operation before undoing it so Redo can restore that exact graph.
        // A refused/no-op preview only dismisses; it must never consume an earlier history entry.
        if (CanApply && Apply()) _document.History.Undo();
        IsValid = false;
        Dismissed?.Invoke();
        Dispose();
        return true;
    }

    private void Invalidate()
    {
        if (_applying || !IsValid) return;
        IsValid = false;
        Graph = null;
        _commands = null;
        Summary = "The model or selection changed. Close and reopen the preview.";
        Invalidated?.Invoke();
        _document.NotifyStructurePreviewChanged();
    }

    public void Dispose()
    {
        _document.Changed -= Invalidate;
        _document.SelectionChanged -= Invalidate;
        _document.SupportSelectionChanged -= Invalidate;
        IsValid = false;
        if (ReferenceEquals(_document.ActiveStructurePreview, this)) _document.ActiveStructurePreview = null;
        _document.NotifyStructurePreviewChanged();
    }

    private static SupportGraph Clone(SupportGraph source)
    {
        var graph = new SupportGraph();
        graph.ReplaceWith(source.Nodes.Select(n => n.Clone()), source.Segments.Select(s => s.Clone()));
        return graph;
    }
}
