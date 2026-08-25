using MahApps.Metro.IconPacks;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;


namespace MSL.controls
{
    /// <summary>
    /// 按照步骤 1a 或 1b 操作，然后执行步骤 2 以在 XAML 文件中使用此自定义控件。
    ///
    /// 步骤 1a) 在当前项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:MSL.controls"
    ///
    ///
    /// 步骤 1b) 在其他项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:MSL.controls;assembly=MSL.controls"
    ///
    /// 您还需要添加一个从 XAML 文件所在的项目到此项目的项目引用，
    /// 并重新生成以避免编译错误:
    ///
    ///     在解决方案资源管理器中右击目标项目，然后依次单击
    ///     “添加引用”->“项目”->[浏览查找并选择此项目]
    ///
    ///
    /// 步骤 2)
    /// 继续操作并在 XAML 文件中使用控件。
    ///
    ///     <MyNamespace:MagicControls/>
    ///
    /// </summary>

    public class MagicCard : ContentControl
    {
        static MagicCard()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MagicCard), new FrameworkPropertyMetadata(typeof(MagicCard)));
        }

        // 定义 Title 依赖属性
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(MagicCard), new PropertyMetadata(string.Empty));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static new readonly DependencyProperty PaddingProperty =
            DependencyProperty.Register("Padding", typeof(Thickness), typeof(MagicCard), new PropertyMetadata(new Thickness(10)));

        public new Thickness Padding
        {
            get { return (Thickness)GetValue(PaddingProperty); }
            set { SetValue(PaddingProperty, value); }
        }

        public static new readonly DependencyProperty BackgroundProperty =
            DependencyProperty.Register("Background", typeof(Brush), typeof(MagicCard), new PropertyMetadata(null));

        public new Brush Background
        {
            get { return (Brush)GetValue(BackgroundProperty); }
            set { SetValue(BackgroundProperty, value); }
        }
    }

    public class MagicCard1 : ContentControl
    {
        static MagicCard1()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MagicCard1), new FrameworkPropertyMetadata(typeof(MagicCard1)));
        }

        // 定义 Title 依赖属性
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(MagicCard1), new PropertyMetadata(string.Empty));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static new readonly DependencyProperty PaddingProperty =
            DependencyProperty.Register("Padding", typeof(Thickness), typeof(MagicCard1), new PropertyMetadata(new Thickness(10)));

        public new Thickness Padding
        {
            get { return (Thickness)GetValue(PaddingProperty); }
            set { SetValue(PaddingProperty, value); }
        }

        public static new readonly DependencyProperty BackgroundProperty =
            DependencyProperty.Register("Background", typeof(Brush), typeof(MagicCard1), new PropertyMetadata(null));

        public new Brush Background
        {
            get { return (Brush)GetValue(BackgroundProperty); }
            set { SetValue(BackgroundProperty, value); }
        }
    }

    public class MagicScrollViewer : ItemsControl
    {
        static MagicScrollViewer()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MagicScrollViewer), new FrameworkPropertyMetadata(typeof(MagicScrollViewer)));
        }
    }
    
    public class MagicListBox : ListBox
    {
        static MagicListBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MagicListBox), new FrameworkPropertyMetadata(typeof(MagicListBox)));
        }
    }

    public class MagicListBox1 : ListBox
    {
        static MagicListBox1()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MagicListBox1), new FrameworkPropertyMetadata(typeof(MagicListBox1)));
        }
    }

    public class ListBoxSideMenu : Control
    {
        public ImageSource Icon { get; set; }

        public static readonly DependencyProperty IconKindProperty =
        DependencyProperty.Register(
            "IconKind", typeof(PackIconMaterialKind),
            typeof(ListBoxSideMenu),
            new PropertyMetadata(PackIconMaterialKind.None));

        public PackIconMaterialKind IconKind
        {
            get { return (PackIconMaterialKind)GetValue(IconKindProperty); }
            set { SetValue(IconKindProperty, value); }
        }

        public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            "Text",
            typeof(string),
            typeof(ListBoxSideMenu),
            new PropertyMetadata(default(string)));

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }
    }

    /// <summary>
    /// 附加行为集合，用于给现有控件（如侧边栏 ListBox）附加通用交互逻辑，
    /// 避免在每个窗口的代码隐藏里重复写同样的事件处理。
    /// </summary>
    public static class ListBoxBehaviors
    {
        /// <summary>
        /// 禁用 ListBox 默认的“鼠标左键按住拖动时连续变更选中项”行为，只保留单击选择。
        /// 侧边栏（SideMenu）快速拖动会连续触发 SelectionChanged，导致内容区被高频切换，
        /// 期间子页面的进场动画和 ScrollViewer 还没完成布局就被卸载/重建，偶发把 Infinity
        /// 赋给 ScrollBar.Value 触发绑定错误。挂上这个附加属性即可屏蔽拖动选择。
        /// </summary>
        public static readonly DependencyProperty DisableDragSelectionProperty =
            DependencyProperty.RegisterAttached(
                "DisableDragSelection",
                typeof(bool),
                typeof(ListBoxBehaviors),
                new PropertyMetadata(false, OnDisableDragSelectionChanged));

        public static bool GetDisableDragSelection(ListBox listBox) => (bool)listBox.GetValue(DisableDragSelectionProperty);

        public static void SetDisableDragSelection(ListBox listBox, bool value) => listBox.SetValue(DisableDragSelectionProperty, value);

        // 按压缩放动画的参数，与原 XAML 中的手感保持一致
        private const double PressedScale = 0.92;
        private static readonly Duration PressDuration = new Duration(TimeSpan.FromSeconds(0.08));
        private static readonly Duration ReleaseDuration = new Duration(TimeSpan.FromSeconds(0.15));

        // 用弱引用持有，避免按住时窗体被关闭导致 ListBoxItem 及其可视树被静态字段拖住不放
        private static WeakReference<ListBoxItem> _pressedItem;

        private static ListBoxItem PressedItem =>
            _pressedItem != null && _pressedItem.TryGetTarget(out var item) ? item : null;

        private static void UpdatePressedItem(ListBoxItem item)
        {
            var previous = PressedItem;
            if (ReferenceEquals(previous, item))
                return;

            if (previous != null)
                AnimatePress(previous, false);

            _pressedItem = item == null ? null : new WeakReference<ListBoxItem>(item);

            if (item != null)
                AnimatePress(item, true);
        }

        /// <summary>
        /// 直接对模板里的 ScaleTransform 做动画。
        /// 这里不用 XAML 的 EventTrigger/DataTrigger：DisableDragSelection 必须吞掉
        /// PreviewMouseLeftButtonDown 才能屏蔽拖动选择，而隧道事件一旦 Handled，
        /// WPF 就不再抛出冒泡的 Mouse.MouseDown/MouseUp，EventTrigger 收不到事件；
        /// 而触发器里的绑定路径和 Storyboard.TargetName 都是运行时按字符串解析的，
        /// 解析失败不报错也不动画，难以排查。改成在代码里拿到 ScaleTransform 直接动画，
        /// 出问题时是编译错误或空引用，行为明确。
        /// </summary>
        private static void AnimatePress(ListBoxItem item, bool pressed)
        {
            var scale = FindPressScaleTransform(item);
            if (scale == null)
                return;

            var to = pressed ? PressedScale : 1.0;
            var duration = pressed ? PressDuration : ReleaseDuration;
            var animation = new DoubleAnimation(to, duration)
            {
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        /// <summary>
        /// 取模板根 Border 上的 ScaleTransform，没有则不做动画（其他样式的 ListBoxItem 不受影响）。
        /// </summary>
        private static ScaleTransform FindPressScaleTransform(ListBoxItem item)
        {
            if (VisualTreeHelper.GetChildrenCount(item) == 0)
                return null;

            if (VisualTreeHelper.GetChild(item, 0) is not UIElement root)
                return null;

            if (root.RenderTransform is not ScaleTransform scale)
                return null;

            // 模板里的 Freezable 可能以冻结状态交付，冻结对象上 BeginAnimation 会抛异常，换成可写副本
            if (scale.IsFrozen)
            {
                scale = scale.Clone();
                root.RenderTransform = scale;
            }

            return scale;
        }

        private static void OnDisableDragSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox listBox)
                return;

            listBox.PreviewMouseLeftButtonDown -= ListBox_PreviewMouseLeftButtonDown;
            listBox.PreviewMouseLeftButtonUp -= ListBox_PreviewMouseLeftButtonUp;
            listBox.PreviewMouseMove -= ListBox_PreviewMouseMove;
            listBox.MouseLeave -= ListBox_MouseLeave;
            if ((bool)e.NewValue)
            {
                listBox.PreviewMouseLeftButtonDown += ListBox_PreviewMouseLeftButtonDown;
                listBox.PreviewMouseLeftButtonUp += ListBox_PreviewMouseLeftButtonUp;
                listBox.PreviewMouseMove += ListBox_PreviewMouseMove;
                listBox.MouseLeave += ListBox_MouseLeave;
            }
        }

        private static void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsScrollBarsHitTest(e.OriginalSource as DependencyObject))
                return;

            UpdatePressedItem(FindParent<ListBoxItem>(e.OriginalSource as DependencyObject));
            e.Handled = true;
        }

        private static void ListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            // 按住后滑到别的项上就撤掉按压反馈，避免松手时还停在按下状态
            var pressed = PressedItem;
            if (pressed != null && !ReferenceEquals(FindParent<ListBoxItem>(e.OriginalSource as DependencyObject), pressed))
                UpdatePressedItem(null);

            e.Handled = true;
        }

        private static void ListBox_MouseLeave(object sender, MouseEventArgs e)
        {
            UpdatePressedItem(null);
        }

        private static void ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            UpdatePressedItem(null);

            if (sender is not ListBox listBox)
                return;
            if (IsScrollBarsHitTest(e.OriginalSource as DependencyObject))
                return;

            var pos = e.GetPosition(listBox);
            var hit = VisualTreeHelper.HitTest(listBox, pos);
            var listBoxItem = FindParent<ListBoxItem>(hit.VisualHit as DependencyObject);
            if (listBoxItem != null)
            {
                var item = listBoxItem.DataContext ?? listBoxItem;
                var index = listBox.Items.IndexOf(item);
                if (index >= 0)
                    listBox.SelectedItem = item;
            }
        }

        private static bool IsScrollBarsHitTest(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ScrollBar)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T typed)
                    return typed;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }

    public class TabControlHeader : Control
    {
        static TabControlHeader()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TabControlHeader),
                new FrameworkPropertyMetadata(typeof(TabControlHeader)));
        }

        public static readonly DependencyProperty IconKindProperty =
        DependencyProperty.Register(
            "IconKind", typeof(PackIconMaterialKind),
            typeof(TabControlHeader),
            new PropertyMetadata(PackIconMaterialKind.Home));

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                "Text", typeof(string),
                typeof(TabControlHeader),
                new PropertyMetadata(default(string)));

        public PackIconMaterialKind IconKind
        {
            get { return (PackIconMaterialKind)GetValue(IconKindProperty); }
            set { SetValue(IconKindProperty, value); }
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }
    }

    /* MagicGrowlPanel 用处不多不大，暂时弃用
    public class MagicGrowlPanel : Control
    {
        static MagicGrowlPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MagicGrowlPanel), new FrameworkPropertyMetadata(typeof(MagicGrowlPanel)));
        }

        public MagicGrowlPanel()
        {
            this.Loaded += GrowlPanelControl_Loaded;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            // 获取模板中的 GrowlPanel 和 ScrollViewer
            GrowlPanel = GetTemplateChild("GrowlPanel") as StackPanel;
            GrowlScrollViewer = GetTemplateChild("GrowlScrollViewer") as ScrollViewer;
        }

        private void GrowlPanelControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.TemplatedParent is Window window)
            {
                window.Activated += (s, args) => HandyControl.Controls.Growl.SetGrowlParent(this.GrowlPanel, true);
                window.Deactivated += (s, args) => HandyControl.Controls.Growl.SetGrowlParent(this.GrowlPanel, false);
            }
        }

        public StackPanel GrowlPanel { get; private set; }
        public ScrollViewer GrowlScrollViewer { get; private set; }
    }
    */
}
