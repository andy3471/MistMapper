using Microsoft.Gaming.XboxGameBar;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MistMapper.GameBarWidget
{
    sealed partial class App : Application
    {
        XboxGameBarWidget _widget;

        public App()
        {
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            InitializeComponent();
            Suspending += OnSuspending;
            UnhandledException += (_, e) =>
            {
                // Keep Game Bar from showing a blank "isn't playing nice" without a chance to recover.
                e.Handled = true;
            };
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            XboxGameBarWidgetActivatedEventArgs widgetArgs = null;
            if (args.Kind == ActivationKind.Protocol)
            {
                var protocolArgs = args as IProtocolActivatedEventArgs;
                string scheme = protocolArgs?.Uri?.Scheme;
                if (string.Equals(scheme, "ms-gamebarwidget", StringComparison.OrdinalIgnoreCase))
                    widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
            }

            if (widgetArgs == null)
                return;

            if (!widgetArgs.IsLaunchActivation)
                return;

            var rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            Window.Current.Content = rootFrame;

            // Construct the Game Bar widget bridge BEFORE navigating, matching Microsoft samples.
            _widget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
            rootFrame.Navigate(typeof(WidgetPage), _widget);
            Window.Current.Closed += WidgetWindow_Closed;
            Window.Current.Activate();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            if (!e.PrelaunchActivated)
            {
                if (rootFrame.Content == null)
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                Window.Current.Activate();
            }
        }

        void WidgetWindow_Closed(object sender, Windows.UI.Core.CoreWindowEventArgs e)
        {
            _widget = null;
            Window.Current.Closed -= WidgetWindow_Closed;
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            _widget = null;
            deferral.Complete();
        }
    }
}
