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

using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Presentation.ViewModels;
using SkiaSharp;

namespace ShareX.ImageEditor.Presentation.Views;

public partial class EditorView
{
    private static List<Annotation>? _clipboardAnnotations;

    private static void ClearMultipleClipboard()
    {
        if (_clipboardAnnotations != null)
            foreach (var annotation in _clipboardAnnotations) (annotation as IDisposable)?.Dispose();
        _clipboardAnnotations = null;
    }

    private async Task<bool> CopyMultipleSelectionAsync(bool cut)
    {
        if (_selectionController.SelectedShapes.Count < 2) return false;
        ClearMultipleClipboard();
        _clipboardAnnotations = _selectionController.SelectedShapes.Select(c => c.Tag).OfType<Annotation>()
            .Select(a => a.Clone()).ToList();
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.ClearAsync();
        if (cut) PerformDelete();
        await CheckClipboardStatus();
        return true;
    }

    private void AddSelectionCopies(IEnumerable<Annotation> originals)
    {
        if (this.FindControl<Canvas>("AnnotationCanvas") is not Canvas canvas) return;
        var copies = originals.Select(a => a.Clone()).ToArray();
        var sequences = new Dictionary<Guid, Guid>();
        foreach (var copy in copies)
        {
            copy.Id = Guid.NewGuid();
            copy.StartPoint += new SKPoint(20, 20);
            copy.EndPoint += new SKPoint(20, 20);
            if (copy is ICurvedSegmentAnnotation curved)
                CurvedSegmentHelper.OffsetCurvePoint(curved, 20, 20);
            if (copy is FreehandAnnotation freehand)
                for (int i = 0; i < freehand.Points.Count; i++) freehand.Points[i] += new SKPoint(20, 20);
            if (copy is NumberAnnotation number)
            {
                if (number.HasTailPoint) number.SetTailPoint(number.TailPoint + new SKPoint(20, 20));
                if (!sequences.TryGetValue(number.SequenceId, out var sequence))
                    sequences[number.SequenceId] = sequence = Guid.NewGuid();
                number.SequenceId = sequence;
            }
            if (copy is SpeechBalloonAnnotation balloon && balloon.HasTailPoint)
                balloon.SetTailPoint(balloon.TailPoint + new SKPoint(20, 20));
        }
        _editorCore.AddAnnotations(copies);
        var controls = new List<Control>();
        foreach (var copy in copies)
            if (CreateControlForAnnotation(copy) is Control control)
            {
                canvas.Children.Add(control);
                controls.Add(control);
            }
        if (DataContext is MainViewModel vm) { vm.ActiveTool = EditorTool.Select; vm.HasAnnotations = true; }
        _selectionController.SetSelectedShapes(controls);
    }
}
