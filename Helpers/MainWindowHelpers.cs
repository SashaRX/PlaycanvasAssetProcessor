using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using HelixToolkit.Wpf;
using Newtonsoft.Json.Linq;
using OxyPlot;
using OxyPlot.Series;
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


namespace AssetProcessor.Helpers {
    internal static partial class MainWindowHelpers {
        [GeneratedRegex(@"[\[\]]")]
        public static partial Regex BracketsRegex();

        public const string MODEL_PATH = @"C:\models\carbitLamp\ao.fbx";

        public static readonly string baseUrl = "https://playcanvas.com";

        public static readonly object logLock = new();

        public static string CleanProjectName(string input) {
            Regex bracketsRegex = BracketsRegex();
            string[] parts = input.Split(',');
            if (parts.Length > 1) {
                return bracketsRegex.Replace(parts[1], "").Trim();
            }
            return input;
        }

        public static void AddSeriesToModel(PlotModel model, int[] histogram, OxyColor color) {
            OxyColor colorWithAlpha = OxyColor.FromAColor(100, color);
            AreaSeries series = new(){Color = color, Fill = colorWithAlpha, StrokeThickness = 1 };

            double[] smoothedHistogram = MovingAverage(histogram, 32);

            for (int i = 0; i < 256; i++) {
                series.Points.Add(new DataPoint(i, smoothedHistogram[i]));
                series.Points2.Add(new DataPoint(i, 0));
            }

            model.Series.Add(series);
        }

        public static async Task<BitmapSource> ApplyChannelFilterAsync(BitmapSource source, string channel) {
            using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(source));

            await Task.Run(() => {
                switch (channel) {
                    case "R":
                        ProcessChannel(image, (pixel) => new Rgba32(pixel.R, pixel.R, pixel.R, pixel.A));
                        break;
                    case "G":
                        ProcessChannel(image, (pixel) => new Rgba32(pixel.G, pixel.G, pixel.G, pixel.A));
                        break;
                    case "B":
                        ProcessChannel(image, (pixel) => new Rgba32(pixel.B, pixel.B, pixel.B, pixel.A));
                        break;
                    case "A":
                        ProcessChannel(image, (pixel) => new Rgba32(pixel.A, pixel.A, pixel.A, pixel.A));
                        break;
                }
            });

            return BitmapToBitmapSource(image);
        }

