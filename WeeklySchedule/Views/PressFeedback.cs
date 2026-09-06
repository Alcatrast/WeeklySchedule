using WeeklySchedule.Utilities;

namespace WeeklySchedule.Views;

// Отклик на нажатие для элементов, которые кнопками не являются: Border + Grid
// с TapGestureRecognizer. У них на Android нет ни ripple, ни состояния Pressed,
// поэтому зажатый пункт меню выглядит как обычная строка.
//
// Плумбинг тот же, что в ContextActions: нативные подписки живут только пока
// элемент загружен. e.Handled намеренно не трогаем — иначе сломаются
// TapGestureRecognizer и долгое нажатие ContextActions.
public static class PressFeedback
{
    private const double PressedOpacity = 0.75;
    private const double PressedScale = 0.97;

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled", typeof(bool), typeof(PressFeedback), false, propertyChanged: OnIsEnabledChanged);
    // Android не зовет OnTouchListener родителя, если касание забрал ребенок. Там, где
    // жесты висят на внутреннем элементе, подписываться надо на него, а рисовать отклик
    // на внешнем: Target указывает, что именно затемнять.
    public static readonly BindableProperty TargetProperty = BindableProperty.CreateAttached(
        "Target", typeof(View), typeof(PressFeedback), null);
    private static readonly BindableProperty SubscriptionProperty = BindableProperty.CreateAttached(
        "Subscription", typeof(Subscription), typeof(PressFeedback), null);

    public static bool GetIsEnabled(BindableObject view) => (bool)view.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject view, bool value) => view.SetValue(IsEnabledProperty, value);
    public static View? GetTarget(BindableObject view) => (View?)view.GetValue(TargetProperty);
    public static void SetTarget(BindableObject view, View? value) => view.SetValue(TargetProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view) return;
        if (newValue is true)
        {
            if (view.GetValue(SubscriptionProperty) == null)
                view.SetValue(SubscriptionProperty, new Subscription(view));
        }
        else if (view.GetValue(SubscriptionProperty) is Subscription existing)
        {
            existing.Dispose();
            view.SetValue(SubscriptionProperty, null);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly View _view;
        private bool _pressed;
        // Собственную прозрачность элемента запоминаем: у прошедшей пары она 0.5,
        // и возврат «в единицу» стер бы это состояние.
        private double _restOpacity = 1;
#if ANDROID
        private Android.Views.View? _native;
        private float _downX, _downY;
#elif WINDOWS
        private Microsoft.UI.Xaml.FrameworkElement? _native;
#endif
        public Subscription(View view)
        {
            _view = view;
            view.Loaded += OnLoaded;
            view.Unloaded += OnUnloaded;
            view.HandlerChanging += OnHandlerChanging;
            view.HandlerChanged += OnHandlerChanged;
            if (view.IsLoaded) Connect();
        }

        private void OnLoaded(object? sender, EventArgs e) => Connect();
        private void OnUnloaded(object? sender, EventArgs e) => Disconnect();
        private void OnHandlerChanging(object? sender, HandlerChangingEventArgs e) => Disconnect();
        private void OnHandlerChanged(object? sender, EventArgs e) { if (_view.IsLoaded) Connect(); }

        public void Dispose()
        {
            Disconnect();
            _view.Loaded -= OnLoaded;
            _view.Unloaded -= OnUnloaded;
            _view.HandlerChanging -= OnHandlerChanging;
            _view.HandlerChanged -= OnHandlerChanged;
        }

        // Цель читается на каждое нажатие: у переиспользуемых шаблонов она меняется
        // вместе с BindingContext.
        private View Visual => GetTarget(_view) ?? _view;

        private void Press()
        {
            if (_pressed) return;
            _pressed = true;
            var visual = Visual;
            _restOpacity = visual.Opacity;
            visual.Opacity = _restOpacity * PressedOpacity;
            SafeFireAndForget.Run(() => visual.ScaleToAsync(PressedScale, 60, Easing.CubicOut));
        }

        // Возврат безусловный: при Cancel во время прокрутки элемент не должен
        // залипнуть затемненным, даже если анимация нажатия еще не закончилась.
        private void Release()
        {
            if (!_pressed) return;
            _pressed = false;
            var visual = Visual;
            visual.Opacity = _restOpacity;
            SafeFireAndForget.Run(() => visual.ScaleToAsync(1.0, 90, Easing.CubicOut));
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
                _native.PointerPressed += OnPointerPressed;
                _native.PointerReleased += OnPointerReleased;
                _native.PointerExited += OnPointerReleased;
                _native.PointerCanceled += OnPointerReleased;
                _native.PointerCaptureLost += OnPointerReleased;
            }
#endif
        }

        private void Disconnect()
        {
            Release();
#if ANDROID
            if (_native != null) _native.Touch -= OnTouch;
            _native = null;
#elif WINDOWS
            if (_native != null)
            {
                _native.PointerPressed -= OnPointerPressed;
                _native.PointerReleased -= OnPointerReleased;
                _native.PointerExited -= OnPointerReleased;
                _native.PointerCanceled -= OnPointerReleased;
                _native.PointerCaptureLost -= OnPointerReleased;
            }
            _native = null;
#endif
        }

#if ANDROID
        private void OnTouch(object? sender, Android.Views.View.TouchEventArgs e)
        {
            var motion = e.Event;
            if (motion == null) return;
            switch (motion.ActionMasked)
            {
                case Android.Views.MotionEventActions.Down:
                    _downX = motion.GetX();
                    _downY = motion.GetY();
                    Press();
                    break;
                case Android.Views.MotionEventActions.Move:
                    var slop = Android.Views.ViewConfiguration.Get(_native!.Context!)?.ScaledTouchSlop ?? 16;
                    if (Math.Abs(motion.GetX() - _downX) > slop || Math.Abs(motion.GetY() - _downY) > slop) Release();
                    break;
                case Android.Views.MotionEventActions.Up:
                case Android.Views.MotionEventActions.Cancel:
                case Android.Views.MotionEventActions.PointerDown:
                    Release();
                    break;
            }
        }
#elif WINDOWS
        private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Press();
        private void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Release();
#endif
    }
}
