"""绘制壁纸工坊图标并导出抗锯齿 PNG 与 Windows ICO。

@author bomo
用途：以高分辨率绘制壁纸山景图标，缩小后得到平滑边缘，并生成多尺寸图标。
参数：命令行可选输出目录；未指定时写入项目 Assets 目录。
返回：成功时返回 0；文件系统错误时向终端报告错误并返回 1。
边界：生成图标需要 Pillow；应用编译使用已生成的 PNG 和 ICO，不依赖 Pillow。
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


AUTHOR = "bomo"
MASTER_SIZE = 1024
SUPERSAMPLE = 3


def _scaled_box(box: tuple[int, int, int, int], scale: int) -> tuple[int, int, int, int]:
    """把 1024 像素设计坐标转换为超采样画布坐标。

    @author bomo
    参数：box 是设计稿中的左、上、右、下坐标；scale 是放大倍数。
    返回：对应的高分辨率坐标元组。
    异常：不主动抛出异常；输入须为四个整数和正整数倍数。
    边界：不验证坐标是否位于画布内，Pillow 会按画布边界裁剪。
    """
    return tuple(value * scale for value in box)  # type: ignore[return-value]


def _vertical_gradient(size: int, top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    """生成由顶部颜色平滑过渡到底部颜色的 RGB 渐变底图。

    @author bomo
    参数：size 是正方形边长；top 与 bottom 是 RGB 颜色元组。
    返回：RGBA 渐变图片。
    异常：画布尺寸过大或内存不足时可能抛出 MemoryError。
    边界：尺寸小于 2 时使用顶部颜色填充。
    """
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    last_y = max(size - 1, 1)
    for y in range(size):
        amount = y / last_y
        color = tuple(round(start + (end - start) * amount) for start, end in zip(top, bottom))
        draw.line((0, y, size, y), fill=(*color, 255))
    return image


def draw_icon(size: int = MASTER_SIZE, supersample: int = SUPERSAMPLE) -> Image.Image:
    """绘制蓝色底板、白色相纸和日照山景组成的应用图标。

    @author bomo
    参数：size 是最终方形像素尺寸；supersample 是绘制时的放大倍数。
    返回：带透明圆角和抗锯齿边缘的 RGBA 图像。
    异常：尺寸无效时抛出 ValueError；内存不足时可能抛出 MemoryError。
    边界：至少输出 32 像素并使用至少 2 倍超采样，以保留圆角细节。
    """
    if size < 32 or supersample < 2:
        raise ValueError("图标边长须至少为 32，超采样倍数须至少为 2。")

    scale = supersample
    canvas_size = MASTER_SIZE * scale
    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    # 蓝色圆角底板使用透明遮罩，超采样缩小时会自然平滑四个角。
    base_mask = Image.new("L", (canvas_size, canvas_size), 0)
    mask_draw = ImageDraw.Draw(base_mask)
    mask_draw.rounded_rectangle(_scaled_box((20, 20, 1004, 1004), scale), radius=220 * scale, fill=255)
    base = _vertical_gradient(canvas_size, (74, 174, 255), (24, 91, 211))
    base.putalpha(base_mask)
    canvas.alpha_composite(base)

    # 相纸投影先模糊，再叠加白色相纸主体，形成轻盈的悬浮感。
    shadow = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    shadow_draw.rounded_rectangle(
        _scaled_box((198, 155, 826, 875), scale),
        radius=94 * scale,
        fill=(9, 38, 95, 92),
    )
    shadow = shadow.filter(ImageFilter.GaussianBlur(18 * scale))
    canvas.alpha_composite(shadow)

    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle(
        _scaled_box((190, 136, 818, 856), scale),
        radius=92 * scale,
        fill=(250, 252, 255, 255),
        outline=(224, 237, 252, 255),
        width=4 * scale,
    )

    # 圆角取景框与天空渐变。
    view_box = (252, 210, 756, 688)
    view_mask = Image.new("L", (canvas_size, canvas_size), 0)
    ImageDraw.Draw(view_mask).rounded_rectangle(
        _scaled_box(view_box, scale), radius=56 * scale, fill=255
    )
    sky = _vertical_gradient(canvas_size, (193, 231, 255), (238, 248, 255))
    sky.putalpha(view_mask)
    canvas.alpha_composite(sky)

    draw = ImageDraw.Draw(canvas)
    sun_box = _scaled_box((573, 278, 674, 379), scale)
    draw.ellipse(sun_box, fill=(255, 195, 95, 255))

    # 三层山脊以高分辨率多边形绘制，缩小时保留柔和的抗锯齿斜边。
    far_ridge = [
        (252, 566), (344, 465), (410, 515), (503, 360),
        (583, 471), (646, 408), (756, 523), (756, 688), (252, 688),
    ]
    middle_ridge = [
        (252, 574), (349, 512), (424, 559), (515, 449),
        (601, 535), (671, 475), (756, 549), (756, 688), (252, 688),
    ]
    front_ridge = [
        (252, 625), (350, 552), (435, 615), (520, 534),
        (609, 602), (682, 540), (756, 593), (756, 688), (252, 688),
    ]
    for points, color in (
        (far_ridge, (140, 190, 237, 255)),
        (middle_ridge, (89, 147, 224, 255)),
        (front_ridge, (43, 104, 204, 255)),
    ):
        draw.polygon([(x * scale, y * scale) for x, y in points], fill=color)

    # 取景框下方的短线作为相片说明，避免缩小后出现难辨的文字细节。
    draw.rounded_rectangle(_scaled_box((252, 744, 450, 770), scale), radius=13 * scale, fill=(83, 135, 202, 255))
    draw.rounded_rectangle(_scaled_box((252, 792, 568, 808), scale), radius=8 * scale, fill=(220, 231, 244, 255))

    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def write_icons(output_directory: Path) -> tuple[Path, Path]:
    """保存主 PNG 与包含多种尺寸的 Windows ICO 文件。

    @author bomo
    参数：output_directory 是输出图标文件的目录。
    返回：PNG 和 ICO 文件的完整路径。
    异常：目录创建或写入失败时抛出 OSError。
    边界：输出固定命名为 AppIcon.png 与 AppIcon.ico，已有同名图标会被覆盖。
    """
    output_directory.mkdir(parents=True, exist_ok=True)
    master = draw_icon()
    png_path = output_directory / "AppIcon.png"
    ico_path = output_directory / "AppIcon.ico"
    master.save(png_path, format="PNG", optimize=True)
    master.save(
        ico_path,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    return png_path, ico_path


def main() -> int:
    """解析输出目录并运行图标生成流程。

    @author bomo
    参数：无；命令行可选 --output-dir 指定文件输出目录。
    返回：成功为 0，写入失败为 1。
    异常：命令行参数错误由 argparse 终止程序。
    边界：默认仅覆盖项目 Assets 目录中的两个生成图标文件。
    """
    default_directory = Path(__file__).resolve().parents[1] / "Assets"
    parser = argparse.ArgumentParser(description="生成壁纸工坊的平滑 PNG 与 Windows 图标。")
    parser.add_argument("--output-dir", type=Path, default=default_directory)
    args = parser.parse_args()

    try:
        png_path, ico_path = write_icons(args.output_dir)
    except OSError as error:
        print(f"图标写入失败：{error}", file=sys.stderr)
        return 1

    print(f"已生成：{png_path}")
    print(f"已生成：{ico_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
