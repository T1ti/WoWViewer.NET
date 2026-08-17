using System.Runtime.InteropServices;
using System.Windows;

namespace WTEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);

        const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;

        public MainWindow()
        {
            AttachConsole(ATTACH_PARENT_PROCESS);

            InitializeComponent();
        }
    }
}