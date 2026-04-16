using System;
using System.Collections.Generic;
using System.Text;

namespace WTEditor.Avalonia.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        public Editor3DViewModel ViewportVM { get; } = new();

        public MainViewModel()
        {

        }
    }
}
