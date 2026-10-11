using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JBSWallpaperStudio;

/// <summary>
/// 提供壁纸选择、屏幕预览和原有设置核心的现代化界面。
/// @author bomo
/// </summary>
public partial class MainWindow : Window
{
    private const int FillPosition = 10;
    private const int FitPosition = 6;
    private const int StretchPosition = 2;
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint UpdateIniFile = 0x01;
    private const uint SendChange = 0x02;
    private const string DesktopRegistryPath = @"Control Panel\Desktop";
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

    private string? selectedImagePath;
    private int selectedPosition = FillPosition;

    /// <summary>
    /// 初始化窗口，并恢复上次选择的图片与显示方式。
    /// @author bomo
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Loaded += Window_Loaded;
    }

    /// <summary>
    /// 恢复本机保存的图片路径与显示方式。
    /// @author bomo
    /// </summary>
    /// <param name="sender">触发加载事件的窗口。</param>
    /// <param name="e">窗口加载事件参数。</param>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = LoadSettings();
        selectedPosition = settings.PositionCode is FillPosition or FitPosition or StretchPosition
            ? settings.PositionCode
            : FillPosition;

        FillModeButton.IsChecked = selectedPosition == FillPosition;
        FitModeButton.IsChecked = selectedPosition == FitPosition;
        StretchModeButton.IsChecked = selectedPosition == StretchPosition;

        if (!string.IsNullOrWhiteSpace(settings.ImagePath) && File.Exists(settings.ImagePath))
        {
            LoadImage(settings.ImagePath);
        }

        UpdatePreviewMode();
    }

    /// <summary>
    /// 打开本机文件选择对话框并载入用户选中的图片。
    /// @author bomo
    /// </summary>
    /// <param name="sender">触发选择操作的按钮。</param>
    /// <param name="e">按钮点击事件参数。</param>
    private void SelectImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择桌面壁纸",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadImage(dialog.FileName);
        }
    }

    /// <summary>
    /// 验证图片格式、载入缩放预览并保存当前选择。
    /// @author bomo
    /// </summary>
    /// <param name="path">要预览的本机图片完整路径。</param>
    /// <exception cref="IOException">图片文件读取失败。</exception>
    /// <exception cref="UnauthorizedAccessException">当前用户无权读取图片。</exception>
    private void LoadImage(string path)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            ShowStatus("暂不支持此图片格式，请选择 JPG、PNG、BMP 或 TIFF。", false);
            return;
        }

        try
        {
            var absolutePath = Path.GetFullPath(path);
            var frame = BitmapDecoder.Create(
                new Uri(absolutePath, UriKind.Absolute),
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.OnDemand).Frames[0];

            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.DecodePixelWidth = 1800;
            preview.UriSource = new Uri(absolutePath, UriKind.Absolute);
            preview.EndInit();
            preview.Freeze();

            selectedImagePath = absolutePath;
            PreviewImage.Source = preview;
            PreviewImage.Visibility = Visibility.Visible;
            EmptyPreview.Visibility = Visibility.Collapsed;
            PreviewFileNameText.Text = Path.GetFileName(absolutePath);
            PreviewDimensionsText.Text = $"{frame.PixelWidth} × {frame.PixelHeight}";

            var sizeMb = new FileInfo(absolutePath).Length / (1024d * 1024d);
            ImageNameText.Text = Path.GetFileName(absolutePath);
            ImageDetailsText.Text = $"{frame.PixelWidth} × {frame.PixelHeight} · {sizeMb:0.0} MB";
            ApplyWallpaperButton.IsEnabled = true;
            ShowStatus("图片已载入，可以调整预览并应用。");
            SaveSettings();
            UpdatePreviewMode();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            ShowStatus($"无法读取这张图片：{ex.Message}", false);
        }
    }

    /// <summary>
    /// 按当前桌面显示方式同步图片预览的缩放规则。
    /// @author bomo
    /// </summary>
    private void UpdatePreviewMode()
    {
        PreviewImage.Stretch = selectedPosition switch
        {
            FitPosition => Stretch.Uniform,
            StretchPosition => Stretch.Fill,
            _ => Stretch.UniformToFill
        };
    }

    /// <summary>
    /// 响应铺放方式变化，更新预览并保存本机偏好。
    /// @author bomo
    /// </summary>
    /// <param name="sender">触发选择的单选按钮。</param>
    /// <param name="e">选择状态变化事件参数。</param>
    private void PositionMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out var position))
        {
            return;
        }

        selectedPosition = position;
        UpdatePreviewMode();
        SaveSettings();
    }

    /// <summary>
    /// 直接把用户所选原图设为桌面背景，并验证 Windows 已记录该路径。
    /// @author bomo
    /// </summary>
    /// <param name="sender">触发应用操作的按钮。</param>
    /// <param name="e">按钮点击事件参数。</param>
    /// <exception cref="InvalidOperationException">Windows 未记录所选图片路径或桌面配置不可用。</exception>
    /// <exception cref="Win32Exception">Windows 壁纸接口报告失败。</exception>
    private void ApplyWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedImagePath is null || !File.Exists(selectedImagePath))
        {
            ShowStatus("请先选择一张可读取的图片。", false);
            return;
        }

        ApplyWallpaperButton.IsEnabled = false;
        ShowStatus("正在应用原图壁纸…");

        try
        {
            using (var desktopSettings = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath, writable: true)
                ?? throw new InvalidOperationException("无法打开当前用户的桌面显示设置。"))
            {
                desktopSettings.SetValue("WallpaperStyle", selectedPosition.ToString(), RegistryValueKind.String);
                desktopSettings.SetValue("TileWallpaper", "0", RegistryValueKind.String);
            }

            if (!SystemParametersInfo(
                    SpiSetDesktopWallpaper,
                    0,
                    selectedImagePath,
                    UpdateIniFile | SendChange))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未能设置所选图片为桌面壁纸。");
            }

            using var verificationSettings = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath, writable: false);
            var recordedPath = verificationSettings?.GetValue("Wallpaper") as string;
            if (!string.Equals(recordedPath, selectedImagePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Windows 接受了设置请求，但系统记录的壁纸路径与所选原图不一致。");
            }

            ShowStatus("已应用原图壁纸，Windows 已记录所选图片路径。");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or Win32Exception
                or System.Security.SecurityException)
        {
            ShowStatus($"应用失败：{ex.Message}", false);
        }
        finally
        {
            ApplyWallpaperButton.IsEnabled = selectedImagePath is not null;
        }
    }

    /// <summary>
    /// 接收拖入窗口的首张受支持图片，并按普通选择流程载入。
    /// @author bomo
    /// </summary>
    /// <param name="sender">接收文件的窗口。</param>
    /// <param name="e">包含拖入文件路径的数据。</param>
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
            && File.Exists(files[0]))
        {
            LoadImage(files[0]);
        }
    }

    /// <summary>
    /// 仅对包含本机文件的拖放操作显示可放置指示。
    /// @author bomo
    /// </summary>
    /// <param name="sender">接收拖放的窗口。</param>
    /// <param name="e">拖放操作数据。</param>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// 关闭壁纸工坊窗口。
    /// @author bomo
    /// </summary>
    /// <param name="sender">关闭按钮。</param>
    /// <param name="e">按钮点击事件参数。</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 将壁纸工坊最小化到任务栏。
    /// @author bomo
    /// </summary>
    /// <param name="sender">最小化按钮。</param>
    /// <param name="e">按钮点击事件参数。</param>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    /// <summary>
    /// 在普通窗口与最大化窗口之间切换。
    /// @author bomo
    /// </summary>
    /// <param name="sender">最大化或还原按钮。</param>
    /// <param name="e">按钮点击事件参数。</param>
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// 读取上次选择的图片路径和桌面铺放方式。
    /// @author bomo
    /// </summary>
    /// <returns>本机偏好；文件不存在或内容无效时返回默认值。</returns>
    private StudioSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new StudioSettings(null, FillPosition);
            }

            return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(SettingsPath))
                ?? new StudioSettings(null, FillPosition);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ShowStatus($"无法读取上次设置：{ex.Message}", false);
            return new StudioSettings(null, FillPosition);
        }
    }

    /// <summary>
    /// 保存最近使用的图片和铺放方式，供下次启动恢复。
    /// @author bomo
    /// </summary>
    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(new StudioSettings(selectedImagePath, selectedPosition)),
                new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"无法保存本机设置：{ex.Message}", false);
        }
    }

    /// <summary>
    /// 显示操作状态，并更新底部状态标记的颜色。
    /// @author bomo
    /// </summary>
    /// <param name="message">展示给用户的状态文字。</param>
    /// <param name="isSuccess">成功或普通状态为 true，错误状态为 false。</param>
    private void ShowStatus(string message, bool isSuccess = true)
    {
        StatusText.Text = message;
        StatusDot.Fill = isSuccess
            ? new SolidColorBrush(Color.FromRgb(52, 167, 83))
            : new SolidColorBrush(Color.FromRgb(230, 77, 72));
    }

    /// <summary>
    /// 返回新版应用保存在用户配置目录中的偏好文件路径。
    /// @author bomo
    /// </summary>
    /// <returns>当前用户本机设置文件的完整路径。</returns>
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JBSWallpaperStudio",
        "settings.json");

    /// <summary>
    /// 通过 Windows 壁纸接口应用图片，并按系统设置刷新 Explorer 桌面。
    /// @author bomo
    /// </summary>
    /// <param name="action">系统参数操作代码。</param>
    /// <param name="parameter">操作的数值参数；设置壁纸时为零。</param>
    /// <param name="wallpaperPath">要应用的图片完整路径。</param>
    /// <param name="flags">更新用户配置并通知系统刷新桌面的标志。</param>
    /// <returns>接口成功时为 true；失败时为 false，可通过 Win32 错误码诊断。</returns>
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string wallpaperPath, uint flags);

    /// <summary>
    /// 表示本机保存的最后图片路径和铺放方式。
    /// @author bomo
    /// </summary>
    /// <param name="ImagePath">上次选中的图片完整路径。</param>
    /// <param name="PositionCode">JBS 核心使用的铺放方式编号。</param>
    private sealed record StudioSettings(string? ImagePath, int PositionCode);
}
