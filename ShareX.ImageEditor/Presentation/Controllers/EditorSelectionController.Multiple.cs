#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;

using Avalonia.Media;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Presentation.Rendering;

namespace ShareX.ImageEditor.Presentation.Controllers;

public partial class EditorSelectionController
{
    private readonly List<Control> _selectedShapes = new();
    private Control? _propertyEditTarget;
    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Control[] _marqueeBaseline = [];
    private global::Avalonia.Controls.Shapes.Rectangle? _marquee;
    private bool _movingMultiple;
    private bool _moveHistoryCaptured;

    public IReadOnlyList<Control> SelectedShapes => _selectedShapes.Count > 0
        ? _selectedShapes : _selectedShape is null ? Array.Empty<Control>() : new[] { _selectedShape };

    public void SetSelectedShapes(IEnumerable<Control> shapes)
    {
        _selectedShapes.Clear();
        _selectedShapes.AddRange(shapes.Where(c => c.Tag is Annotation).Distinct());
        _selectedShape = _selectedShapes.LastOrDefault();
        UpdateBoundsObserver();
        ClearHoverOutline();
        UpdateSelectionHandles();
        SelectionChanged?.Invoke(_selectedShape != null);
    }

    public void SelectAll()
    {
        if (_view.FindControl<Canvas>("AnnotationCanvas") is Canvas canvas)
            SetSelectedShapes(canvas.Children.OfType<Control>().Where(c => c.Tag is Annotation));
    }

    private bool BeginSelection(Canvas canvas, Point point, PointerPressedEventArgs e)
    {
        Control? hit = HitTestShape(canvas, point);
        bool additive = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
        if (hit != null)
        {
            if (e.ClickCount == 2 && !additive)
            {
                SetSelectedShapes(new[] { hit });
                if (TryHandleDoubleClickEdit(hit, canvas)) { e.Handled = true; return true; }
            }
            if (additive)
            {
                var next = SelectedShapes.ToList();
                if (!next.Remove(hit)) next.Add(hit);
                SetSelectedShapes(next);
                e.Handled = true;
                return true;
            }
            if (!SelectedShapes.Contains(hit)) SetSelectedShapes(new[] { hit });
            _isDraggingShape = true;
            _lastDragPoint = point;
            _view.BeginInteractionCursorCapture(e.Pointer, _view.GetClosedHandCursor());
        }
        else
        {
            _marqueeStart = point;
            _marqueeBaseline = additive ? SelectedShapes.ToArray() : [];
            if (!additive) SetSelectedShapes(Array.Empty<Control>());
            _isMarqueeSelecting = true;
            _marquee = CreateSelectionOutline(new Rect(point, new Size(0, 0)));
            _view.FindControl<Canvas>("OverlayCanvas")?.Children.Add(_marquee);
            _view.BeginInteractionCursorCapture(e.Pointer, SelectToolCursor);
        }
        e.Handled = true;
        return true;
    }

    private void UpdateMarquee(Point point)
    {
        var rect = new Rect(Math.Min(point.X, _marqueeStart.X), Math.Min(point.Y, _marqueeStart.Y),
            Math.Abs(point.X - _marqueeStart.X), Math.Abs(point.Y - _marqueeStart.Y));
        if (_marquee != null)
        {
            Canvas.SetLeft(_marquee, ToOverlayCoordinate(rect.X));
            Canvas.SetTop(_marquee, ToOverlayCoordinate(rect.Y));
            _marquee.Width = rect.Width;
            _marquee.Height = rect.Height;
        }
        if (_view.FindControl<Canvas>("AnnotationCanvas") is Canvas canvas)
        {
            var enclosed = canvas.Children.OfType<Control>().Where(c => c.Tag is Annotation ann
                && IsEnclosed(rect, GetSelectionBounds(ann)));
            SetSelectedShapes(_marqueeBaseline.Concat(enclosed));
        }
    }

    private static bool IsEnclosed(Rect area, Rect bounds) =>
        area.Contains(bounds.TopLeft) && area.Contains(bounds.BottomRight);

    private static Rect GetSelectionBounds(Annotation annotation)
    {
        var b = annotation is NumberAnnotation number ? number.GetInteractionBounds() : annotation.GetBounds();
        if (Math.Abs(annotation.RotationAngle) > 0.001)
        {
            var rotation = SkiaSharp.SKMatrix.CreateRotationDegrees(annotation.RotationAngle, b.MidX, b.MidY);
            b = rotation.MapRect(b);
        }
        return new Rect(b.Left, b.Top, Math.Max(1, b.Width), Math.Max(1, b.Height));
    }

    private void ClearMarquee()
    {
        if (_marquee != null) _view.FindControl<Canvas>("OverlayCanvas")?.Children.Remove(_marquee);
        _marquee = null;
        _marqueeBaseline = [];
        _isMarqueeSelecting = false;
    }

    private static global::Avalonia.Controls.Shapes.Rectangle CreateSelectionOutline(Rect bounds)
    {
        var outline = new global::Avalonia.Controls.Shapes.Rectangle
        {
            Width = bounds.Width, Height = bounds.Height, Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>(new[] { 4d, 3d }),
            Fill = Brushes.Transparent, IsHitTestVisible = false
        };
        Canvas.SetLeft(outline, ToOverlayCoordinate(bounds.X));
        Canvas.SetTop(outline, ToOverlayCoordinate(bounds.Y));
        return outline;
    }

    private void DrawMultipleSelection(Canvas overlay)
    {
        foreach (var shape in _selectedShapes)
        {
            if (shape.Tag is not Annotation ann) continue;
            var outline = CreateSelectionOutline(GetSelectionBounds(ann));
            overlay.Children.Add(outline);
            _selectionHandles.Add(outline);
        }
    }

    private bool MoveMultiple(Point point)
    {
        if (_movingMultiple || _selectedShapes.Count < 2) return false;
        var primary = _selectedShape;
        var previousPoint = _lastDragPoint;
        _movingMultiple = true;
        try
        {
            foreach (var shape in _selectedShapes)
            {
                _selectedShape = shape;
                _lastDragPoint = previousPoint;
                HandleMove(point);
            }
        }
        finally
        {
            _selectedShape = primary;
            _lastDragPoint = point;
            _movingMultiple = false;
        }
        UpdateSelectionHandles();
        return true;
    }

    private void FinishMoveHistory()
    {
        if (!_moveHistoryCaptured) return;
        _moveHistoryCaptured = false;
        _view.EditorCore.CompleteAnnotationEdit();
    }

    // Reuse the existing per-shape property handlers, with one undo entry for the entire selection.
    internal bool EditSelection(Action edit)
    {
        if (_propertyEditTarget != null || SelectedShapes.Count == 0) return false;
        var shapes = SelectedShapes.ToArray();
        _view.EditorCore.BeginAnnotationEdit();
        try
        {
            foreach (var shape in shapes)
            {
                _propertyEditTarget = shape;
                edit();
                if (shape.Tag is Annotation annotation)
                    AnnotationVisualFactory.UpdateVisualControl(shape, annotation);
            }
        }
        finally { _propertyEditTarget = null; }
        UpdateSelectionHandles();
        _view.EditorCore.CompleteAnnotationEdit();
        return true;
    }
}
