/*************************************************************************/
/*              This file is substantially derived from:                 */
/*                           GODOT ENGINE                                */
/*                      https://godotengine.org                          */
/*************************************************************************/
/* Copyright (c) 2007-2021 Juan Linietsky, Ariel Manzur.                 */
/* Copyright (c) 2014-2021 Godot Engine contributors (cf. AUTHORS.md).   */

/* Permission is hereby granted, free of charge, to any person obtaining */
/* a copy of this software and associated documentation files (the       */
/* "Software"), to deal in the Software without restriction, including   */
/* without limitation the rights to use, copy, modify, merge, publish,   */
/* distribute, sublicense, and/or sell copies of the Software, and to    */
/* permit persons to whom the Software is furnished to do so, subject to */
/* the following conditions:                                             */

/* The above copyright notice and this permission notice shall be        */
/* included in all copies or substantial portions of the Software.       */

/* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,       */
/* EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF    */
/* MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.*/
/* IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY  */
/* CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,  */
/* TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE     */
/* SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.                */
/*************************************************************************/

using System;
using Godot;

/// <summary>
///   A reimplementation of Window for a much more customized style and functionality. Suitable for general use
///   or as a base class for any custom window dialog derived types.
/// </summary>
/// <remarks>
///   <para>
///     This uses Tool attribute to make this class be run in the Godot editor for live feedback as this class
///     handles UI visuals extensively through code. Not necessary but very helpful when editing scenes involving
///     any custom dialogs.
///     NOTE: should always be commented in master branch to avoid Godot breaking exported properties. Uncomment this
///     only locally if needed.
///   </para>
/// </remarks>
/// TODO: see https://github.com/Revolutionary-Games/Thrive/issues/2751
/// [Tool]
public partial class CustomDialog : Popup, ICustomPopup
{
    private string windowTitle = string.Empty;
    private string translatedWindowTitle = string.Empty;

    private bool closeHovered;

    private Vector2I dragOffset;
    private Vector2I dragOffsetFar;

#pragma warning disable CA2213
    private TextureButton? closeButton;

    private StyleBox customPanel = null!;
    private StyleBox titleBarPanel = null!;
    private StyleBox closeButtonHighlight = null!;

    private Font? titleFont;
#pragma warning restore CA2213
    private Color titleColor;

    private DragType dragType = DragType.None;

    private int titleBarHeight;
    private int titleHeight;
    private int scaleBorderSize;
    private int customMargin;
    private bool showCloseButton = true;
    private bool decorate = true;

    /// <summary>
    ///   NOTE: This is only emitted WHEN the close button (top right corner) is pressed, this doesn't account
    ///   for any other hiding behaviors.
    /// </summary>
    [Signal]
    public delegate void ClosedEventHandler();

    [Flags]
    private enum DragType
    {
        None = 0,
        Move = 1,
        ResizeTop = 1 << 1,
        ResizeRight = 1 << 2,
        ResizeBottom = 1 << 3,
        ResizeLeft = 1 << 4,
    }

    /// <summary>
    ///   The text displayed in the window's title bar.
    /// </summary>
    [Export]
    public string WindowTitle
    {
        get => windowTitle;
        set
        {
            if (windowTitle == value)
                return;

            windowTitle = value;
            translatedWindowTitle = TranslationServer.Translate(value);

            // TODO: ???
            // MinimumSizeChanged();
            // QueueRedraw();
        }
    }

    /// <summary>
    ///   If true, the dialog window size is locked to the size of the viewport.
    /// </summary>
    [Export]
    public bool FullRect { get; set; }

    /// <summary>
    ///   If true, the user can resize the window.
    /// </summary>
    [Export]
    public bool Resizable { get; set; }

    /// <summary>
    ///   If true, the user can move the window around the viewport by dragging the titlebar.
    /// </summary>
    [Export]
    public bool Movable { get; set; } = true;

    /// <summary>
    ///   If true, the window's position is clamped inside the screen so it doesn't go out of bounds.
    /// </summary>
    [Export]
    public bool BoundToScreenArea { get; set; } = true;

    [Export]
    public bool ExclusiveAllowCloseOnEscape { get; set; } = true;

    [Export]
    public bool ShowCloseButton
    {
        get => showCloseButton;
        set
        {
            if (showCloseButton == value)
                return;

            showCloseButton = value;
            SetupCloseButton();
        }
    }

    /// <summary>
    ///   Sets whether the window frame should be visible.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     Note: Doesn't handle close button. That's still controlled by <see cref="ShowCloseButton"/>.
    ///   </para>
    /// </remarks>
    [Export]
    public bool Decorate
    {
        get => decorate;
        set
        {
            if (decorate == value)
                return;

            decorate = value;

            // TODO: doesn't this need to adjust titleBarHeight value here as that's only set on tree entry?
            // Update(); TODO: ???
        }
    }

