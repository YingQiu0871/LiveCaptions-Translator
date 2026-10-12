using System.Reflection;
using Avalonia.Controls;

namespace LiveCaptionsTranslator
{
    public partial class InfoPage : UserControl
    {
        public InfoPage()
        {
            InitializeComponent();
            Version.Content = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        }
    }
}
