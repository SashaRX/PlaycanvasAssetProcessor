using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace AssetProcessor.Helpers {
    public static class VersionHelper {
        public static string GetVersionString() {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            string versionStr = $"{version?.Major}.{version?.Minor}.{version?.Build}";

            string branch = GetGitBranch();
            string commit = GetGitCommit();

            if (!string.IsNullOrEmpty(branch) && branch != "unknown") {
                versionStr += $" | {branch}";
            }

            if (!string.IsNullOrEmpty(commit) && commit != "unknown") {
                versionStr += $" | {commit}";
            }

            return versionStr;
        }

        private static string GetGitBranch() {
            try {
                string gitPath = FindGitDirectory();
                if (string.IsNullOrEmpty(gitPath)) return "unknown";

                string headPath = Path.Combine(gitPath, "HEAD");
                if (!File.Exists(headPath)) return "unknown";

                string headContent = File.ReadAllText(headPath).Trim();

                // Если HEAD указывает на ветку
                if (headContent.StartsWith("ref: refs/heads/")) {
                    return headContent.Substring("ref: refs/heads/".Length);
                }

                // Если HEAD detached (указывает на коммит)
                if (headContent.Length == 40 || headContent.Length == 7) {
                    return "detached HEAD";
                }

                return "unknown";
            } catch {
                return "unknown";
            }
        }

        private static string GetGitCommit() {
            try {
                string gitPath = FindGitDirectory();
                if (string.IsNullOrEmpty(gitPath)) return "unknown";

                string headPath = Path.Combine(gitPath, "HEAD");
                if (!File.Exists(headPath)) return "unknown";

                string headContent = File.ReadAllText(headPath).Trim();

                // Если HEAD указывает на ветку
                if (headContent.StartsWith("ref: ")) {
                    string refPath = headContent.Substring(5); // Убираем "ref: "
                    string refFilePath = Path.Combine(gitPath, refPath);

                    if (File.Exists(refFilePath)) {
                        string commitHash = File.ReadAllText(refFilePath).Trim();
                        return commitHash.Length > 7 ? commitHash.Substring(0, 7) : commitHash;
                    }
                }

                // Если HEAD detached
                if (headContent.Length >= 7) {
                    return headContent.Substring(0, 7);
                }

                return "unknown";
            } catch {
                return "unknown";
            }
        }

        private static string FindGitDirectory() {
            try {
                // Получаем директорию приложения
                string? currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                // Поднимаемся вверх по директориям в поисках .git
                while (!string.IsNullOrEmpty(currentDir)) {
                    string gitDir = Path.Combine(currentDir, ".git");
                    if (Directory.Exists(gitDir)) {
                        return gitDir;
                    }

                    DirectoryInfo? parent = Directory.GetParent(currentDir);
                    if (parent == null) break;
                    currentDir = parent.FullName;
                }

                return string.Empty;
            } catch {
                return string.Empty;
            }
        }
    }
}
