using System.Reflection;
using System.Windows.Controls;
using System.Windows.Navigation;
using Wpf.Ui.Appearance;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class InfoPage : Page
    {
        public const int MIN_HEIGHT = 210;

        public InfoPage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            Version.Text = version;

            Loaded += (s, e) =>
            {
                (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(minHeight: MIN_HEIGHT);
            };
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            ShellUtil.Open(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}
