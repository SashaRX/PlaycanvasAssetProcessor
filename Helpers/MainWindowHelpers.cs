using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using HelixToolkit.Wpf;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AssetProcessor.Services;


namespace AssetProcessor.Helpers {
    internal static partial class MainWindowHelpers {
        [GeneratedRegex(@"[\[\]]")]
        public static partial Regex BracketsRegex();

#if DEBUG
        /// <summary>Dev-only: path to test model for quick debugging at startup</summary>
        public const string MODEL_PATH = @"C:\models\carbitLamp\ao.fbx";
#endif

        public static readonly string baseUrl = "https://playcanvas.com";

        public static string CleanProjectName(string input) {
            Regex bracketsRegex = BracketsRegex();
            string[] parts = input.Split(',');
            if (parts.Length > 1) {
                return bracketsRegex.Replace(parts[1], "").Trim();
            }
            return input;
        }

        public static GeometryModel3D CreateArrowModel(Point3D start, Point3D end, double thickness, double coneHeight, double coneRadius, System.Windows.Media.Color color) {
            MeshBuilder meshBuilder = new();
            Vector3D direction = new(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            direction.Normalize();
            Point3D cylinderEnd = end - direction * coneHeight;
            meshBuilder.AddCylinder(start, cylinderEnd, thickness, 36, false, true);
            meshBuilder.AddCone(cylinderEnd,
                                direction,
                                coneRadius,
                                0,
                                coneHeight,
                                true,
                                false,
                                36);
            MeshGeometry3D geometry = meshBuilder.ToMesh(true);
            EmissiveMaterial material = new(new SolidColorBrush(color));
            return new GeometryModel3D(geometry, material);
        }

        public static ModelVisual3D CreatePivotGizmo(Transform3DGroup transformGroup) {
            double axisLength = 10.0;
            double thickness = 0.1;
            double coneHeight = 1.0;
            double coneRadius = 0.3;

            // Создаем оси с конусами на концах
            GeometryModel3D xAxisModel = CreateArrowModel(new Point3D(0, 0, 0), new Point3D(axisLength, 0, 0), thickness, coneHeight, coneRadius, Colors.Red);
            GeometryModel3D yAxisModel = CreateArrowModel(new Point3D(0, 0, 0), new Point3D(0, axisLength, 0), thickness, coneHeight, coneRadius, Colors.Green);
            GeometryModel3D zAxisModel = CreateArrowModel(new Point3D(0, 0, 0), new Point3D(0, 0, axisLength), thickness, coneHeight, coneRadius, Colors.Blue);

            // Создаем группу для гизмо
            Model3DGroup group = new();
            group.Children.Add(xAxisModel);
            group.Children.Add(yAxisModel);
            group.Children.Add(zAxisModel);

            // Создаем ModelVisual3D для осей
            ModelVisual3D modelVisual = new() { Content = group, Transform = transformGroup };

            // Создаем подписи для осей
            BillboardTextVisual3D xLabel = CreateTextLabel("X", Colors.Red, new Point3D(axisLength + 0.5, 0, 0));
            BillboardTextVisual3D yLabel = CreateTextLabel("Y", Colors.Green, new Point3D(0, axisLength + 0.5, 0));
            BillboardTextVisual3D zLabel = CreateTextLabel("Z", Colors.Blue, new Point3D(0, 0, axisLength + 0.5));

            // Применяем те же трансформации к подписям
            xLabel.Transform = transformGroup;
            yLabel.Transform = transformGroup;
            zLabel.Transform = transformGroup;

            // Добавляем подписи к модели
            ModelVisual3D gizmoGroup = new();
            gizmoGroup.Children.Add(modelVisual);
            gizmoGroup.Children.Add(xLabel);
            gizmoGroup.Children.Add(yLabel);
            gizmoGroup.Children.Add(zLabel);

            return gizmoGroup;
        }

        public static BillboardTextVisual3D CreateTextLabel(string text, System.Windows.Media.Color color, Point3D position) {
            TextBlock textBlock = new() {
                Text = text,
                Foreground = new SolidColorBrush(color),
                Background = System.Windows.Media.Brushes.Transparent
            };

            VisualBrush visualBrush = new(textBlock);
            EmissiveMaterial material = new(visualBrush);

            return new BillboardTextVisual3D {
                Text = text,
                Position = position,
                Foreground = new SolidColorBrush(color),
                Background = System.Windows.Media.Brushes.Transparent,
                Material = material
            };
        }

        public static BitmapImage? CreateThumbnailImage(string? imagePath) {
            if (!string.IsNullOrEmpty(imagePath)) {
                if (File.Exists(imagePath)) {
                    BitmapImage bitmapImage = new();
                    bitmapImage.BeginInit();
                    bitmapImage.UriSource = new Uri(imagePath);
                    bitmapImage.DecodePixelWidth = 64; // Задаем ширину уменьшенного изображения
                    bitmapImage.DecodePixelHeight = 64; // Задаем высоту уменьшенного изображения
                    bitmapImage.EndInit();
                    return bitmapImage;
                }
            }
            return null; // Возвращаем пустое изображение, если файл не существует или путь пуст
        }

        public static string? GetFileExtension(string fileUrl) {
            return Path.GetExtension(fileUrl.Split('?')[0])?.ToLowerInvariant();
        }

        public static string? GetFileUrl(JToken file) {
            string? relativeUrl = file["url"]?.ToString();
            return !string.IsNullOrEmpty(relativeUrl) ? new Uri(new Uri(baseUrl), relativeUrl).ToString() : string.Empty;
        }

        public static (int width, int height)? GetLocalImageResolution(string imagePath, ILogService logService) {
            ArgumentNullException.ThrowIfNull(logService);
            try {
                using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
                return (image.Width, image.Height);
            } catch (Exception ex) {
                logService.LogError($"Error loading image for resolution: {ex.Message}");
                return null;
            }
        }

        public static async Task UpdateTextureResolutionAsync(
            TextureResource texture,
            ILogService logService,
            CancellationToken cancellationToken) {
            ArgumentNullException.ThrowIfNull(texture);
            ArgumentNullException.ThrowIfNull(logService);

            if (string.IsNullOrEmpty(texture.Url)) {
                texture.Resolution = new int[2];
                texture.Status = "Error";
                return;
            }

            try {
                string absoluteUrl = new Uri(new Uri(baseUrl), texture.Url).ToString(); // Ensure the URL is absolute
                (int Width, int Height) = await ImageHelper.GetImageResolutionAsync(absoluteUrl, cancellationToken);
                texture.Resolution = [Width, Height];
                logService.LogInfo($"Successfully retrieved resolution for {absoluteUrl}: {Width}x{Height}");
            } catch (Exception ex) {
                texture.Resolution = new int[2];
                texture.Status = "Error";
                logService.LogError($"Exception in UpdateTextureResolutionAsync for {texture.Url}: {ex.Message}");
            }
        }

        public static async Task VerifyAndProcessResourceAsync<TResource>(
            TResource resource,
            Func<Task> processResourceAsync,
            ILogService logService)
            where TResource : BaseResource {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(processResourceAsync);
            ArgumentNullException.ThrowIfNull(logService);

            try {
                if (resource != null) {
                    if (String.IsNullOrEmpty(resource.Path)) {
                        return;
                    }
                } else {
                    return;
                }

                if (!FileExistsWithLogging(resource.Path, logService)) {
                    logService.LogInfo($"{resource.Name} not found on disk: {resource.Path}");
                    resource.Status = "On Server";
                } else {
                    FileInfo fileInfo = new(resource.Path);
                    logService.LogInfo($"{resource.Name} found on disk: {resource.Path}");

                    if (fileInfo.Length == 0) {
                        resource.Status = "Empty File";
                    } else {
                        // Проверка на тип MaterialResource для пропуска проверок хэша и размера
                        if (resource is MaterialResource) {
                            resource.Status = "Downloaded";
                        } else {
                            long fileSizeInBytes = fileInfo.Length;
                            long resourceSizeInBytes = resource.Size;

                            if (resourceSizeInBytes <= 0) {
                                resource.Size = (int)fileSizeInBytes;
                                resource.Status = "Downloaded";
                            } else {
                                double tolerance = 0.05;
                                double lowerBound = resourceSizeInBytes * (1 - tolerance);
                                double upperBound = resourceSizeInBytes * (1 + tolerance);

                                if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                                    if (!string.IsNullOrEmpty(resource.Hash) && !FileHelper.IsFileIntact(resource.Path, resource.Hash, resource.Size)) {
                                        resource.Status = "Hash ERROR";
                                        logService.LogError($"{resource.Name} hash mismatch for file: {resource.Path}, expected hash: {resource.Hash}");
                                    } else {
                                        resource.Status = "Downloaded";
                                    }
                                } else {
                                    resource.Status = "Size Mismatch";
                                    logService.LogError($"{resource.Name} size mismatch: fileSizeInBytes: {fileSizeInBytes} and resourceSizeInBytes: {resourceSizeInBytes}");
                                }
                            }
                        }
                    }
                }
                await processResourceAsync();
            } catch (Exception ex) {
                logService.LogError($"Error processing resource: {ex.Message}");
            }
        }

        private static bool FileExistsWithLogging(string filePath, ILogService logService) {
            ArgumentNullException.ThrowIfNull(logService);

            try {
                logService.LogInfo($"Checking if file exists: {filePath}");
                bool exists = File.Exists(filePath);
                logService.LogInfo($"File exists: {exists}");
                return exists;
            } catch (Exception ex) {
                logService.LogError($"Exception while checking file existence: {filePath}, Exception: {ex.Message}");
                return false;
            }
        }

    }
}