        public static byte[] BitmapSourceToArray(BitmapSource bitmapSource) {
            PngBitmapEncoder encoder = new(); // Или любой другой доступный энкодер
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)bitmapSource.Clone()));
            using MemoryStream stream = new();
            encoder.Save(stream);
            return stream.ToArray();
        }

        public static BitmapImage BitmapToBitmapSource(Image<Rgba32> image) {
            using MemoryStream memoryStream = new();
            image.SaveAsBmp(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            BitmapImage bitmapImage = new();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memoryStream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            return bitmapImage;
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

        public static async Task<bool> DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken) {
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                using FileStream fs = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs, cancellationToken);
                return true;
            } catch (Exception ex) {
                LogError($"Error downloading file from {url}: {ex.Message}");
                return false;
            }
        }

        public static bool FileExistsWithLogging(string filePath) {
            try {
                LogInfo($"Checking if file exists: {filePath}");
                bool exists = File.Exists(filePath);
                LogInfo($"File exists: {exists}");
                return exists;
            } catch (Exception ex) {
                LogError($"Exception while checking file existence: {filePath}, Exception: {ex.Message}");
                return false;
            }
        }

        public static string? GetFileExtension(string fileUrl) {
            return Path.GetExtension(fileUrl.Split('?')[0])?.ToLowerInvariant();
        }

        public static string? GetFileUrl(JToken file) {
            string? relativeUrl = file["url"]?.ToString();
            return !string.IsNullOrEmpty(relativeUrl) ? new Uri(new Uri(baseUrl), relativeUrl).ToString() : string.Empty;
        }

        public static void LogError(string? message) {
            string logFilePath = "error_log.txt";
            lock (logLock) {
                File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
                // Вывод сообщения в консоль IDE
                System.Diagnostics.Debug.WriteLine($"{DateTime.Now}: {message}");
            }
        }

        public static void LogInfo(string message) {
            string logFilePath = "info_log.txt";
            lock (logLock) {
                File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
            }
        }

        public static double[] MovingAverage(int[] values, int windowSize) {
            double[] result = new double[values.Length];
            double sum = 0;
            for (int i = 0; i < values.Length; i++) {
                sum += values[i];
                if (i >= windowSize) {
                    sum -= values[i - windowSize];
                }
                result[i] = sum / Math.Min(windowSize, i + 1);
            }
            return result;
        }

        public static void ProcessChannel(Image<Rgba32> image, Func<Rgba32, Rgba32> transform) {
            int width = image.Width;
            int height = image.Height;
            int numberOfChunks = Environment.ProcessorCount; // Количество потоков для параллельной обработки
            int chunkHeight = height / numberOfChunks;

            Parallel.For(0, numberOfChunks, chunk => {
                int startY = chunk * chunkHeight;
                int endY = (chunk == numberOfChunks - 1) ? height : startY + chunkHeight;

                for (int y = startY; y < endY; y++) {
                    Span<Rgba32> pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                    for (int x = 0; x < width; x++) {
                        pixelRow[x] = transform(pixelRow[x]);
                    }
                }
            });
        }

        public static void ProcessImage(BitmapSource bitmapSource, int[] redHistogram, int[] greenHistogram, int[] blueHistogram) {
            using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(bitmapSource));
            Parallel.For(0, image.Height, y => {
                Span<Rgba32> pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < pixelRow.Length; x++) {
                    Rgba32 pixel = pixelRow[x];
                    redHistogram[pixel.R]++;
                    greenHistogram[pixel.G]++;
                    blueHistogram[pixel.B]++;
                }
            });
        }

        public static (int width, int height)? GetLocalImageResolution(string imagePath) {
            try {
                using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
                return (image.Width, image.Height);
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error loading image for resolution: {ex.Message}");
                return null;
            }
        }

        public static async Task UpdateTextureResolutionAsync(TextureResource texture, CancellationToken cancellationToken) {
            ArgumentNullException.ThrowIfNull(texture);

            if (string.IsNullOrEmpty(texture.Url)) {
                texture.Resolution = new int[2];
                texture.Status = "Error";
                return;
            }

            try {
                string absoluteUrl = new Uri(new Uri(baseUrl), texture.Url).ToString(); // Ensure the URL is absolute
                (int Width, int Height) = await ImageHelper.GetImageResolutionAsync(absoluteUrl, cancellationToken);
                texture.Resolution = [Width, Height];
                LogError($"Successfully retrieved resolution for {absoluteUrl}: {Width}x{Height}");
            } catch (Exception ex) {
                texture.Resolution = new int[2];
                texture.Status = "Error";
                LogError($"Exception in UpdateTextureResolutionAsync for {texture.Url}: {ex.Message}");
            }
        }

        public static async Task VerifyAndProcessResourceAsync<TResource>(
            TResource resource,
            Func<Task> processResourceAsync)
            where TResource : BaseResource {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(processResourceAsync);

            try {
                if (resource != null) {
                    if (String.IsNullOrEmpty(resource.Path)) {
                        return;
                    }
                } else {
                    return;
                }

                if (!FileExistsWithLogging(resource.Path)) {
                    LogInfo($"{resource.Name} not found on disk: {resource.Path}");
                    resource.Status = "On Server";
                } else {
                    FileInfo fileInfo = new(resource.Path);
                    LogInfo($"{resource.Name} found on disk: {resource.Path}");

                    if (fileInfo.Length == 0) {
                        resource.Status = "Empty File";
                    } else {
                        // Проверка на тип MaterialResource для пропуска проверок хэша и размера
                        if (resource is MaterialResource) {
                            resource.Status = "Downloaded";
                        } else {
                            long fileSizeInBytes = fileInfo.Length;
                            long resourceSizeInBytes = resource.Size;
                            double tolerance = 0.05;
                            double lowerBound = resourceSizeInBytes * (1 - tolerance);
                            double upperBound = resourceSizeInBytes * (1 + tolerance);

                            if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                                if (!string.IsNullOrEmpty(resource.Hash) && !FileHelper.IsFileIntact(resource.Path, resource.Hash, resource.Size)) {
                                    resource.Status = "Hash ERROR";
                                    LogError($"{resource.Name} hash mismatch for file: {resource.Path}, expected hash: {resource.Hash}");
                                } else {
                                    resource.Status = "Downloaded";
                                }
                            } else {
                                resource.Status = "Size Mismatch";
                                LogError($"{resource.Name} size mismatch: fileSizeInBytes: {fileSizeInBytes} and resourceSizeInBytes: {resourceSizeInBytes}");
                            }
                        }
                    }
                }
                await processResourceAsync();
            } catch (Exception ex) {
                LogError($"Error processing resource: {ex.Message}");
            }
        }

    }
}