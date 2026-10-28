using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.VisualTree;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Core.Editor;
using ShareX.ImageEditor.Presentation.Controllers;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
using SkiaSharp;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
Application.Current!.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS " + message);
}
void Pump() => Dispatcher.UIThread.RunJobs();
T PrivateField<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

var vm = new MainViewModel();
var view = new EditorView { DataContext = vm };
var editorCore = PrivateField<EditorCore>(view, "_editorCore");
var window = new Window { Width = 1200, Height = 850, Content = view };
window.Show();
vm.UpdatePreview(new SKBitmap(800, 500));
Pump();
window.UpdateLayout();
var canvas = view.FindControl<Canvas>("AnnotationCanvas")!;
var selection = PrivateField<EditorSelectionController>(view, "_selectionController");
Control Add(Annotation annotation)
{
    editorCore.AddAnnotation(annotation);
    var control = (Control)Invoke(view, "CreateControlForAnnotation", annotation)!;
    canvas.Children.Add(control);
    return control;
}
var first = new RectangleAnnotation { StartPoint = new SKPoint(100, 100), EndPoint = new SKPoint(180, 180), StrokeColor = "#FFFF0000" };
var second = new RectangleAnnotation { StartPoint = new SKPoint(220, 100), EndPoint = new SKPoint(300, 180), StrokeColor = "#FF00FF00" };
var outside = new RectangleAnnotation { StartPoint = new SKPoint(600, 350), EndPoint = new SKPoint(700, 450), StrokeColor = "#FF000000" };
var firstControl = Add(first);
var secondControl = Add(second);
Add(outside);
vm.ActiveTool = EditorTool.Select;
Pump();
window.UpdateLayout();
Point At(double x, double y) => canvas.TranslatePoint(new Point(x, y), window)!.Value;
window.MouseDown(At(40, 40), MouseButton.Left);
window.MouseMove(At(340, 220));
window.MouseUp(At(340, 220), MouseButton.Left);
Pump();
Check(selection.SelectedShapes.Count == 2, "drag box selects both enclosed annotations and excludes the outside item");
Check(first.StrokeColor == "#FFFF0000" && second.StrokeColor == "#FF00FF00", "selecting mixed colors does not recolor either item");
Invoke(view, "OnColorChanged", view, new SolidColorBrush(Color.Parse("#FF00FF00")));
Pump();
Check(first.StrokeColor == "#FF00FF00" && second.StrokeColor == "#FF00FF00", "choosing the primary item's existing color still updates the rest of the selection");
editorCore.Undo();
Pump();
first = (RectangleAnnotation)editorCore.Annotations[0];
second = (RectangleAnnotation)editorCore.Annotations[1];
outside = (RectangleAnnotation)editorCore.Annotations[2];
selection.SetSelectedShapes(canvas.Children.OfType<Control>().Take(2));
vm.SelectedColor = "#FF0000FF";
Pump();
Check(first.StrokeColor == "#FF0000FF" && second.StrokeColor == "#FF0000FF" && outside.StrokeColor == "#FF000000", "one color change updates only the selected items");
editorCore.Undo();
Pump();
var restored = editorCore.Annotations.OfType<RectangleAnnotation>().ToArray();
Check(restored[0].StrokeColor == "#FFFF0000" && restored[1].StrokeColor == "#FF00FF00", "one undo restores every selected item's original color");
editorCore.Redo();
Pump();
Check(editorCore.Annotations.Take(2).All(a => a.StrokeColor == "#FF0000FF"), "redo reapplies the complete bulk edit");

selection.ClearSelection();
window.MouseDown(At(105, 105), MouseButton.Left);
window.MouseUp(At(105, 105), MouseButton.Left);
window.MouseDown(At(225, 105), MouseButton.Left, RawInputModifiers.Control);
window.MouseUp(At(225, 105), MouseButton.Left, RawInputModifiers.Control);
Pump();
Check(selection.SelectedShapes.Count == 2, "Ctrl-click adds a separate item to selection");
window.MouseDown(At(225, 105), MouseButton.Left, RawInputModifiers.Shift);
window.MouseUp(At(225, 105), MouseButton.Left, RawInputModifiers.Shift);
Pump();
Check(selection.SelectedShapes.Count == 1, "Shift-click toggles an item out of selection");
selection.SelectAll();
var beforeMove = editorCore.Annotations.Select(a => a.StartPoint).ToArray();
selection.MoveSelectedShape(10, 5);
Check(editorCore.Annotations.Select((a, i) => a.StartPoint == beforeMove[i] + new SKPoint(10, 5)).All(v => v), "all selected items move by the same delta");
editorCore.Undo();
Pump();
Check(editorCore.Annotations.Select((a, i) => a.StartPoint == beforeMove[i]).All(v => v), "one undo restores the whole group move");
selection.SelectAll();
int beforeDuplicate = editorCore.Annotations.Count;
Invoke(view, "DuplicateSelectedAnnotation");
Pump();
Check(editorCore.Annotations.Count == beforeDuplicate * 2 && selection.SelectedShapes.Count == beforeDuplicate, "duplicate copies the entire group and selects its copies");
Check(editorCore.Annotations.Select(a => a.Id).Distinct().Count() == editorCore.Annotations.Count, "duplicate creates unique annotation identifiers");
editorCore.Undo();
Pump();
Check(editorCore.Annotations.Count == beforeDuplicate, "one undo removes the entire duplicated group");
selection.SelectAll();
Invoke(view, "PerformDelete");
Pump();
Check(editorCore.Annotations.Count == 0, "delete removes all selected items");
editorCore.Undo();
Pump();
Check(editorCore.Annotations.Count == beforeDuplicate, "one undo restores the entire deleted group");

