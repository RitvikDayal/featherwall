using Vortice.DirectComposition;

namespace FeatherWall.Rendering;

/// <summary>Anything the host keeps under a key above the content: a single surface, or a group.</summary>
internal interface IComposedOverlay : IDisposable
{
    IDCompositionVisual Visual { get; }
}

/// <summary>One keyed overlay made of ordered layers, each composed above the one before it.
///
/// The record needs this. Its face turns on the compositor while the progress rim stays still on
/// top of it, so the two cannot share a surface, and two separate overlays would not do either:
/// the root keeps no order among overlays, and these two overlap. Inside a group the order is
/// explicit. Layer offsets are relative to the group.</summary>
public sealed class CompositionLayerGroup : IComposedOverlay
{
    private readonly CompositionHost _host;
    private readonly List<CompositionSurface> _layers = [];

    public IDCompositionVisual Visual { get; }

    internal CompositionLayerGroup(CompositionHost host, int offsetX, int offsetY)
    {
        _host = host;
        Visual = host.Factories.DComp.CreateVisual();
        Visual.SetOffsetX(offsetX);
        Visual.SetOffsetY(offsetY);
    }

    /// <summary>A transparent surface above every layer added so far.</summary>
    public CompositionSurface AddLayer(int width, int height, int offsetX, int offsetY)
    {
        var layer = new CompositionSurface(_host, width, height, premultipliedAlpha: true, offsetX, offsetY)
        {
            Parent = Visual,
        };
        layer.DeviceLost += _host.RaiseDeviceLost;
        _host.MutateTree(() =>
        {
            // Ordered against the previous layer by name. A null reference does not mean "on top"
            // (see CompositionHost.CreateContent), so it is only used for the first, sibling-less one.
            Visual.AddVisual(layer.Visual, true, _layers.Count > 0 ? _layers[^1].Visual : null);
            _layers.Add(layer);
        });
        return layer;
    }

    public void SetOffset(int x, int y) => _host.MutateTree(() =>
    {
        Visual.SetOffsetX(x);
        Visual.SetOffsetY(y);
    });

    public void Dispose()
    {
        foreach (var layer in _layers) layer.Dispose();
        _layers.Clear();
        Visual.Dispose();
    }
}
