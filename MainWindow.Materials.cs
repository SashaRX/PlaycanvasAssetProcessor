using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using NLog;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xceed.Wpf.Toolkit;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing material-related UI logic:
    /// - Material display and parameter updates
    /// - Texture navigation from materials
    /// - Master material ComboBox handling
    /// - Color picker handlers for tints
    /// </summary>
    public partial class MainWindow {

        #region Materials

        /// <summary>
        /// Wires event handlers for MaterialInfoPanel controls.
        /// Called from MainWindow constructor after InitializeComponent.
        /// </summary>
        private void InitializeMaterialInfoPanel() {
            // ComboBox
            materialInfoPanel.MaterialMasterComboBox.SelectionChanged += MaterialMasterComboBox_SelectionChanged;

            // Hyperlink Click (all share same handler)
            materialInfoPanel.MaterialDiffuseMapHyperlink.Click += MaterialMapHyperlink_Click;
            materialInfoPanel.MaterialNormalMapHyperlink.Click += MaterialMapHyperlink_Click;
            materialInfoPanel.MaterialAOMapHyperlink.Click += MaterialMapHyperlink_Click;
            materialInfoPanel.MaterialGlossMapHyperlink.Click += MaterialMapHyperlink_Click;
            materialInfoPanel.MaterialMetalnessMapHyperlink.Click += MaterialMapHyperlink_Click;
            materialInfoPanel.MaterialEmissiveMapHyperlink.Click += MaterialMapHyperlink_Click;
            materialInfoPanel.MaterialOpacityMapHyperlink.Click += MaterialMapHyperlink_Click;

            // Image MouseLeftButtonUp (all share same handler)
            materialInfoPanel.TextureDiffusePreviewImage.MouseLeftButtonUp += TexturePreview_MouseLeftButtonUp;
            materialInfoPanel.TextureNormalPreviewImage.MouseLeftButtonUp += TexturePreview_MouseLeftButtonUp;
            materialInfoPanel.TextureAOPreviewImage.MouseLeftButtonUp += TexturePreview_MouseLeftButtonUp;
            materialInfoPanel.TextureGlossPreviewImage.MouseLeftButtonUp += TexturePreview_MouseLeftButtonUp;
            materialInfoPanel.TextureMetalnessPreviewImage.MouseLeftButtonUp += TexturePreview_MouseLeftButtonUp;
            materialInfoPanel.TextureEmissivePreviewImage.MouseLeftButtonUp += TexturePreview_MouseLeftButtonUp;
            materialInfoPanel.TextureOpacityPreviewImage.MouseLeftButtonUp += TexturePreview_MouseLeftButtonUp;

            // Navigate buttons (unnamed) - catch bubbling Button.Click from the UserControl
            materialInfoPanel.AddHandler(Button.ClickEvent, new RoutedEventHandler(MaterialInfoPanel_ButtonClick));

            // ColorPicker SelectedColorChanged
            materialInfoPanel.TintColorPicker.SelectedColorChanged += TintColorPicker_SelectedColorChanged;
            materialInfoPanel.AOTintColorPicker.SelectedColorChanged += AOTintColorPicker_SelectedColorChanged;
            materialInfoPanel.TintSpecularColorPicker.SelectedColorChanged += TintSpecularColorPicker_SelectedColorChanged;
        }

        /// <summary>
        /// Handles bubbling Button.Click from MaterialInfoPanel navigate buttons.
        /// </summary>
        private void MaterialInfoPanel_ButtonClick(object sender, RoutedEventArgs e) {
            if (e.OriginalSource is Button btn && btn.Tag is string textureType) {
                NavigateToMaterialTexture(textureType);
            }
        }

        private void DisplayMaterialParameters(MaterialResource parameters) {
            Dispatcher.Invoke(() => {
                // Header
                materialInfoPanel.MaterialIDTextBlock.Text = $"ID: {parameters.ID}";
                materialInfoPanel.MaterialNameTextBlock.Text = parameters.Name ?? "Unnamed";

                // NOTE: Do NOT update MaterialMasterComboBox here!
                // The MasterMaterialName is stored in config.json, not in material JSON files.
                // MaterialsDataGrid_SelectionChanged already handles ComboBox update using
                // the material from DataGrid which has the correct MasterMaterialName.

                // Texture hyperlinks and previews
                UpdateTextureHyperlink(materialInfoPanel.MaterialDiffuseMapHyperlink, parameters.DiffuseMapId, parameters);
                UpdateTextureHyperlink(materialInfoPanel.MaterialNormalMapHyperlink, parameters.NormalMapId, parameters);
                UpdateTextureHyperlink(materialInfoPanel.MaterialAOMapHyperlink, parameters.AOMapId, parameters);
                UpdateTextureHyperlink(materialInfoPanel.MaterialGlossMapHyperlink, parameters.GlossMapId, parameters);
                UpdateTextureHyperlink(materialInfoPanel.MaterialMetalnessMapHyperlink, parameters.MetalnessMapId, parameters);
                UpdateTextureHyperlink(materialInfoPanel.MaterialEmissiveMapHyperlink, parameters.EmissiveMapId, parameters);
                UpdateTextureHyperlink(materialInfoPanel.MaterialOpacityMapHyperlink, parameters.OpacityMapId, parameters);

                // Texture previews
                SetTextureImage(materialInfoPanel.TextureDiffusePreviewImage, parameters.DiffuseMapId);
                SetTextureImage(materialInfoPanel.TextureNormalPreviewImage, parameters.NormalMapId);
                SetTextureImage(materialInfoPanel.TextureAOPreviewImage, parameters.AOMapId);
                SetTextureImage(materialInfoPanel.TextureGlossPreviewImage, parameters.GlossMapId);
                SetTextureImage(materialInfoPanel.TextureMetalnessPreviewImage, parameters.MetalnessMapId);
                SetTextureImage(materialInfoPanel.TextureEmissivePreviewImage, parameters.EmissiveMapId);
                SetTextureImage(materialInfoPanel.TextureOpacityPreviewImage, parameters.OpacityMapId);

                // Overrides sliders
                materialInfoPanel.MaterialBumpinessTextBox.Text = (parameters.BumpMapFactor ?? 1.0f).ToString("F2");
                materialInfoPanel.MaterialBumpinessIntensitySlider.Value = parameters.BumpMapFactor ?? 1.0;

                materialInfoPanel.MaterialMetalnessTextBox.Text = (parameters.Metalness ?? 0.0f).ToString("F2");
                materialInfoPanel.MaterialMetalnessIntensitySlider.Value = parameters.Metalness ?? 0.0;

                materialInfoPanel.MaterialGlossinessTextBox.Text = (parameters.Glossiness ?? parameters.Shininess ?? 0.25f).ToString("F2");
                materialInfoPanel.MaterialGlossinessIntensitySlider.Value = parameters.Glossiness ?? parameters.Shininess ?? 0.25;

                // Tint colors
                SetTintColor(materialInfoPanel.MaterialDiffuseTintCheckBox, materialInfoPanel.MaterialTintColorRect, materialInfoPanel.TintColorPicker, parameters.DiffuseTint, parameters.Diffuse);
                SetTintColor(materialInfoPanel.MaterialAOTintCheckBox, materialInfoPanel.MaterialAOTintColorRect, materialInfoPanel.AOTintColorPicker, parameters.AOTint, parameters.AOColor);
                SetTintColor(materialInfoPanel.MaterialSpecularTintCheckBox, materialInfoPanel.MaterialSpecularTintColorRect, materialInfoPanel.TintSpecularColorPicker, parameters.SpecularTint, parameters.Specular);
            });
        }

        private void UpdateTextureHyperlink(Hyperlink hyperlink, int? mapId, MaterialResource material) {
            if (hyperlink == null) return;

            hyperlink.DataContext = material;

            if (mapId.HasValue) {
                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == mapId.Value);
                hyperlink.NavigateUri = new Uri($"texture://{mapId.Value}");
                hyperlink.Inlines.Clear();
                hyperlink.Inlines.Add(texture?.Name ?? $"ID:{mapId.Value}");
            } else {
                hyperlink.NavigateUri = null;
                hyperlink.Inlines.Clear();
                hyperlink.Inlines.Add("-");
            }
        }

        private static void SetTintColor(CheckBox checkBox, TextBox colorRect, ColorPicker colorPicker, bool isTint, System.Collections.Generic.List<float>? colorValues) {
            checkBox.IsChecked = isTint;
            if (isTint && colorValues != null && colorValues.Count >= 3) {
                Color color = Color.FromRgb(
                    (byte)(colorValues[0] * 255),
                    (byte)(colorValues[1] * 255),
                    (byte)(colorValues[2] * 255)
                );
                colorRect.Background = new SolidColorBrush(color);
                colorRect.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

                // Sync selected color to ColorPicker
                colorPicker.SelectedColor = color;
            } else {
                colorRect.Background = new SolidColorBrush(Colors.Transparent);
                colorRect.Text = "No Tint";
                colorPicker.SelectedColor = null;
            }
        }

        private void MaterialMapHyperlink_Click(object sender, RoutedEventArgs e) {
            if (sender is not Hyperlink link || link.NavigateUri == null) return;
            if (!string.Equals(link.NavigateUri.Scheme, "texture", StringComparison.OrdinalIgnoreCase)) return;

            string idText = link.NavigateUri.AbsoluteUri.Replace("texture://", string.Empty);
            if (int.TryParse(idText, out int textureId)) {
                NavigateToTextureById(textureId);
            }
        }

        private void NavigateToMaterialTexture(string textureType) {
            var material = MaterialsDataGrid.SelectedItem as MaterialResource;
            if (material == null) return;

            int? textureId = textureType switch {
                "Diffuse" => material.DiffuseMapId,
                "Normal" => material.NormalMapId,
                "AO" => material.AOMapId,
                "Gloss" => material.GlossMapId,
                "Metalness" => material.MetalnessMapId,
                "Emissive" => material.EmissiveMapId,
                "Opacity" => material.OpacityMapId,
                _ => null
            };
            NavigateToTextureById(textureId);
        }

        private void NavigateToTextureById(int? textureId) {
            if (!textureId.HasValue) return;

            _ = Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                }

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Centralized helper to update the MaterialMasterComboBox with proper ItemsSource and selection.
        /// Ensures ItemsSource is set before selection to prevent WPF binding issues.
        /// </summary>
        private void UpdateMasterMaterialComboBox(string? masterName) {
            _isUpdatingMasterComboBox = true;
            try {
                var comboBox = materialInfoPanel.MaterialMasterComboBox;

                // Always populate ItemsSource first with master names as strings
                var masters = viewModel.MasterMaterialsViewModel.MasterMaterials;
                var masterNames = masters.Select(m => m.Name).ToList();

                logger.Info($"UpdateMasterMaterialComboBox: masterNames count={masterNames.Count}, selecting='{masterName}'");

                // Set ItemsSource (will clear selection)
                comboBox.ItemsSource = masterNames;

                // Now set selection
                if (!string.IsNullOrEmpty(masterName) && masterNames.Contains(masterName)) {
                    comboBox.SelectedItem = masterName;
                    logger.Info($"UpdateMasterMaterialComboBox: SelectedItem set to '{masterName}', SelectedIndex={comboBox.SelectedIndex}");
                } else {
                    comboBox.SelectedIndex = -1;
                    logger.Info($"UpdateMasterMaterialComboBox: Cleared selection (masterName='{masterName}' not found)");
                }

                // Force layout update to ensure visual refresh
                comboBox.UpdateLayout();
            } finally {
                _isUpdatingMasterComboBox = false;
            }
        }

        private void MaterialMasterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Skip if we're programmatically updating the ComboBox
            if (_isUpdatingMasterComboBox) return;
            if (materialInfoPanel.MaterialMasterComboBox.SelectedItem is not string masterName) return;

            // Apply to ALL selected materials (group assignment)
            var selectedMaterials = MaterialsDataGrid.SelectedItems.Cast<MaterialResource>().ToList();
            if (selectedMaterials.Count == 0) return;

            logger.Info($"MaterialMasterComboBox_SelectionChanged: Applying master '{masterName}' to {selectedMaterials.Count} materials");

            foreach (var material in selectedMaterials) {
                material.MasterMaterialName = masterName;
                viewModel.MasterMaterialsViewModel.SetMasterForMaterial(material.ID, masterName);
            }

            // Refresh DataGrid to show updated values
            MaterialsDataGrid.Items.Refresh();
        }

        private void TexturePreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            if (sender is not System.Windows.Controls.Image image) {
                logger.Warn("TexturePreview_MouseLeftButtonUp received unexpected type {SenderType}, expected Image.", sender.GetType().FullName);
                return;
            }

            MaterialResource? material = MaterialsDataGrid.SelectedItem as MaterialResource;
            if (material == null) {
                logger.Warn("Cannot determine material for texture navigation.");
                return;
            }

            string textureType = image.Tag as string ?? "";
            int? textureId = textureType switch {
                "AO" => material.AOMapId,
                "Diffuse" => material.DiffuseMapId,
                "Normal" => material.NormalMapId,
                "Specular" => material.SpecularMapId,
                "Metalness" => material.MetalnessMapId,
                "Gloss" => material.GlossMapId,
                "Emissive" => material.EmissiveMapId,
                "Opacity" => material.OpacityMapId,
                _ => null
            };

            if (!textureId.HasValue) {
                logger.Info("Material {MaterialName} ({MaterialId}) has no texture of type {TextureType}.",
                    material.Name, material.ID, textureType);
                return;
            }

            logger.Info("Click on texture preview {TextureType} with ID {TextureId} from material {MaterialName} ({MaterialId}).",
                textureType, textureId.Value, material.Name, material.ID);

            _ = Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                    logger.Debug("Switched to Textures tab via TabControl.");
                }

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("Texture {TextureName} (ID {TextureId}) selected and scrolled into view.", texture.Name, texture.ID);
                } else {
                    logger.Error("Texture with ID {TextureId} not found in collection. Total textures: {TextureCount}.", textureId.Value, viewModel.Textures.Count);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private async void MaterialsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            await UiAsyncHelper.ExecuteAsync(async () => {
                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    logger.Info($"MaterialsDataGrid_SelectionChanged: material={selectedMaterial.Name}");

                    viewModel.SelectedMaterial = selectedMaterial;
                    UpdateMasterMaterialComboBox(selectedMaterial.MasterMaterialName);
                    await viewModel.MaterialSelection.SelectMaterialCommand.ExecuteAsync(selectedMaterial);
                }
            }, nameof(MaterialsDataGrid_SelectionChanged));
        }

        private void SetTextureImage(System.Windows.Controls.Image imageControl, int? textureId) {
            if (textureId.HasValue) {
                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
                if (texture != null && File.Exists(texture.Path)) {
                    BitmapImage bitmapImage = new(new Uri(texture.Path));
                    imageControl.Source = bitmapImage;
                } else {
                    imageControl.Source = null;
                }
            } else {
                imageControl.Source = null;
            }
        }

        private void TintColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e) {
            if (e.NewValue.HasValue) {
                Color color = e.NewValue.Value;
                Color mediaColor = Color.FromArgb(color.A, color.R, color.G, color.B);

                materialInfoPanel.MaterialTintColorRect.Background = new SolidColorBrush(mediaColor);
                materialInfoPanel.MaterialTintColorRect.Text = $"#{mediaColor.A:X2}{mediaColor.R:X2}{mediaColor.G:X2}{mediaColor.B:X2}";

                double brightness = (mediaColor.R * 0.299 + mediaColor.G * 0.587 + mediaColor.B * 0.114) / 255;
                materialInfoPanel.MaterialTintColorRect.Foreground = new SolidColorBrush(brightness > 0.5 ? Colors.Black : Colors.White);

                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    selectedMaterial.DiffuseTint = true;
                    selectedMaterial.Diffuse = [mediaColor.R, mediaColor.G, mediaColor.B];
                }
            }
        }

        private void AOTintColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e) {
            if (e.NewValue.HasValue) {
                Color newColor = e.NewValue.Value;
                materialInfoPanel.MaterialAOTintColorRect.Background = new SolidColorBrush(newColor);
                materialInfoPanel.MaterialAOTintColorRect.Text = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";

                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    selectedMaterial.AOTint = true;
                    selectedMaterial.AOColor = [newColor.R, newColor.G, newColor.B];
                }
            }
        }

        private void TintSpecularColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e) {
            if (e.NewValue.HasValue) {
                Color newColor = e.NewValue.Value;
                materialInfoPanel.MaterialSpecularTintColorRect.Background = new SolidColorBrush(newColor);
                materialInfoPanel.MaterialSpecularTintColorRect.Text = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";

                // Update material specular
                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    selectedMaterial.SpecularTint = true;
                    selectedMaterial.Specular = [newColor.R, newColor.G, newColor.B];
                }
            }
        }

        #endregion
    }
}
