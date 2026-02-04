using System.Reflection;
using System.Windows;

namespace AssetProcessor.Windows {
    public partial class AboutWindow : Window {
        public AboutWindow() {
            InitializeComponent();
            LoadVersionInfo();
        }

        private void LoadVersionInfo() {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";

            string branch = GetGitBranch();
            string commit = GetGitCommit();

            if (branch != "unknown" && commit != "unknown") {
                BuildInfoText.Text = $"{branch} ({commit})";
            } else {
                BuildInfoText.Visibility = Visibility.Collapsed;
            }
        }

        private static string GetGitBranch() {
            try {
                string? currentDir = System.AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(currentDir)) {
                    string gitDir = System.IO.Path.Combine(currentDir, ".git");
                    if (System.IO.Directory.Exists(gitDir)) {
                        string headPath = System.IO.Path.Combine(gitDir, "HEAD");
                        if (System.IO.File.Exists(headPath)) {
                            string headContent = System.IO.File.ReadAllText(headPath).Trim();
                            if (headContent.StartsWith("ref: refs/heads/")) {
                                return headContent.Substring("ref: refs/heads/".Length);
                            }
                        }
                    }
                    var parent = System.IO.Directory.GetParent(currentDir);
                    if (parent == null) break;
                    currentDir = parent.FullName;
                }
            } catch { }
            return "unknown";
        }

        private static string GetGitCommit() {
            try {
                string? currentDir = System.AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(currentDir)) {
                    string gitDir = System.IO.Path.Combine(currentDir, ".git");
                    if (System.IO.Directory.Exists(gitDir)) {
                        string headPath = System.IO.Path.Combine(gitDir, "HEAD");
                        if (System.IO.File.Exists(headPath)) {
                            string headContent = System.IO.File.ReadAllText(headPath).Trim();
                            if (headContent.StartsWith("ref: ")) {
                                string refPath = headContent.Substring(5);
                                string refFilePath = System.IO.Path.Combine(gitDir, refPath);
                                if (System.IO.File.Exists(refFilePath)) {
                                    string hash = System.IO.File.ReadAllText(refFilePath).Trim();
                                    return hash.Length > 7 ? hash.Substring(0, 7) : hash;
                                }
                            } else if (headContent.Length >= 7) {
                                return headContent.Substring(0, 7);
                            }
                        }
                    }
                    var parent = System.IO.Directory.GetParent(currentDir);
                    if (parent == null) break;
                    currentDir = parent.FullName;
                }
            } catch { }
            return "unknown";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            Close();
        }
    }
}
