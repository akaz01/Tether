# Tether 아이콘 시안 세 가지를 그려 비교 이미지로 저장한다. 실행: python design\make-icons.py
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

SIZE = 2048
CORNER = 440
OUT = Path(__file__).parent
FONT_PATH = r"C:\Windows\Fonts\malgunbd.ttf"
WHITE = (255, 255, 255, 255)
GREEN = (74, 222, 128, 255)


def gradient(top, bottom):
    column = Image.new("RGBA", (1, SIZE))
    for y in range(SIZE):
        ratio = y / (SIZE - 1)
        column.putpixel((0, y), tuple(round(a + (b - a) * ratio) for a, b in zip(top, bottom)) + (255,))
    return column.resize((SIZE, SIZE))


def rounded_tile(top, bottom):
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), CORNER, fill=255)
    tile = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    tile.paste(gradient(top, bottom), (0, 0), mask)
    return tile


def cap(draw, center, radius, angle, width, color):
    x = center[0] + radius * math.cos(math.radians(angle))
    y = center[1] + radius * math.sin(math.radians(angle))
    half = width / 2
    draw.ellipse((x - half, y - half, x + half, y + half), fill=color)


def arc(draw, center, radius, width, color, start=225, end=315):
    x, y = center
    draw.arc((x - radius, y - radius, x + radius, y + radius), start, end, fill=color, width=width)
    for angle in (start, end):
        cap(draw, center, radius - width / 2, angle, width, color)


def concept_closed_lid():
    """닫힌 노트북 위로 신호가 살아 있는 모습."""
    tile = rounded_tile((30, 36, 48), (12, 15, 22))
    draw = ImageDraw.Draw(tile)
    center = (1024, 1180)
    arc(draw, center, 760, 150, GREEN)
    arc(draw, center, 500, 150, GREEN)
    draw.ellipse((924, 1000, 1124, 1200), fill=GREEN)
    draw.rounded_rectangle((344, 1370, 1704, 1490), 60, fill=WHITE)
    draw.rounded_rectangle((264, 1520, 1784, 1640), 60, fill=(148, 163, 184, 255))
    return tile


def concept_antenna_t():
    """와이파이 부채꼴을 윗변으로 삼은 글자 T."""
    tile = rounded_tile((37, 99, 235), (30, 58, 138))
    draw = ImageDraw.Draw(tile)
    center = (1024, 1110)
    arc(draw, center, 800, 170, WHITE)
    arc(draw, center, 520, 170, WHITE)
    draw.rounded_rectangle((939, 760, 1109, 1640), 85, fill=WHITE)
    draw.ellipse((884, 1500, 1164, 1780), fill=GREEN)
    return tile


def concept_link():
    """두 고리가 맞물린 연결 표시."""
    tile = rounded_tile((20, 184, 166), (15, 90, 110))
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    draw.rounded_rectangle((300, 744, 1180, 1304), 280, outline=WHITE, width=170)
    draw.rounded_rectangle((868, 744, 1748, 1304), 280, outline=WHITE, width=170)
    # 뒤로 지나가는 구간을 지워 맞물린 것처럼 보이게 한다.
    keep = Image.new("L", (SIZE, SIZE), 255)
    ImageDraw.Draw(keep).rectangle((1000, 1080, 1230, 1330), fill=0)
    layer.putalpha(Image.composite(layer.getchannel("A"), Image.new("L", (SIZE, SIZE), 0), keep))
    tile.alpha_composite(layer.rotate(45, resample=Image.BICUBIC))
    return tile


INK = (24, 28, 38, 255)
BLUSH = (251, 146, 160, 255)
SLATE = (148, 163, 184, 255)
MINT = (94, 234, 212, 255)
CREAM = (253, 224, 180, 255)
ORANGE = (251, 146, 60, 255)


def closed_laptop(draw, top):
    draw.rounded_rectangle((344, top, 1704, top + 120), 60, fill=WHITE)
    draw.rounded_rectangle((264, top + 150, 1784, top + 270), 60, fill=SLATE)


def eye(draw, x, y, radius):
    draw.ellipse((x - radius, y - radius * 1.2, x + radius, y + radius * 1.2), fill=INK)
    glint = radius * 0.38
    draw.ellipse((x - glint * 1.4, y - radius * 0.85, x + glint * 0.6, y - radius * 0.85 + glint * 2), fill=WHITE)


def face(draw, center_x, center_y, spread, radius):
    eye(draw, center_x - spread, center_y, radius)
    eye(draw, center_x + spread, center_y, radius)
    cheek_y = center_y + radius * 1.5
    for side in (-1, 1):
        x = center_x + side * (spread + radius * 1.1)
        draw.ellipse((x - radius * 0.8, cheek_y - radius * 0.5, x + radius * 0.8, cheek_y + radius * 0.5), fill=BLUSH)
    mouth = radius * 0.9
    mouth_y = center_y + radius * 0.9
    draw.arc((center_x - mouth, mouth_y - mouth, center_x + mouth, mouth_y + mouth), 30, 150, fill=INK, width=int(radius * 0.32))


def concept_robot():
    """닫힌 노트북 위에 앉아 신호를 보내는 작은 로봇."""
    tile = rounded_tile((30, 36, 48), (12, 15, 22))
    draw = ImageDraw.Draw(tile)
    signal = (1024, 520)
    arc(draw, signal, 340, 90, GREEN)
    arc(draw, signal, 190, 90, GREEN)
    draw.rounded_rectangle((994, 500, 1054, 760), 30, fill=SLATE)
    draw.ellipse((954, 440, 1094, 580), fill=GREEN)
    draw.rounded_rectangle((584, 720, 1464, 1420), 330, fill=MINT)
    face(draw, 1024, 1040, 200, 84)
    closed_laptop(draw, 1400)
    return tile


