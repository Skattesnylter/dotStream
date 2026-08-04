using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DotStream.Core;

namespace DotStream.Simulator;

/// <summary>
/// A virtual AKP153E: the same 6x3 grid, the same cell indices, and - importantly -
/// the same restriction that column 5 cannot be pressed.
///
/// Built in code rather than XAML because the grid is generated from
/// <see cref="DeckLayout"/>; there is no hand-authored layout to keep in sync.
/// </summary>
public sealed class DeckSimulatorControl : UserControl
{
    // 1:1 with the hardware. Any other value means resampling a nearest-neighbour
    // image by a non-integer factor, which duplicates pixel rows and makes clean
    // artwork look far worse than the real LCD ever will.
    private const double CellDisplaySize = DeckLayout.CellPixels;
    private const double CellGap = 8;

    private static readonly Color BodyColor = Color.FromRgb(0x1B, 0x1B, 0x1E);
    private static readonly Color KeyBezelColor = Color.FromRgb(0x33, 0x33, 0x38);
    private static readonly Color InfoBezelColor = Color.FromRgb(0x24, 0x24, 0x28);

    private readonly Image[] _images = new Image[DeckLayout.CellCount + 1];
    private readonly Border[] _cells = new Border[DeckLayout.CellCount + 1];
    private readonly ScaleTransform[] _presses = new ScaleTransform[DeckLayout.CellCount + 1];
    private readonly Border _dimmer;

    private Point _dragOrigin;
    private int _dragFrom = -1;
    private bool _dragging;