    public override void _EnterTree()
    {
        // To make popup rect readjustment react to window resizing
        GetTree().Root.Connect("size_changed",new Callable(this,nameof(ApplyRectSettings)));

        customPanel = GetThemeStylebox("custom_panel", "Window");
        titleBarPanel = GetThemeStylebox("custom_titlebar", "Window");
        titleBarHeight = decorate ? GetThemeConstant("custom_titlebar_height", "Window") : 0;
        titleFont = GetThemeFont("custom_title_font", "Window");
        titleHeight = GetThemeConstant("custom_title_height", "Window");
        titleColor = GetThemeColor("custom_title_color", "Window");
        closeButtonHighlight = GetThemeStylebox("custom_close_highlight", "Window");
        scaleBorderSize = GetThemeConstant("custom_scaleBorder_size", "Window");
        customMargin = decorate ? GetThemeConstant("custom_margin", "Dialogs") : 0;

        base._EnterTree();
    }

    public override void _ExitTree()
    {
        GetTree().Root.Disconnect("size_changed", new Callable(this, nameof(ApplyRectSettings)));

        base._ExitTree();
    }

    public override void _Notification(int what)
    {
        switch (what)
        {
            case (int)NotificationReady:
            {
                SetupCloseButton();
                UpdateChildRects();
                ApplyRectSettings();
                break;
            }

            /* TODO: Redacted?
            case (int)NotificationResized:
            {
                UpdateChildRects();
                ApplyRectSettings();
                break;
            }
            */

            case (int)NotificationVisibilityChanged:
            {
                if (Visible)
                {
                    ApplyRectSettings();
                    OnShown();
                }
                else
                {
                    OnHidden();
                }

                UpdateChildRects();
                break;
            }

            /* TODO: No equivalent
            case NotificationMouseExit:
            {
                // Reset the mouse cursor when leaving the resizable window border.
                if (Resizable && dragType == DragType.None)
                {
                    if (MouseDefaultCursorShape != CursorShape.Arrow)
                        MouseDefaultCursorShape = CursorShape.Arrow;
                }

                break;
            }
            */

            case (int)NotificationTranslationChanged:
            {
                translatedWindowTitle = TranslationServer.Translate(windowTitle);
                break;
            }
        }
    }

    /*
    public override void _Draw()
    {
        if (!Decorate)
            return;

        // Draw background panels
        DrawStyleBox(customPanel, new Rect2(
            new Vector2(0, -titleBarHeight), new Vector2(Size.X, Size.Y + titleBarHeight)));

        DrawStyleBox(titleBarPanel, new Rect2(
            new Vector2(3, -titleBarHeight + 3), new Vector2(Size.X - 6, titleBarHeight - 3)));

        // Draw title in the title bar
        var fontHeight = titleFont!.GetHeight() - titleFont.GetDescent() * 2;

        var titlePosition = new Vector2(
            (Size.X - titleFont.GetStringSize(translatedWindowTitle).X) / 2, (-titleHeight + fontHeight) / 2);

        DrawString(titleFont, titlePosition, translatedWindowTitle, titleColor,
            (int)(Size.X - customPanel.GetMinimumSize().X));

        // Draw close button highlight
        if (closeHovered)
        {
            DrawStyleBox(closeButtonHighlight, closeButton!.GetRect());
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Handle title bar dragging
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            if (mouseButton.ButtonPressed && Movable)
            {
                // Begin a possible dragging operation
                dragType = DragHitTest(new Vector2(mouseButton.Position.X, mouseButton.Position.Y));

                if (dragType != DragType.None)
                    dragOffset = GetGlobalMousePosition() - Position;

                dragOffsetFar = Position + Size - GetGlobalMousePosition();
            }
            else if (dragType != DragType.None && !mouseButton.ButtonPressed)
            {
                // End a dragging operation
                dragType = DragType.None;
            }
        }

        if (@event is InputEventMouseMotion mouseMotion)
        {
            if (dragType == DragType.None)
            {
                HandlePreviewDrag(mouseMotion);
            }
            else
            {
                HandleActiveDrag();
            }
        }
    }

    /// <summary>
    ///   This is overriden so mouse position could take the titlebar into account due to it being drawn
    ///   outside of the normal Control's rect bounds.
    /// </summary>
    public override bool HasPoint(Vector2 point)
    {
        var rect = new Rect2(Vector2.Zero, Size);

        // Enlarge upwards for title bar
        var adjustedRect = new Rect2(
            new Vector2(rect.Position.X, rect.Position.Y - titleBarHeight),
            new Vector2(rect.Size.X, rect.Size.Y + titleBarHeight));

        // Inflate by the resizable border thickness
        if (Resizable)
        {
            adjustedRect = new Rect2(
                new Vector2(adjustedRect.Position.X - scaleBorderSize, adjustedRect.Position.Y - scaleBorderSize),
                new Vector2(adjustedRect.Size.X + scaleBorderSize * 2, adjustedRect.Size.Y + scaleBorderSize * 2));
        }

        return adjustedRect.HasPoint(point);
    }

    /// <summary>
    ///   Overrides the minimum size to account for default elements (e.g title, close button, margin) rect size
    ///   and for the other custom added contents on the window.
    /// </summary>
    public override Vector2I _GetMinimumSize()
    {
        var buttonWidth = closeButton?.GetCombinedMinimumSize().X;
        var titleWidth = titleFont?.GetStringSize(translatedWindowTitle).X;
        var buttonArea = buttonWidth + (buttonWidth / 2);

        var contentSize = Vector2.Zero;

        for (int i = 0; i < GetChildCount(); ++i)
        {
            var child = GetChildOrNull<Control>(i);

            if (child == null || child == closeButton || child.IsSetAsTopLevel())
                continue;

            var childMinSize = child.GetCombinedMinimumSize();

            contentSize = new Vector2(
                Mathf.Max(childMinSize.X, contentSize.X),
                Mathf.Max(childMinSize.Y, contentSize.Y));
        }

        // Re-decide whether the largest rect is the default elements' or the contents'
        return new Vector2(Mathf.Max(2 * buttonArea.GetValueOrDefault() + titleWidth.GetValueOrDefault(),
            contentSize.X + customMargin * 2), contentSize.Y + customMargin * 2);
    }
    */