var numberingVm = new MainViewModel();
var sequenceA = numberingVm.NumberSequenceId;
var a = Enumerable.Range(1, 5).Select(n => new NumberAnnotation { Number = n, SequenceId = sequenceA }).ToArray();
numberingVm.RecalculateNumberCounter(a);
Check(numberingVm.NumberCounter == 6, "first sequence increments through five markers");
numberingVm.ResetNumberCounterCommand.Execute(null);
var sequenceB = numberingVm.NumberSequenceId;
numberingVm.RecalculateNumberCounter(a);
Check(sequenceA != sequenceB && numberingVm.NumberCounter == 1 && a.Select(n => n.Number).SequenceEqual(new[] {1,2,3,4,5}), "new sequence restarts at one without changing the existing 1-5 set");
var b = Enumerable.Range(1, 3).Select(n => new NumberAnnotation { Number = n, SequenceId = sequenceB }).ToArray();
numberingVm.RecalculateNumberCounter(a.Concat(b));
Check(numberingVm.NumberCounter == 4, "second sequence increments independently");
using var core = new EditorCore();
core.LoadImage(new SKBitmap(100, 100));
core.AddAnnotations(a.Concat(b));
core.RemoveAnnotation(a[1]);
Check(a[2].Number == 2 && b.Select(n => n.Number).SequenceEqual(new[] {1,2,3}), "deleting a marker renumbers only its own sequence");
core.Undo();
Check(core.Annotations.OfType<NumberAnnotation>().Count(n => n.SequenceId == sequenceA) == 5, "undo preserves numbering sequence identities");
var removedB = core.Annotations.OfType<NumberAnnotation>().Where(n => n.SequenceId == sequenceB && n.Number <= 2).ToArray();
core.RemoveAnnotations(removedB);
Check(core.Annotations.OfType<NumberAnnotation>().Single(n => n.SequenceId == sequenceB).Number == 1
    && core.Annotations.OfType<NumberAnnotation>().Count(n => n.SequenceId == sequenceA) == 5, "bulk delete compacts only the affected count");
numberingVm.StepStartNumber = 3;
numberingVm.RecalculateNumberCounter(a.Concat(b));
Check(numberingVm.NumberCounter == 3, "changing the starting number begins a separate count");

Invoke(view, "ClearAllAnnotations");
Pump();
vm.ActiveTool = EditorTool.Step;
Pump();
for (int i = 0; i < 5; i++)
{
    window.MouseDown(At(80 + i * 120, 100), MouseButton.Left);
    window.MouseUp(At(80 + i * 120, 100), MouseButton.Left);
    Pump();
}
Check(editorCore.Annotations.OfType<NumberAnnotation>().Select(n => n.Number).SequenceEqual(new[] {1,2,3,4,5}), "mouse input creates a real 1-5 marker sequence");
var newSequenceButton = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "New sequence");
Check(newSequenceButton.Command?.CanExecute(null) == true, "New sequence button is bound to an enabled command");
newSequenceButton.Command!.Execute(null);
for (int i = 0; i < 5; i++)
{
    window.MouseDown(At(80 + i * 120, 300), MouseButton.Left);
    window.MouseUp(At(80 + i * 120, 300), MouseButton.Left);
    Pump();
}
Check(editorCore.Annotations.OfType<NumberAnnotation>().Select(n => n.Number).SequenceEqual(new[] {1,2,3,4,5,1,2,3,4,5}), "drawing after New sequence produces a second 1-5 set without continuing at six");
Check(editorCore.Annotations.OfType<NumberAnnotation>().Select(n => n.SequenceId).Distinct().Count() == 2, "the two drawn sets retain separate sequence identities");

window.Close();
Console.WriteLine($"Completed {checks} behavior checks.");