    public DeckSimulatorControl()
    {
        var body = new Border
        {
            Background = new SolidColorBrush(BodyColor),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            SnapsToDevicePixels = true
        };

        var grid = new Grid();

        for (int column = 0; column < DeckLayout.Columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(CellDisplaySize + CellGap * 2),
                // A slightly wider gutter before the info column, as on the real device.
                MinWidth = column == DeckLayout.InfoColumn ? CellDisplaySize + CellGap * 3 : 0
            });
        }

        for (int row = 0; row < DeckLayout.Rows; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellDisplaySize + CellGap * 2) });

        for (int row = 0; row < DeckLayout.Rows; row++)
        {
            for (int column = 0; column < DeckLayout.Columns; column++)
            {
                int index = DeckLayout.ToProtocolIndex(row, column);
                FrameworkElement cell = BuildCell(index);

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        _dimmer = new Border
        {
            Background = new SolidColorBrush(Colors.Black),
            Opacity = 0,
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(14)
        };

        var stack = new Grid();
        stack.Children.Add(grid);
        stack.Children.Add(_dimmer);
        body.Child = stack;

        Content = body;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Raised with the protocol index when a key cell is clicked.</summary>
    public event EventHandler<int>? CellPressed;

    /// <summary>
    /// Right-click on a cell. An editor gesture only - the hardware has no such
    /// thing, so nothing in the runtime behaviour may depend on it.
    /// </summary>
    public event EventHandler<int>? CellRightClicked;

    /// <summary>Something was dragged onto a cell.</summary>
    public event EventHandler<DeckDropEventArgs>? CellDropped;

    /// <summary>The drag format carrying a cell's own index, for moving a key.</summary>
    public const string CellDragFormat = "dotstream/cell";

    /// <summary>
    /// Whether a cell holds something worth dragging. The control has only pixels; it
    /// cannot tell an empty key from a black one, so the host answers.
    /// </summary>
    public Func<int, bool>? CanDragCell { get; set; }

    private static DragDropEffects EffectFor(IDataObject data) =>
        data.GetDataPresent(CellDragFormat) ? DragDropEffects.Move : DragDropEffects.Copy;

    /// <summary>Highlights a cell as a live drop target.</summary>
    public void SetDropTarget(int protocolIndex, bool active)
    {
        if (!DeckLayout.IsValid(protocolIndex)) return;

        bool isKey = DeckLayout.IsKey(protocolIndex);

        _cells[protocolIndex].BorderBrush = new SolidColorBrush(
            active ? Color.FromRgb(0x4D, 0xD9, 0xE8) : isKey ? KeyBezelColor : InfoBezelColor);
        _cells[protocolIndex].BorderThickness = new Thickness(active ? 3 : isKey ? 2 : 1);
    }

    public void SetCell(int protocolIndex, BitmapSource image)
    {
        if (!DeckLayout.IsValid(protocolIndex)) return;
        _images[protocolIndex].Source = image;
    }

    public void ClearCell(int protocolIndex)
    {
        if (!DeckLayout.IsValid(protocolIndex)) return;
        _images[protocolIndex].Source = null;
    }

    public void ClearAll()
    {
        foreach (int index in DeckLayout.AllCells())
            _images[index].Source = null;
    }

    /// <summary>0-100, mirroring the LIG command.</summary>
    public void SetBrightness(int percent)
    {
        double clamped = Math.Clamp(percent, 0, 100) / 100.0;
        _dimmer.Opacity = 1.0 - (0.25 + 0.75 * clamped);
    }

    private FrameworkElement BuildCell(int index)
    {
        bool isKey = DeckLayout.IsKey(index);

        var image = new Image
        {
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true
        };

        // Cells are 85x85 blown up to 92 on screen; keep the pixels crisp rather
        // than letting WPF smooth them, so we see what the LCD would see.
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        _images[index] = image;

        var scale = new ScaleTransform(1, 1);
        _presses[index] = scale;

        var cell = new Border
        {
            Width = CellDisplaySize,
            Height = CellDisplaySize,
            Margin = new Thickness(CellGap),
            Background = new SolidColorBrush(Colors.Black),
            BorderBrush = new SolidColorBrush(isKey ? KeyBezelColor : InfoBezelColor),
            BorderThickness = new Thickness(isKey ? 2 : 1),
            CornerRadius = new CornerRadius(isKey ? 8 : 6),
            ClipToBounds = true,
            Child = image,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            Cursor = isKey ? Cursors.Hand : Cursors.Arrow,
            ToolTip = isKey
                ? $"Key {index} (0x{index:X2})\nDrop something here, or right-click to configure it."
                : $"Info cell {index} (0x{index:X2}) - no switch on the real device."
                  + "\nRight-click to choose what it shows and its colours."
        };

        _cells[index] = cell;

        // Editor gestures - right-click and drop - work on every cell, including the
        // info column. They are not device gestures, so allowing them there costs no
        // fidelity.
        cell.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            CellRightClicked?.Invoke(this, index);
        };

        cell.AllowDrop = true;

        // The effect has to be one the drag was started with, or WPF refuses the drop
        // and shows the no-entry cursor. Moving a key is started as Move; everything
        // arriving from the palette is a Copy.
        cell.DragEnter += (_, e) =>
        {
            SetDropTarget(index, true);
            e.Effects = EffectFor(e.Data);
            e.Handled = true;
        };

        cell.DragOver += (_, e) =>
        {
            e.Effects = EffectFor(e.Data);
            e.Handled = true;
        };

        cell.DragLeave += (_, _) => SetDropTarget(index, false);

        cell.Drop += (_, e) =>
        {
            SetDropTarget(index, false);
            e.Handled = true;
            CellDropped?.Invoke(this, new DeckDropEventArgs(index, e.Data));
        };

        // A press, however, is a device gesture. The info cells have no switch under
        // them, so they must never raise one.
        if (isKey)
        {
            cell.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _dragFrom = index;
                _dragOrigin = e.GetPosition(this);
                _dragging = false;
                AnimatePress(index);
            };

            // Dragging a key moves it. The press therefore has to wait for the button
            // to come back up: raising it on the way down means a key that is about to
            // be dragged has already fired its hotkey into whatever had focus.
            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;

                bool moved = _dragging;
                _dragFrom = -1;
                _dragging = false;

                if (!moved) CellPressed?.Invoke(this, index);
            };

            cell.MouseMove += (_, e) =>
            {
                if (_dragging || _dragFrom != index) return;
                if (e.LeftButton != MouseButtonState.Pressed) return;
                if (CanDragCell?.Invoke(index) != true) return;

                Vector travelled = e.GetPosition(this) - _dragOrigin;

                if (Math.Abs(travelled.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(travelled.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;

                _dragging = true;
                DragDrop.DoDragDrop(cell, new DataObject(CellDragFormat, index), DragDropEffects.Move);

                // DoDragDrop swallows the mouse-up, so the release handler never runs.
                _dragFrom = -1;
                _dragging = false;
            };
        }

        return cell;
    }

    private void AnimatePress(int index)
    {
        ScaleTransform scale = _presses[index];

        var animation = new DoubleAnimation
        {
            To = 0.94,
            Duration = TimeSpan.FromMilliseconds(60),
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
