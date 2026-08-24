from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"


def point_on_curve(t: float) -> tuple[float, float]:
    p0 = (132.0, 315.0)
    p1 = (132.0, 173.0)
    p2 = (380.0, 173.0)
    p3 = (380.0, 315.0)
    u = 1.0 - t
    x = u**3 * p0[0] + 3 * u**2 * t * p1[0] + 3 * u * t**2 * p2[0] + t**3 * p3[0]
    y = u**3 * p0[1] + 3 * u**2 * t * p1[1] + 3 * u * t**2 * p2[1] + t**3 * p3[1]
    return x, y


def render(size: int) -> Image.Image:
    scale = size / 512.0
    image = Image.new("RGBA", (size, size), "#0B1220")
    draw = ImageDraw.Draw(image)

    def box(values: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
        return tuple(round(value * scale) for value in values)

    curve = [(round(x * scale), round(y * scale)) for x, y in (point_on_curve(i / 80) for i in range(81))]
    draw.line(curve, fill="#38BDF8", width=max(2, round(34 * scale)), joint="curve")

    draw.rounded_rectangle(box((78, 240, 190, 416)), radius=round(22 * scale), fill="#F8FAFC")
    draw.rounded_rectangle(box((96, 264, 172, 380)), radius=max(1, round(8 * scale)), fill="#0B1220")
    draw.rounded_rectangle(box((122, 394, 146, 402)), radius=max(1, round(4 * scale)), fill="#0B1220")

    draw.rounded_rectangle(box((332, 250, 456, 354)), radius=round(12 * scale), fill="#F8FAFC")
    draw.rounded_rectangle(box((352, 270, 436, 332)), radius=max(1, round(5 * scale)), fill="#0B1220")
    stand = [(378, 354), (410, 354), (410, 374), (430, 374), (430, 390), (358, 390), (358, 374), (378, 374)]
    draw.polygon([(round(x * scale), round(y * scale)) for x, y in stand], fill="#F8FAFC")

    center = box((231, 171, 281, 221))
    draw.ellipse(center, fill="#22C55E", outline="#F8FAFC", width=max(1, round(9 * scale)))
    return image


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    master = render(512)
    master.save(ASSETS / "roamadb-icon.png", optimize=True)
    master.save(
        ASSETS / "roamadb-icon.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    main()
