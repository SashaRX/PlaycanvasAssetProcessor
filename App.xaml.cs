using System.Configuration;
using System.Data;
using System.Windows;

namespace TexTool{
    public partial class App : Application{
        public static string ProjectId { get; set; } = "";
        public static string BranchId { get; set; } = "";
        public static string PlaycanvasApiKey { get; set; } = "";
        public static string BaseUrl { get; set; } = "https://playcanvas.com";
        public static int SemaphoreLimit { get; set; } = 32;
    }
}