    public void PopupFullRect()
    {
        // Popup_(GetFullRect());
    }

    public virtual void CustomShow()
    {
        // TODO: implement default show animation(?)
        // ShowModal(Exclusive);
    }

    public virtual void CustomHide()
    {
        // TODO: add proper close animation
        // Hide();
    }

    protected virtual void OnShown()
    {
    }

    /// <summary>
    ///   Called after popup is made invisible.
    /// </summary>
    protected virtual void OnHidden()
    {
        closeHovered = false;
    }

    protected Rect2 GetFullRect()
    {
        var viewportSize = GetScreenSize();
        return new Rect2(new Vector2(0, titleBarHeight), new Vector2(viewportSize.X, viewportSize.Y));
    }

    /// <summary>
    ///   Evaluates what kind of drag type is being done on the window based on the current mouse position.
    /// </summary>
    private DragType DragHitTest(Vector2 position)
    {
        var result = DragType.None;

        if (Resizable)
        {
            if (position.Y < (-titleBarHeight + scaleBorderSize))
            {
                result = DragType.ResizeTop;
            }
            else if (position.Y >= (Size.Y - scaleBorderSize))
            {
                result = DragType.ResizeBottom;
            }

            if (position.X < scaleBorderSize)
            {
                result |= DragType.ResizeLeft;
            }
            else if (position.X >= (Size.X - scaleBorderSize))
            {
                result |= DragType.ResizeRight;
            }
        }

        if (result == DragType.None && position.Y < 0)
            result = DragType.Move;

        return result;
    }

    /*
    /// <summary>
    ///   Updates the cursor icon while moving along the borders.
    /// </summary>
    private void HandlePreviewDrag(InputEventMouseMotion mouseMotion)
    {
        var cursor = CursorShape.Arrow;

        if (Resizable)
        {
            var previewDragType = DragHitTest(new Vector2(mouseMotion.Position.X, mouseMotion.Position.Y));

            switch (previewDragType)
            {
                case DragType.ResizeTop:
                case DragType.ResizeBottom:
                    cursor = CursorShape.Vsize;
                    break;
                case DragType.ResizeLeft:
                case DragType.ResizeRight:
                    cursor = CursorShape.Hsize;
                    break;
                case DragType.ResizeTop | DragType.ResizeLeft:
                case DragType.ResizeBottom | DragType.ResizeRight:
                    cursor = CursorShape.Fdiagsize;
                    break;
                case DragType.ResizeTop | DragType.ResizeRight:
                case DragType.ResizeBottom | DragType.ResizeLeft:
                    cursor = CursorShape.Bdiagsize;
                    break;
            }
        }

        if (GetCursorShape() != cursor)
            MouseDefaultCursorShape = cursor;
    }
    */

    private Vector2 GetScreenSize()
    {
        return GetViewport().GetVisibleRect().Size;
    }

