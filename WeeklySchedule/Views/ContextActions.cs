using System.Windows.Input;
using WeeklySchedule.Utilities;

namespace WeeklySchedule.Views;

// Attach к конкретному элементу шаблона: команды всегда читаются из его текущего
// BindingContext. Нативные подписки живут только пока элемент загружен.
public static class ContextActions
{
    public static readonly BindableProperty TapCommandProperty = BindableProperty.CreateAttached(
        "TapCommand", typeof(ICommand), typeof(ContextActions), null, propertyChanged: Attach);
    public static readonly BindableProperty MenuCommandProperty = BindableProperty.CreateAttached(
        "MenuCommand", typeof(ICommand), typeof(ContextActions), null, propertyChanged: Attach);
    public static readonly BindableProperty ParameterProperty = BindableProperty.CreateAttached(
        "Parameter", typeof(object), typeof(ContextActions), null);
    private static readonly BindableProperty SubscriptionProperty = BindableProperty.CreateAttached(
        "Subscription", typeof(Subscription), typeof(ContextActions), null);
    public static ICommand? GetTapCommand(BindableObject view) => (ICommand?)view.GetValue(TapCommandProperty);
    public static void SetTapCommand(BindableObject view, ICommand? value) => view.SetValue(TapCommandProperty, value);
    public static ICommand? GetMenuCommand(BindableObject view) => (ICommand?)view.GetValue(MenuCommandProperty);
    public static void SetMenuCommand(BindableObject view, ICommand? value) => view.SetValue(MenuCommandProperty, value);
    public static object? GetParameter(BindableObject view) => view.GetValue(ParameterProperty);
    public static void SetParameter(BindableObject view, object? value) => view.SetValue(ParameterProperty, value);

    private static void Attach(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is View view && view.GetValue(SubscriptionProperty) == null)
            view.SetValue(SubscriptionProperty, new Subscription(view));
    }

    private sealed class Subscription
    {
        private readonly View _view;
        private long _suppressTapUntil;
        private readonly HoldGestureState _hold = new();
#if ANDROID
        private Android.Views.View? _native;
#elif WINDOWS
        private Microsoft.UI.Xaml.FrameworkElement? _native;
#endif
        public Subscription(View view)
        {
            _view = view;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
#if ANDROID
                if (_hold.Held || _hold.Cancelled) return;
#endif
                if (Environment.TickCount64 >= _suppressTapUntil) Execute(GetTapCommand(_view));
            };
            view.GestureRecognizers.Add(tap);
            view.Loaded += (_, _) => Connect();
            view.Unloaded += (_, _) => Disconnect();
            view.HandlerChanging += (_, _) => Disconnect();
            view.HandlerChanged += (_, _) => { if (view.IsLoaded) Connect(); };
            view.BindingContextChanged += (_, _) => _hold.Cancel();
            if (view.IsLoaded) Connect();
        }

        private void Execute(ICommand? command)
        {
            var parameter = GetParameter(_view);
            if (command?.CanExecute(parameter) == true) command.Execute(parameter);
        }

        private void Menu()
        {
            _suppressTapUntil = Environment.TickCount64 + 700;
            Execute(GetMenuCommand(_view));
        }

        private void Connect()
        {
            Disconnect();
#if ANDROID
            _native = _view.Handler?.PlatformView as Android.Views.View;
            if (_native != null) _native.Touch += OnTouch;
#elif WINDOWS
            _native = _view.Handler?.PlatformView as Microsoft.UI.Xaml.FrameworkElement;
            if (_native != null)
            {
                _native.RightTapped += OnRightTapped;
                _native.Holding += OnHolding;
            }
#endif
        }

        private void Disconnect()
        {
            _hold.Cancel();
#if ANDROID
            if (_native != null) _native.Touch -= OnTouch;
            _native = null;
#elif WINDOWS
            if (_native != null)
            {
                _native.RightTapped -= OnRightTapped;
                _native.Holding -= OnHolding;
            }
            _native = null;
#endif
        }

#if ANDROID
        private void OnTouch(object? sender, Android.Views.View.TouchEventArgs e)
        {
            var motion = e.Event;
            if (motion == null) return;
            // Не сбрасываем Handled: до нас событие мог обработать MAUI.
            // Родители по-прежнему могут перехватить MOVE и прислать CANCEL.
            switch (motion.ActionMasked)
            {
                case Android.Views.MotionEventActions.Down:
                    var version = _hold.Begin(motion.GetX(), motion.GetY());
                    _view.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Android.Views.ViewConfiguration.LongPressTimeout), () =>
                    {
                        if (!_view.IsLoaded || !_hold.TryHold(version)) return;
                        Menu();
                    });
                    break;
                case Android.Views.MotionEventActions.Move:
                    var slop = Android.Views.ViewConfiguration.Get(_native!.Context!)?.ScaledTouchSlop ?? 16;
                    _hold.Move(motion.GetX(), motion.GetY(), slop);
                    break;
                case Android.Views.MotionEventActions.Up:
                    if (_hold.End()) { _suppressTapUntil = Environment.TickCount64 + 700; e.Handled = true; }
                    break;
                case Android.Views.MotionEventActions.Cancel:
                case Android.Views.MotionEventActions.PointerDown:
                    _hold.Cancel();
                    break;
            }
        }
#elif WINDOWS
        private void OnRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            Menu();
        }
        private void OnHolding(object sender, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState != Microsoft.UI.Input.HoldingState.Started) return;
            e.Handled = true;
            Menu();
        }
#endif
    }
}
