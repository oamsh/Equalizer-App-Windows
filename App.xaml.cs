using System.Configuration;
using System.Data;
using System.Windows;

// --- AMBIGUITY FIX ---
// Explicitly tell the compiler to use the WPF Application class
using Application = System.Windows.Application;
// ---------------------

namespace EqualizerPro
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
    }
}