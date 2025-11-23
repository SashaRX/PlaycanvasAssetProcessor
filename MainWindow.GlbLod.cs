using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Viewer;
using AssetProcessor.Resources;
using NLog;

namespace AssetProcessor {
    /// <summary>
    /// MainWindow partial class для работы с GLB LOD просмотром
    /// </summary>
    public partial class MainWindow {
        private static readonly Logger LodLogger = LogManager.GetCurrentClassLogger();
        private GlbViewerControl? _glbViewer;
        private Dictionary<LodLevel, GlbLodHelper.LodInfo> _currentLodInfos = new();
        private ObservableCollection<LodDisplayInfo> _lodDisplayItems = new();
        private bool _isGlbViewerActive = false;
        private Border? _viewportBorderContainer;

        /// <summary>
        /// Инициализация GLB LOD компонентов (вызывается из конструктора MainWindow)
        /// </summary>
        private void InitializeGlbLodComponents() {
            // Инициализация выполнена - коллекция создана в поле
            LodLogger.Info("GLB LOD components initialized");
        }

        /// <summary>
        /// Очищает GLB viewer ресурсы при закрытии окна
        /// </summary>
        private void CleanupGlbViewer() {
            try {
                _glbViewer?.Clear();
                _currentLodInfos.Clear();
                _lodDisplayItems.Clear();
                _isGlbViewerActive = false;
                LodLogger.Info("GLB viewer cleaned up");
            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to cleanup GLB viewer");
            }
        }

        /// <summary>
        /// Класс для отображения LOD информации в DataGrid
        /// </summary>
        public class LodDisplayInfo : INotifyPropertyChanged {
            private bool _isSelected;

            public LodLevel Level { get; set; }
            public string LodName => $"LOD{(int)Level}";
            public int TriangleCount { get; set; }
            public int VertexCount { get; set; }
            public string FileSizeFormatted { get; set; } = string.Empty;