def concept_awake_lid():
    """위에서 본 닫힌 노트북이 눈을 뜨고 있는 캐릭터."""
    tile = rounded_tile((30, 36, 48), (12, 15, 22))
    draw = ImageDraw.Draw(tile)
    signal = (1024, 640)
    arc(draw, signal, 420, 110, GREEN)
    arc(draw, signal, 240, 110, GREEN)
    draw.rounded_rectangle((304, 660, 1744, 1620), 150, fill=WHITE)
    draw.rounded_rectangle((304, 1540, 1744, 1760), 90, fill=SLATE)
    draw.rounded_rectangle((304, 660, 1744, 1620), 150, fill=WHITE)
    face(draw, 1024, 1070, 280, 110)
    return tile


def awake_lid_small():
    """작은 크기용: 신호를 빼고 얼굴을 키워 16픽셀에서도 알아보게 한다."""
    tile = rounded_tile((30, 36, 48), (12, 15, 22))
    draw = ImageDraw.Draw(tile)
    draw.rounded_rectangle((180, 1500, 1868, 1860), 130, fill=SLATE)
    draw.rounded_rectangle((180, 240, 1868, 1720), 220, fill=WHITE)
    for side in (-1, 1):
        x = 1024 + side * 380
        draw.ellipse((x - 190, 700, x + 190, 1240), fill=INK)
    return tile


def save_app_icon():
    large = concept_awake_lid()
    small = awake_lid_small()
    frames = [small.resize((size, size), Image.LANCZOS) for size in (16, 24, 32)]
    frames += [large.resize((size, size), Image.LANCZOS) for size in (48, 64, 128, 256)]
    frames[-1].save(OUT.parent / "Tether.ico", format="ICO", append_images=frames[:-1],
                    sizes=[frame.size for frame in frames])


def concept_cat():
    """닫힌 노트북 위에 식빵처럼 앉아 귀를 세운 고양이."""
    tile = rounded_tile((30, 36, 48), (12, 15, 22))
    draw = ImageDraw.Draw(tile)
    signal = (1024, 500)
    arc(draw, signal, 330, 90, GREEN)
    arc(draw, signal, 180, 90, GREEN)
    draw.ellipse((964, 440, 1084, 560), fill=GREEN)
    for side in (-1, 1):
        base = 1024 + side * 300
        draw.polygon([(base - 190, 860), (base + side * 110, 520), (base + 190, 860)], fill=ORANGE)
        draw.polygon([(base - 95, 830), (base + side * 80, 640), (base + 95, 830)], fill=BLUSH)
    draw.rounded_rectangle((484, 680, 1564, 1420), 370, fill=ORANGE)
    draw.ellipse((744, 1090, 1304, 1430), fill=CREAM)
    face(draw, 1024, 1010, 240, 84)
    for side in (-1, 1):
        for offset in (-34, 34):
            start = 1024 + side * 400
            draw.line((start, 1070 + offset, start + side * 170, 1050 + offset * 2), fill=CREAM, width=20)
    closed_laptop(draw, 1400)
    return tile


def sheet(concepts):
    canvas = Image.new("RGBA", (1800, 860), (243, 245, 249, 255))
    draw = ImageDraw.Draw(canvas)
    title_font = ImageFont.truetype(FONT_PATH, 34)
    small_font = ImageFont.truetype(FONT_PATH, 22)
    for index, (label, description, tile) in enumerate(concepts):
        left = 60 + index * 590
        canvas.alpha_composite(tile.resize((480, 480), Image.LANCZOS), (left, 60))
        draw.text((left, 570), label, font=title_font, fill=(30, 41, 59, 255))
        draw.text((left, 622), description, font=small_font, fill=(71, 85, 105, 255))
        x = left
        for size in (64, 48, 32, 16):
            canvas.alpha_composite(tile.resize((size, size), Image.LANCZOS), (x, 690 + (64 - size)))
            x += size + 26
    return canvas


def main():
    save_app_icon()
    print("saved Tether.ico")
    if "--sheets" in sys.argv:
        save_sheets()


def save_sheets():
    concepts = [
        ("A. 닫힌 뚜껑", "뚜껑은 닫혀도 연결은 살아 있음", concept_closed_lid()),
        ("B. 안테나 T", "이름 첫 글자와 와이파이 신호", concept_antenna_t()),
        ("C. 연결 고리", "테더의 뜻인 묶어 둔 연결", concept_link()),
    ]
    for key, (_, _, tile) in zip("abc", concepts):
        tile.resize((512, 512), Image.LANCZOS).save(OUT / f"icon-{key}.png")
    sheet(concepts).convert("RGB").save(OUT / "icon-options.png")

    characters = [
        ("D. 로봇 친구", "닫힌 노트북 위에서 신호를 보냄", concept_robot()),
        ("E. 깨어 있는 뚜껑", "닫혀 있지만 눈은 뜨고 있음", concept_awake_lid()),
        ("F. 식빵 고양이", "노트북 위에서 귀를 세운 고양이", concept_cat()),
    ]
    for key, (_, _, tile) in zip("def", characters):
        tile.resize((512, 512), Image.LANCZOS).save(OUT / f"icon-{key}.png")
    sheet(characters).convert("RGB").save(OUT / "icon-options-2.png")


if __name__ == "__main__":
    main()
