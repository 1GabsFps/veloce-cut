using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Veloce.Core.Models;

namespace Veloce.App.Views;

public partial class PresetEditorWindow : Window
{
    private readonly BlurPreset _preset;
    private readonly string _presetsFilePath;
    private readonly Action<BlurPreset, bool> _onSave;
    private readonly Action<BlurPreset>? _onDelete;

    public PresetEditorWindow(
        BlurPreset preset,
        string presetsFilePath,
        bool canDelete,
        Action<BlurPreset, bool> onSave,
        Action<BlurPreset>? onDelete = null)
    {
        InitializeComponent();
        _preset = preset;
        _presetsFilePath = presetsFilePath;
        _onSave = onSave;
        _onDelete = onDelete;

        BtnDelete.IsEnabled = canDelete;

        // 1. Identificação
        TxtName.Text = preset.Name;

        // 2. Modo de Blur (Weighting)
        SelectComboBoxByTag(CmbWeighting, string.IsNullOrEmpty(preset.BlurWeighting) ? "gaussian" : preset.BlurWeighting);

        // 3. Sliders de Intensidade e Gamma
        SliderBlurAmount.Value = preset.BlurAmount > 0 ? preset.BlurAmount : 1.35;
        SliderBlurGamma.Value = preset.BlurGamma > 0 ? preset.BlurGamma : 1.40;

        // 4. Taxa de Quadros
        SliderOutputFps.Value = preset.OutputFps > 0 ? preset.OutputFps : 60;

        // 5. Interpolação
        SelectComboBoxByTag(CmbInterpolationMethod, string.IsNullOrEmpty(preset.InterpolationMethod) ? "svp" : preset.InterpolationMethod);
        TxtInterpolatedFps.Text = string.IsNullOrEmpty(preset.InterpolatedFps) ? "1440" : preset.InterpolatedFps;

        // 6. Qualidade
        SliderQuality.Value = preset.Quality > 0 ? preset.Quality : 14;
    }

    private static void SelectComboBoxByTag(ComboBox comboBox, string tagValue)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem cbItem && string.Equals(cbItem.Tag?.ToString(), tagValue, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = cbItem;
                return;
            }
        }
        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private BlurPreset BuildPresetFromFields(string? newId = null)
    {
        string weighting = "gaussian";
        if (CmbWeighting.SelectedItem is ComboBoxItem selectedWeighting)
        {
            weighting = selectedWeighting.Tag?.ToString() ?? "gaussian";
        }

        string method = "svp";
        if (CmbInterpolationMethod.SelectedItem is ComboBoxItem selectedMethod)
        {
            method = selectedMethod.Tag?.ToString() ?? "svp";
        }

        return new BlurPreset
        {
            Id = newId ?? _preset.Id,
            Name = TxtName.Text.Trim(),
            BlurAmount = Math.Round(SliderBlurAmount.Value, 2),
            BlurGamma = Math.Round(SliderBlurGamma.Value, 2),
            BlurWeighting = weighting,
            OutputFps = (int)Math.Round(SliderOutputFps.Value),
            Interpolate = true,
            InterpolationMethod = method,
            InterpolatedFps = TxtInterpolatedFps.Text.Trim(),
            RifeModel = _preset.RifeModel,
            Quality = (int)Math.Round(SliderQuality.Value)
        };
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var updated = BuildPresetFromFields();
        _onSave(updated, false);
        DialogResult = true;
        Close();
    }

    private void OnSaveAsNewClicked(object sender, RoutedEventArgs e)
    {
        var newPreset = BuildPresetFromFields("preset_" + Guid.NewGuid().ToString("N")[..8]);
        if (string.Equals(newPreset.Name, _preset.Name, StringComparison.OrdinalIgnoreCase))
        {
            newPreset.Name += " (Novo)";
        }
        _onSave(newPreset, true);
        DialogResult = true;
        Close();
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            $"Deseja realmente excluir o preset '{_preset.Name}'?",
            "Confirmar Exclusão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _onDelete?.Invoke(_preset);
            DialogResult = true;
            Close();
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnOpenJsonClicked(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_presetsFilePath))
        {
            try
            {
                Process.Start("notepad.exe", _presetsFilePath);
            }
            catch
            {
                // Ignore failure
            }
        }
    }
}