            public bool IsSelected {
                get => _isSelected;
                set {
                    if (_isSelected != value) {
                        _isSelected = value;
                        OnPropertyChanged(nameof(IsSelected));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged(string propertyName) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// Проверяет наличие GLB LOD файлов для выбранной модели и загружает их
        /// </summary>
        private async Task TryLoadGlbLodAsync(string fbxPath) {
            try {
                LodLogger.Info($"Checking for GLB LOD files: {fbxPath}");

                // Ищем GLB LOD файлы
                _currentLodInfos = GlbLodHelper.FindGlbLodFiles(fbxPath);

                if (_currentLodInfos.Count == 0) {
                    LodLogger.Info("No GLB LOD files found, using FBX viewer");
                    HideGlbLodUI();
                    return;
                }

                LodLogger.Info($"Found {_currentLodInfos.Count} GLB LOD files");

                // Показываем LOD UI
                ShowGlbLodUI();

                // Заполняем DataGrid
                PopulateLodDataGrid();

                // Создаём GlbViewerControl если его еще нет
                if (_glbViewer == null) {
                    LodLogger.Info("Creating GlbViewerControl");
                    _glbViewer = new GlbViewerControl();
                }

                // Загружаем LOD цепочку
                var lodFilePaths = GlbLodHelper.GetLodFilePaths(fbxPath);
                await _glbViewer.LoadLodChainAsync(lodFilePaths);

                // Переключаемся на GLB viewer
                SwitchToGlbViewer();

                // Выбираем LOD0 по умолчанию
                SelectLod(LodLevel.LOD0);

                LodLogger.Info("GLB LOD preview loaded successfully");

            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to load GLB LOD files");
                HideGlbLodUI();
            }
        }

        /// <summary>
        /// Показывает UI элементы для работы с LOD
        /// </summary>
        private void ShowGlbLodUI() {
            Dispatcher.Invoke(() => {
                LodQuickSwitchPanel.Visibility = Visibility.Visible;
                LodInformationPanel.Visibility = Visibility.Visible;
                ModelCurrentLodTextBlock.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// Скрывает UI элементы для работы с LOD
        /// </summary>
        private void HideGlbLodUI() {
            Dispatcher.Invoke(() => {
                LodQuickSwitchPanel.Visibility = Visibility.Collapsed;
                LodInformationPanel.Visibility = Visibility.Collapsed;
                ModelCurrentLodTextBlock.Visibility = Visibility.Collapsed;

                // Очищаем данные
                _currentLodInfos.Clear();
                _lodDisplayItems.Clear();

                // Переключаемся обратно на FBX viewer если был активен GLB
                if (_isGlbViewerActive) {
                    SwitchToFbxViewer();
                }
            });
        }

        /// <summary>
        /// Заполняет DataGrid информацией о LOD уровнях
        /// </summary>
        private void PopulateLodDataGrid() {
            Dispatcher.Invoke(() => {
                _lodDisplayItems.Clear();

                foreach (var kvp in _currentLodInfos.OrderBy(x => x.Key)) {
                    var lodInfo = kvp.Value;
                    _lodDisplayItems.Add(new LodDisplayInfo {
                        Level = lodInfo.Level,
                        TriangleCount = lodInfo.TriangleCount,
                        VertexCount = lodInfo.VertexCount,
                        FileSizeFormatted = lodInfo.FileSizeFormatted
                    });
                }

                LodInformationGrid.ItemsSource = _lodDisplayItems;
            });
        }

        /// <summary>
        /// Переключается на GLB viewer
        /// </summary>
        private void SwitchToGlbViewer() {
            if (_glbViewer == null) return;

            Dispatcher.Invoke(() => {
                try {
                    // Сохраняем ссылку на Border контейнер при первом переключении
                    if (_viewportBorderContainer == null) {
                        _viewportBorderContainer = viewPort3d.Parent as Border;
                    }

                    if (_viewportBorderContainer != null) {
                        // Удаляем FBX viewer из контейнера
                        _viewportBorderContainer.Child = null;

                        // Добавляем GLB viewer
                        _viewportBorderContainer.Child = _glbViewer;
                    }

                    _isGlbViewerActive = true;
                    LodLogger.Info("Switched to GLB viewer");
                } catch (Exception ex) {
                    LodLogger.Error(ex, "Failed to switch to GLB viewer");
                }
            });
        }

        /// <summary>
        /// Переключается обратно на FBX viewer
        /// </summary>
        private void SwitchToFbxViewer() {
            Dispatcher.Invoke(() => {
                try {
                    if (_viewportBorderContainer != null) {
                        // Удаляем GLB viewer из контейнера
                        _viewportBorderContainer.Child = null;

                        // Очищаем GLB viewer перед переключением
                        _glbViewer?.Clear();

                        // Возвращаем FBX viewer
                        _viewportBorderContainer.Child = viewPort3d;

                        // Сбрасываем viewport (очищаем старые модели)
                        ResetViewport();
                    }

                    _isGlbViewerActive = false;
                    LodLogger.Info("Switched to FBX viewer");
                } catch (Exception ex) {
                    LodLogger.Error(ex, "Failed to switch to FBX viewer");
                }
            });
        }

        /// <summary>
        /// Выбирает конкретный LOD уровень для просмотра
        /// </summary>
        private void SelectLod(LodLevel lodLevel) {
            if (_glbViewer == null) return;

            try {
                LodLogger.Info($"Selecting LOD: {lodLevel}");

                // Переключаем LOD в viewer
                _glbViewer.SwitchLod(lodLevel);

                // Обновляем UI
                Dispatcher.Invoke(() => {
                    // Обновляем текст Current LOD
                    ModelCurrentLodTextBlock.Text = $"Current LOD: {lodLevel} (GLB)";

                    // Обновляем информацию о модели
                    if (_currentLodInfos.TryGetValue(lodLevel, out var lodInfo)) {
                        ModelTrianglesTextBlock.Text = $"Triangles: {lodInfo.TriangleCount:N0}";
                        ModelVerticesTextBlock.Text = $"Vertices: {lodInfo.VertexCount:N0}";
                    }

                    // Обновляем кнопки (подсвечиваем активную)
                    UpdateLodButtonStates(lodLevel);

                    // Обновляем выделение в DataGrid
                    var selectedItem = _lodDisplayItems.FirstOrDefault(x => x.Level == lodLevel);
                    if (selectedItem != null) {
                        LodInformationGrid.SelectedItem = selectedItem;
                    }
                });

                LodLogger.Info($"LOD {lodLevel} selected successfully");

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to select LOD: {lodLevel}");
            }
        }

        /// <summary>
        /// Обновляет состояние кнопок LOD (подсвечивает активную)
        /// </summary>
        private void UpdateLodButtonStates(LodLevel currentLod) {
            // Обновляем стили кнопок
            LodButton0.FontWeight = currentLod == LodLevel.LOD0 ? FontWeights.Bold : FontWeights.Normal;
            LodButton1.FontWeight = currentLod == LodLevel.LOD1 ? FontWeights.Bold : FontWeights.Normal;
            LodButton2.FontWeight = currentLod == LodLevel.LOD2 ? FontWeights.Bold : FontWeights.Normal;
            LodButton3.FontWeight = currentLod == LodLevel.LOD3 ? FontWeights.Bold : FontWeights.Normal;

            // Включаем/выключаем кнопки в зависимости от доступности LOD
            LodButton0.IsEnabled = _currentLodInfos.ContainsKey(LodLevel.LOD0);
            LodButton1.IsEnabled = _currentLodInfos.ContainsKey(LodLevel.LOD1);
            LodButton2.IsEnabled = _currentLodInfos.ContainsKey(LodLevel.LOD2);
            LodButton3.IsEnabled = _currentLodInfos.ContainsKey(LodLevel.LOD3);
        }

        /// <summary>
        /// Обработчик клика по кнопкам LOD
        /// </summary>
        private void LodButton_Click(object sender, RoutedEventArgs e) {
            if (sender is Button button && button.Tag is string tagStr) {
                if (int.TryParse(tagStr, out int lodIndex)) {
                    var lodLevel = (LodLevel)lodIndex;
                    SelectLod(lodLevel);
                }
            }
        }

        /// <summary>
        /// Обработчик выбора строки в LOD Information DataGrid
        /// </summary>
        private void LodInformationGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (LodInformationGrid.SelectedItem is LodDisplayInfo selectedLod) {
                SelectLod(selectedLod.Level);
            }
        }
    }
}
