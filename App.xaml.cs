using System.Configuration;
using System.Data;
using System.Windows;

namespace TexTool{
    public partial class App : Application{
        public static string ProjectId { get; set; } = "1054788";
        public static string BranchId { get; set; } = "55d4b774-8ecf-4a72-9798-9ca0e83304f0";
        public static string PlaycanvasApiKey { get; set; } = "o5lPWdvxh6lCMtw6jlvlF8jqnhq1RjGd";
        public static string BaseUrl { get; set; } = "https://playcanvas.com";
        public static int SemaphoreLimit { get; set; } = 5;
    }
}
