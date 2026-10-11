# 壁纸工坊

一个使用 WPF 编写的 Windows 桌面壁纸工具，可选择本机图片、预览铺放方式，并将原图路径交给 Windows 设置为桌面背景。

## 功能

- 支持 JPG、PNG、BMP 与 TIFF 图片；可通过文件选择器或拖放载入。
- 提供填满屏幕、完整显示和拉伸铺满三种预览与铺放方式。
- 直接使用所选原图路径，不在应用中重新编码或压缩图片。
- 应用后核对 Windows 壁纸接口结果与系统记录的图片路径。
- 设置只保存在当前 Windows 用户的本机配置目录。

## 构建

需要 Windows、.NET 8 SDK 和 Windows Desktop Runtime。打开 PowerShell，在项目目录运行：

```powershell
dotnet publish .\JBSWallpaperStudio.csproj --configuration Release
```

重新绘制图标时需要 Python 与 Pillow：

```powershell
python -m pip install Pillow
python .\tools\generate_icon.py
```

图标生成器以 3 倍超采样绘制圆角，再使用 Lanczos 缩小并导出多尺寸 ICO，以减少图标圆角和斜边的锯齿。

## 项目说明

- 作者标记：`bomo`
- 当前仓库未附带开源许可证；公开代码不代表授予再使用或分发许可。
- 壁纸图片不会作为项目文件上传。
