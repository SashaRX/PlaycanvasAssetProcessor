using System.Collections.ObjectModel;
using System.Linq;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// Узел дерева папок для отображения иерархии на сервере
    /// </summary>
    public class ServerFolderNode {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public ObservableCollection<ServerFolderNode> Children { get; } = new();
        public ObservableCollection<ServerAssetViewModel> Files { get; } = new();
        public bool IsExpanded { get; set; } = true;

        /// <summary>
        /// Общее количество файлов (включая вложенные папки)
        /// </summary>
        public int TotalFileCount => Files.Count + Children.Sum(c => c.TotalFileCount);

        /// <summary>
        /// Отображаемое имя с количеством файлов
        /// </summary>
        public string DisplayName => $"{Name} ({TotalFileCount})";

        /// <summary>
        /// Строит дерево из плоского списка файлов
        /// </summary>
        public static ServerFolderNode BuildTree(IEnumerable<ServerAssetViewModel> assets, string rootName = "Server") {
            var root = new ServerFolderNode { Name = rootName, FullPath = "" };

            foreach (var asset in assets) {
                var pathParts = asset.RemotePath.Split('/');
                var currentNode = root;

                // Проходим по всем частям пути кроме имени файла
                for (int i = 0; i < pathParts.Length - 1; i++) {
                    var folderName = pathParts[i];
                    var existingChild = currentNode.Children.FirstOrDefault(c => c.Name == folderName);

                    if (existingChild == null) {
                        var newPath = string.Join("/", pathParts.Take(i + 1));
                        existingChild = new ServerFolderNode {
                            Name = folderName,
                            FullPath = newPath
                        };
                        currentNode.Children.Add(existingChild);
                    }

                    currentNode = existingChild;
                }

                // Добавляем файл в текущую папку
                currentNode.Files.Add(asset);
            }

            // Сортируем папки и файлы
            SortTree(root);

            return root;
        }

        private static void SortTree(ServerFolderNode node) {
            var sortedChildren = node.Children.OrderBy(c => c.Name).ToList();
            node.Children.Clear();
            foreach (var child in sortedChildren) {
                node.Children.Add(child);
                SortTree(child);
            }

            var sortedFiles = node.Files.OrderBy(f => f.FileName).ToList();
            node.Files.Clear();
            foreach (var file in sortedFiles) {
                node.Files.Add(file);
            }
        }
    }
}