    /// <summary>
    ///   Updates the window position and size while in a dragging operation.
    /// </summary>
    private void HandleActiveDrag()
    {
        var globalMousePos = GetMousePosition().ToVector2I();

        var minSize = Vector2I.Zero; // GetCombinedMinimumSize(); TODO: ???

        var newPosition = Position;
        var newSize = Size;

        if (dragType == DragType.Move)
        {
            newPosition = globalMousePos - dragOffset;
        }
        else
        {
            // Handle border dragging
            var screenSize = GetScreenSize();

            if (dragType.HasFlag(DragType.ResizeTop))
            {
                var bottom = Position.Y + Size.Y;
                var maxY = bottom - minSize.Y;

                newPosition.Y = Mathf.Clamp(globalMousePos.Y - dragOffset.Y, titleBarHeight, maxY);
                newSize.Y = bottom - newPosition.Y;
            }
            else if (dragType.HasFlag(DragType.ResizeBottom))
            {
                newSize.Y = (int)Mathf.Min(globalMousePos.Y - newPosition.Y + dragOffsetFar.Y, screenSize.Y - newPosition.Y);
            }

            if (dragType.HasFlag(DragType.ResizeLeft))
            {
                var right = Position.X + Size.X;
                var maxX = right - minSize.X;

                newPosition.X = Mathf.Clamp(globalMousePos.X - dragOffset.X, 0, maxX);
                newSize.X = right - newPosition.X;
            }
            else if (dragType.HasFlag(DragType.ResizeRight))
            {
                newSize.X = (int)Mathf.Min(globalMousePos.X - newPosition.X + dragOffsetFar.X, screenSize.X - newPosition.X);
            }
        }

        Position = newPosition;
        Size = newSize;

        if (BoundToScreenArea)
            FixRect();
    }

    /// <summary>
    ///   Applies final adjustments to the window's rect.
    /// </summary>
    private void FixRect()
    {
        var screenSize = GetScreenSize();

        // Clamp position to ensure window stays inside the screen
        Position = new Vector2I(
            (int)Mathf.Clamp(Position.X, 0, screenSize.X - Size.X),
            (int)Mathf.Clamp(Position.Y, titleBarHeight, screenSize.Y - Size.Y));

        if (Resizable)
        {
            // Size can't be bigger than the viewport
            Size = new Vector2I(
                (int)Mathf.Min(Size.X, screenSize.X), (int)Mathf.Min(Size.Y, screenSize.Y - titleBarHeight));
        }
    }

    private void SetupCloseButton()
    {
        /* TODO: First make it appear
        if (!ShowCloseButton)
        {
            if (closeButton != null)
            {
                RemoveChild(closeButton);
                closeButton = null;
            }

            return;
        }

        if (closeButton != null)
            return;

        var closeColor = GetColor("custom_close_color", "Window");

        closeButton = new TextureButton
        {
            Expand = true,
            CustomMinimumSize = new Vector2(14, 14),
            SelfModulate = closeColor,
            MouseFilter = MouseFilterEnum.Pass,
            TextureNormal = GetIcon("custom_close", "Window"),
        };

        closeButton.SetAnchorsPreset(LayoutPreset.TopRight);

        closeButton.Position = new Vector2(
            -GetConstant("custom_close_h_ofs", "Window"),
            -GetConstant("custom_close_v_ofs", "Window"));

        closeButton.Connect("mouse_entered",new Callable(this,nameof(OnCloseButtonMouseEnter)));
        closeButton.Connect("mouse_exited",new Callable(this,nameof(OnCloseButtonMouseExit)));
        closeButton.Connect("pressed",new Callable(this,nameof(OnCloseButtonPressed)));

        AddChild(closeButton);
        */
    }

    private void UpdateChildRects()
    {
        var childPos = new Vector2(customMargin, customMargin);
        var childSize = new Vector2(Size.X - customMargin * 2, Size.Y - customMargin * 2);

        for (int i = 0; i < GetChildCount(); ++i)
        {
            var child = GetChildOrNull<Control>(i);

            if (child == null || child == closeButton || child.TopLevel)
                continue;

            child.Position = childPos;
            child.Size = childSize;
        }
    }

    private void SetToFullRect()
    {
        var fullRect = GetFullRect().Size.ToVector2I();

        Position = new Vector2I(0, titleBarHeight);
        Size = new Vector2I(fullRect.X, fullRect.Y - titleBarHeight);
    }

    private void ApplyRectSettings()
    {
        if (FullRect)
            SetToFullRect();

        if (BoundToScreenArea)
            FixRect();
    }

    private void OnCloseButtonMouseEnter()
    {
        closeHovered = true;
        // Update();
    }

    private void OnCloseButtonMouseExit()
    {
        closeHovered = false;
        // Update();
    }

    private void OnCloseButtonPressed()
    {
        GUICommon.Instance.PlayButtonPressSound();
        CustomHide();
        EmitSignal(nameof(Closed));
    }
}
